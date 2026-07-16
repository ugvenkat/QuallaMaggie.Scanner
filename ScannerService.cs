using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QualMaggie.Scanner.Models;

namespace QualMaggie.Scanner.Services;

public class ScannerService
{
    private readonly string _connectionString;
    private readonly MarketRegimeService _regimeService = new();
    private readonly QualMaggieRules _rules;
    private readonly QualMaggieSettings _settings;
    private readonly ResultDirectorySettings _resultDirSettings;

    public ScannerService(
        IOptions<QualMaggieSettings> settings,
        QualMaggieRules rules,
        string connectionString,
        IOptions<ResultDirectorySettings> resultDirSettings)
    {
        _settings = settings.Value;
        _rules = rules;
        _connectionString = connectionString;
        _resultDirSettings = resultDirSettings.Value;
    }


    public async Task RunAsync()
    {
        Console.WriteLine("Starting QualMaggie Scanner...");
        DateTime startTime = DateTime.Now;
        DateTime scanDate = DateTime.UtcNow.Date;
        Guid runId = Guid.NewGuid();

        using var conn = new SqlConnection(_connectionString);
        conn.Open();

        // ---------------------------------------------------------
        // Load benchmark (QQQ)
        // ---------------------------------------------------------
        Console.WriteLine("Loading benchmark (QQQ)...");
        var benchmarkCandles = conn.Query<Candle>(
            @"SELECT Ticker, TradeDate, OpenPrice, HighPrice, LowPrice, ClosePrice, Volume
              FROM dbo.DailyPrice
              WHERE Ticker = @Ticker
              ORDER BY TradeDate",
            new { Ticker = "QQQ" }).ToList();

        if (benchmarkCandles.Count == 0)
        {
            Console.WriteLine("No benchmark data — scan aborted.");
            return;
        }

        string regime = _regimeService.GetRegime(benchmarkCandles);
        decimal benchmarkClose = benchmarkCandles.Last().ClosePrice;

        if (regime == "Bear")
        {
            Console.WriteLine("Market in Bear regime — scan skipped.");
            return;
        }

        // ---------------------------------------------------------
        // Load ALL candles in ONE query (fast)
        // ---------------------------------------------------------
        Console.WriteLine("Loading all candles (optimized single query)...");

        var allRows = conn.Query<Candle>(
            @"SELECT Ticker, TradeDate, OpenPrice, HighPrice, LowPrice, ClosePrice, Volume
              FROM dbo.DailyPrice
              ORDER BY Ticker, TradeDate").ToList();

        Console.WriteLine($"Loaded {allRows.Count:N0} rows.");

        var allCandles = allRows
            .GroupBy(r => r.Ticker)
            .ToDictionary(g => g.Key, g => g.ToList());

        Console.WriteLine($"Grouped into {allCandles.Count:N0} tickers.");

        // ---------------------------------------------------------
        // ⭐ PRECOMPUTE RS RETURNS ONCE (Fix O(N²))
        // ---------------------------------------------------------
        var allReturns = allCandles
            .Where(kvp => kvp.Value.Count > _settings.RsLookback)
            .ToDictionary(
                kvp => kvp.Key,
                kvp =>
                {
                    var list = kvp.Value;
                    decimal start = list[^_settings.RsLookback].ClosePrice;
                    decimal end = list.Last().ClosePrice;
                    return start > 0m ? (end - start) / start : 0m;
                });

        // ---------------------------------------------------------
        // Prepare hit buckets
        // ---------------------------------------------------------
        var elite = new List<string>();
        var strong = new List<string>();
        var moderate = new List<string>();
        var weak = new List<string>();

        using var tx = conn.BeginTransaction();

        // ---------------------------------------------------------
        // MAIN SCAN LOOP
        // ---------------------------------------------------------
        int processed = 0;
        int total = allCandles.Count;

        foreach (var kvp in allCandles)
        {
            string ticker = kvp.Key;
            var candles = kvp.Value;

            processed++;
            if (processed % 200 == 0)
                Console.WriteLine($"Scanning {processed}/{total}...");

            if (ticker == "QQQ")
                continue;

            if (candles.Count < 250)
                continue;

            var result = _rules.Evaluate(
                ticker,
                candles,
                scanDate,
                runId,
                regime,
                benchmarkCandles,
                allCandles,
                allReturns   // ⭐ NEW: pass precomputed RS returns
            );

            if (result == null)
                continue;

            if (result.SignalQuality == "Weak")
            {
                weak.Add(result.Ticker);
                continue;
            }

            switch (result.SignalQuality)
            {
                case "Elite": elite.Add(result.Ticker); break;
                case "Strong": strong.Add(result.Ticker); break;
                case "Moderate": moderate.Add(result.Ticker); break;
            }

            // ---------------------------------------------------------
            // FULL INSERT — matches dbo.ScannerResult schema
            // ---------------------------------------------------------
            conn.Execute(
                @"INSERT INTO dbo.ScannerResult
                  (Ticker, ScanDate, RunId, Signal, SignalQuality,
                   ClosePrice, PivotPrice, EMA10, EMA20, SMA50,
                   PercentMove, Volume, TrendCleanlinessScore,
                   VcpTightnessScore, VolumeDryUp, IsLiquid,
                   IsEpisodicPivot, IsDailyBreakout, IsNearPivotBreakout,
                   BaseQualityScore, PriorMoveScore, EarningsReactionScore,
                   RelativeStrengthScore, RsLine52WeekScore,
                   MarketRegime, CreatedAtUtc)
                  VALUES
                  (@Ticker, @ScanDate, @RunId, @Signal, @SignalQuality,
                   @ClosePrice, @PivotPrice, @EMA10, @EMA20, @SMA50,
                   @PercentMove, @Volume, @TrendCleanlinessScore,
                   @VcpTightnessScore, @VolumeDryUp, @IsLiquid,
                   @IsEpisodicPivot, @IsDailyBreakout, @IsNearPivotBreakout,
                   @BaseQualityScore, @PriorMoveScore, @EarningsReactionScore,
                   @RelativeStrengthScore, @RsLine52WeekScore,
                   @MarketRegime, @CreatedAtUtc)",
                new
                {
                    result.Ticker,
                    result.ScanDate,
                    result.RunId,
                    result.Signal,
                    result.SignalQuality,
                    result.ClosePrice,
                    result.PivotPrice,
                    result.EMA10,
                    result.EMA20,
                    result.SMA50,
                    result.PercentMove,
                    result.Volume,
                    result.TrendCleanlinessScore,
                    result.VcpTightnessScore,
                    result.VolumeDryUp,
                    result.IsLiquid,
                    result.IsEpisodicPivot,
                    result.IsDailyBreakout,
                    result.IsNearPivotBreakout,
                    result.BaseQualityScore,
                    result.PriorMoveScore,
                    result.EarningsReactionScore,
                    result.RelativeStrengthScore,
                    result.RsLine52WeekScore,
                    result.MarketRegime,
                    result.CreatedAtUtc
                },
                transaction: tx);
        }

        tx.Commit();

        // ---------------------------------------------------------
        // Summary Output
        // ---------------------------------------------------------
        DateTime endTime = DateTime.Now;
        TimeSpan duration = endTime - startTime;

        Console.WriteLine();
        Console.WriteLine($"Market Regime : {regime,-10} QQQ Close : {benchmarkClose}");
        Console.WriteLine();
        Console.WriteLine($"Scanner Hits — {DateTime.Now:MM/dd/yyyy  h:mm tt}");
        Console.WriteLine($"Elite   : {string.Join(" ", elite)}");
        Console.WriteLine($"Strong  : {string.Join(" ", strong)}");
        Console.WriteLine($"Moderate: {string.Join(" ", moderate)}");
        Console.WriteLine();
        Console.WriteLine(
            $"Duration  : {duration:hh\\:mm\\:ss}   " +
            $"Start: {startTime:hh\\:mm\\:ss tt}   " +
            $"End: {endTime:hh\\:mm\\:ss tt}");

        Console.WriteLine();
        QualMaggieRules.PrintRuleFailureBreakdown();

        WriteLogFile(
            $"Market Regime : {regime,-10} QQQ Close : {benchmarkClose}\n\n" +
            $"Scanner Hits — {DateTime.Now:MM/dd/yyyy  h:mm tt}\n" +
            $"Elite   : {string.Join(" ", elite)}\n" +
            $"Strong  : {string.Join(" ", strong)}\n" +
            $"Moderate: {string.Join(" ", moderate)}\n" +
            $"Duration  : {duration:hh\\:mm\\:ss}   " +
            $"Start: {startTime:hh\\:mm\\:ss tt}   " +
            $"End: {endTime:hh\\:mm\\:ss tt}\n");
    }

    private void WriteLogFile(string message)
    {
        try
        {
            // Dallas time (Central Time)
            TimeZoneInfo cst = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            DateTime dallasNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cst);

            // Directory from appsettings.json
            string logDir = _resultDirSettings.ResultDirectory;

            if (string.IsNullOrWhiteSpace(logDir))
            {
                Console.WriteLine("[Scanner Logging Error] ResultDirectory is not configured.");
                return;
            }

            Directory.CreateDirectory(logDir);

            // File name using Dallas date
            string filePath = Path.Combine(logDir, $"sr_{dallasNow:MMddyyyy}.log");

            File.AppendAllText(filePath, message + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Scanner Logging Error] {ex.Message}");
        }
    }

}
