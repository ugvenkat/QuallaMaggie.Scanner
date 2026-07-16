using System;
using System.Collections.Generic;
using System.Linq;

namespace QualMaggie.Scanner.Services;

public static class MovingAverageCalculator
{
    // ---------------------------------------------------------
    // Simple Moving Average (SMA)
    // ---------------------------------------------------------
    public static List<decimal> CalculateSMA(List<decimal> prices, int period)
    {
        var sma = new List<decimal>(prices.Count);

        for (int i = 0; i < prices.Count; i++)
        {
            if (i < period - 1)
            {
                sma.Add(0m);
                continue;
            }

            decimal avg = prices.Skip(i - (period - 1)).Take(period).Average();
            sma.Add(avg);
        }

        return sma;
    }

    // ---------------------------------------------------------
    // Exponential Moving Average (EMA)
    // ---------------------------------------------------------
    public static List<decimal> CalculateEMA(List<decimal> prices, int period)
    {
        var ema = new List<decimal>(prices.Count);
        if (prices.Count == 0) return ema;

        decimal multiplier = 2m / (period + 1);

        // Seed with SMA
        decimal sma = prices.Take(period).Average();
        ema.Add(sma);

        for (int i = period; i < prices.Count; i++)
        {
            decimal value = ((prices[i] - ema.Last()) * multiplier) + ema.Last();
            ema.Add(value);
        }

        // Pad initial values
        while (ema.Count < prices.Count)
            ema.Insert(0, 0m);

        return ema;
    }
}
