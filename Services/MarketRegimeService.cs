using QualMaggie.Scanner.Models;

namespace QualMaggie.Scanner.Services;

public class MarketRegimeService
{
    public string GetRegime(List<Candle> benchmarkCandles)
    {
        if (benchmarkCandles.Count < 60)
            return "Neutral";

        var closes = benchmarkCandles.Select(c => c.ClosePrice).ToList();
        var ema10 = EmaCalculator.CalculateEMA(closes, 10);
        var ema20 = EmaCalculator.CalculateEMA(closes, 20);

        int n = closes.Count;
        decimal sma50 = closes.Skip(Math.Max(0, n - 50)).Average();

        bool ema10Rising = ema10[n - 1] > ema10[Math.Max(0, n - 5)];
        bool ema20Rising = ema20[n - 1] > ema20[Math.Max(0, n - 5)];
        bool stacked = ema10[n - 1] > ema20[n - 1] && ema20[n - 1] > sma50;

        if (ema10Rising && ema20Rising && stacked)
            return "Bull";

        return "Neutral";
    }
}
