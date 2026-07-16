using Microsoft.Extensions.Options;
using QualMaggie.Scanner.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QualMaggie.Scanner.Services;

public class QualMaggieRules
{
    private readonly QualMaggieSettings _s;

    private static readonly Dictionary<string, int> RuleFailCounts = new();

    public QualMaggieRules(IOptions<QualMaggieSettings> settings)
    {
        _s = settings.Value;
    }

    private static void LogFail(string ticker, List<string> reasons)
    {
        if (reasons.Count == 0) return;

        Console.WriteLine($"{ticker} failed: {string.Join(", ", reasons)}");

        foreach (var r in reasons)
        {
            if (!RuleFailCounts.ContainsKey(r))
                RuleFailCounts[r] = 0;
            RuleFailCounts[r]++;
        }
    }

    public static void PrintRuleFailureBreakdown()
    {
        Console.WriteLine();
        Console.WriteLine("Rule Failure Breakdown:");

        if (RuleFailCounts.Count == 0)
        {
            Console.WriteLine("  (no failures recorded)");
            return;
        }

        foreach (var kvp in RuleFailCounts.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
    }

    public ScanResult? Evaluate(
        string ticker,
        List<Candle> candles,
        DateTime scanDate,
        Guid runId,
        string marketRegime,
        List<Candle> benchmark,
        Dictionary<string, List<Candle>> allCandles,
        Dictionary<string, decimal> allReturns)
    {
        var failReasons = new List<string>();

        if (candles.Count < 80)
        {
            failReasons.Add("HISTORY");
            LogFail(ticker, failReasons);
            return null;
        }

        if (marketRegime == "Bear")
        {
            failReasons.Add("REGIME");
            LogFail(ticker, failReasons);
            return null;
        }

        var closes = candles.Select(c => c.ClosePrice).ToList();
        var ema10 = EmaCalculator.CalculateEMA(closes, 10);
        var ema20 = EmaCalculator.CalculateEMA(closes, 20);

        var sma50Array = MovingAverageCalculator.CalculateSMA(closes, 50);

        var atr = AtrCalculator.CalculateAtr(candles, _s.AtrPeriod);

        int i = candles.Count - 1;
        var today = candles[i];
        var yesterday = candles[i - 1];

        decimal trend = FeatureCalculators.CalculateTrendCleanliness(
            candles, ema10, ema20, _s.BaseLookback);

        decimal vcp = FeatureCalculators.CalculateVcpTightness(
            candles, _s.VcpLookback);

        bool volDry = FeatureCalculators.DetectVolumeDryUp(
            candles, _s.VolumeLookback, _s.VolumeDryUpRatio);

        bool liquid = FeatureCalculators.IsLiquid(
            candles, _s.MinPrice, _s.MinAvgVolume);

        bool maRespect = FeatureCalculators.RespectMovingAverages(
            candles, ema20, sma50Array, _s.MaRespectLookback);

        if (!maRespect)
        {
            failReasons.Add("MA_RESPECT");
            LogFail(ticker, failReasons);
            return null;
        }

        bool ep = FeatureCalculators.DetectEpisodicPivot(candles);

        var (dailyBreakout, nearPivotBreakout, pivot) =
            FeatureCalculators.DetectDailyBreakout(
                candles,
                _s.OrhLookback,
                _s.OrhCloseStrength,
                _s.AllowHighTouchBreakouts,
                _s.HighTouchTolerance);

        decimal baseQ = FeatureCalculators.CalculateBaseQuality(
            candles, _s.BaseLookback, _s.BaseMaxDepth, _s.BaseHigherLowWeight);

        decimal prior = FeatureCalculators.CalculatePriorMoveScore(candles);
        decimal earn = FeatureCalculators.CalculateEarningsReactionScore(candles);

        decimal postEarnTrend =
            FeatureCalculators.CalculatePostEarningsTrend(candles, _s.PostEarningsTrendDays);

        decimal rs = FeatureCalculators.CalculateRelativeStrengthPercentile(
            ticker, allReturns);

        decimal rs52 = 0m;
        int lookback52 = 252;

        if (candles.Count > lookback52 && benchmark.Count > lookback52)
        {
            var stockStart = candles[^lookback52].ClosePrice;
            var benchStart = benchmark[^lookback52].ClosePrice;
            var stockEnd = today.ClosePrice;
            var benchEnd = benchmark.Last().ClosePrice;

            if (stockStart > 0m && benchStart > 0m && benchEnd > 0m)
            {
                var rsStart = stockStart / benchStart;
                var rsEnd = stockEnd / benchEnd;
                if (rsStart > 0m)
                    rs52 = (rsEnd - rsStart) / rsStart;
            }
        }

        decimal percentMove =
            yesterday.ClosePrice > 0m
                ? (today.ClosePrice - yesterday.ClosePrice) / yesterday.ClosePrice
                : 0m;

        if (!liquid)
        {
            failReasons.Add("LIQUIDITY");
            LogFail(ticker, failReasons);
            return null;
        }

        if (rs < _s.RsMinScore)
        {
            failReasons.Add("RS_MIN");
            LogFail(ticker, failReasons);
            return null;
        }

        if (vcp < _s.VcpMinTightness)
        {
            failReasons.Add("VCP_MIN");
            LogFail(ticker, failReasons);
            return null;
        }

        if (baseQ <= 0m)
        {
            failReasons.Add("BASE_Q");
            LogFail(ticker, failReasons);
            return null;
        }

        if (!ep && !dailyBreakout && !nearPivotBreakout)
        {
            failReasons.Add("BREAKOUT");
            LogFail(ticker, failReasons);
            return null;
        }

        decimal atrVal = atr[i];
        if (atrVal <= 0m)
        {
            failReasons.Add("ATR_ZERO");
            LogFail(ticker, failReasons);
            return null;
        }

        if (today.HighPrice - today.LowPrice > _s.MaxStopAtr * atrVal)
        {
            failReasons.Add("ATR_STOP");
            LogFail(ticker, failReasons);
            return null;
        }

        decimal rawScore =
            baseQ * 2.0m +
            vcp * 2.0m +
            trend * 1.75m +
            postEarnTrend * 1.0m +
            rs * 1.5m +
            prior * 1.0m +
            earn * 0.75m +
            (ep ? 0.75m : 0m) +
            (dailyBreakout ? 0.5m : 0m) +
            (nearPivotBreakout ? 0.25m : 0m) +
            (volDry ? 0.5m : 0m);

        decimal score = Math.Clamp(rawScore, 0m, 10m);

        string quality =
            score >= _s.EliteMinScore ? "Elite" :
            score >= _s.StrongMinScore ? "Strong" :
            score >= _s.ModerateMinScore ? "Moderate" :
            "Weak";

        return new ScanResult
        {
            Ticker = ticker,
            ScanDate = scanDate,
            RunId = runId,
            Signal = ep ? "EP" : "Breakout",
            SignalQuality = quality,

            ClosePrice = today.ClosePrice,
            PivotPrice = pivot,
            EMA10 = ema10[i],
            EMA20 = ema20[i],
            SMA50 = sma50Array[i],

            PercentMove = percentMove,
            Volume = today.Volume,

            TrendCleanlinessScore = trend,
            VcpTightnessScore = vcp,
            VolumeDryUp = volDry,
            IsLiquid = liquid,
            IsEpisodicPivot = ep,
            IsDailyBreakout = dailyBreakout,
            IsNearPivotBreakout = nearPivotBreakout,

            BaseQualityScore = baseQ,
            PriorMoveScore = prior,
            EarningsReactionScore = earn,
            RelativeStrengthScore = rs,
            RsLine52WeekScore = rs52,

            MarketRegime = marketRegime,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
