using System;
using System.Collections.Generic;
using System.Linq;
using QualMaggie.Scanner.Models;

namespace QualMaggie.Scanner.Services;

public static class FeatureCalculators
{
    // ---------------------------------------------------------
    // Trend Cleanliness
    // ---------------------------------------------------------
    public static decimal CalculateTrendCleanliness(
        List<Candle> candles,
        List<decimal> ema10,
        List<decimal> ema20,
        int lookback)
    {
        int n = candles.Count;
        if (n < lookback + 20)
            return 0m;

        int start = n - lookback;
        int cleanCount = 0;

        for (int i = start; i < n; i++)
        {
            if (ema10[i] > ema20[i] &&
                candles[i].ClosePrice > ema10[i] &&
                candles[i].LowPrice > ema20[i])
            {
                cleanCount++;
            }
        }

        return (decimal)cleanCount / lookback;
    }

    // ---------------------------------------------------------
    // VCP Tightness
    // ---------------------------------------------------------
    public static decimal CalculateVcpTightness(List<Candle> candles, int lookback)
    {
        if (candles.Count < lookback + 5)
            return 0m;

        var window = candles.Skip(candles.Count - lookback).ToList();
        var ranges = window.Select(c => c.HighPrice - c.LowPrice).ToList();

        if (ranges.Count == 0)
            return 0m;

        decimal max = ranges.Max();
        decimal min = ranges.Min();

        if (max <= 0m)
            return 0m;

        return 1m - (min / max);
    }

    // ---------------------------------------------------------
    // Volume Dry-Up
    // ---------------------------------------------------------
    public static bool DetectVolumeDryUp(
        List<Candle> candles,
        int lookback,
        decimal ratio)
    {
        if (candles.Count < lookback + 5)
            return false;

        var window = candles.Skip(candles.Count - lookback).ToList();
        decimal avgVol = window.Average(c => (decimal)c.Volume);

        return candles.Last().Volume < avgVol * ratio;
    }

    // ---------------------------------------------------------
    // Liquidity Filter
    // ---------------------------------------------------------
    public static bool IsLiquid(
        List<Candle> candles,
        decimal minPrice,
        decimal minAvgVolume)
    {
        if (candles.Count < 20)
            return false;

        decimal price = candles.Last().ClosePrice;
        decimal avgVol = candles.TakeLast(20).Average(c => (decimal)c.Volume);

        return price >= minPrice && avgVol >= minAvgVolume;
    }

    // ---------------------------------------------------------
    // STRICT Episodic Pivot Detection (Qullamaggie)
    // ---------------------------------------------------------
    public static bool DetectEpisodicPivot(List<Candle> candles, decimal minGapPercent = 0.08m)
    {
        if (candles.Count < 30)
            return false;

        var today = candles[^1];
        var yesterday = candles[^2];

        // True gap up
        decimal gap = (today.OpenPrice - yesterday.ClosePrice) / yesterday.ClosePrice;
        if (gap < minGapPercent)
            return false;

        // Exceptional volume (3× 20-day average)
        decimal avgVol20 = candles.TakeLast(21).Take(20).Average(c => (decimal)c.Volume);
        if (today.Volume < avgVol20 * 3.0m)
            return false;

        return true;
    }

    // ---------------------------------------------------------
    // ⭐ FIXED DAILY BREAKOUT DETECTION (Bug A)
    // ---------------------------------------------------------
    public static (bool dailyBreakout, bool nearPivot, decimal pivot)
        DetectDailyBreakout(
            List<Candle> candles,
            int lookback,
            decimal closeStrength,
            bool allowHighTouch,
            decimal tolerance)
    {
        if (candles.Count < lookback + 5)
            return (false, false, 0m);

        // FIX: Exclude today's candle
        var window = candles
            .Skip(candles.Count - lookback - 1)
            .Take(lookback)
            .ToList();

        decimal pivot = window.Max(c => c.HighPrice);

        var today = candles.Last();
        decimal close = today.ClosePrice;
        decimal high = today.HighPrice;

        bool closeBreakout =
            close > pivot &&
            close >= high * closeStrength;

        bool highTouchBreakout = false;
        bool toleranceBreakout = false;

        if (allowHighTouch)
        {
            highTouchBreakout = high >= pivot;

            decimal toleranceLevel = pivot * (1 - tolerance);
            toleranceBreakout = close >= toleranceLevel;
        }

        bool dailyBreakout = closeBreakout || highTouchBreakout;
        bool nearPivot = !dailyBreakout && toleranceBreakout;

        return (dailyBreakout, nearPivot, pivot);
    }

    // ---------------------------------------------------------
    // Base Quality
    // ---------------------------------------------------------
    public static decimal CalculateBaseQuality(
        List<Candle> candles,
        int lookback,
        decimal maxDepth,
        decimal higherLowWeight)
    {
        if (candles.Count < lookback + 5)
            return 0m;

        var window = candles.Skip(candles.Count - lookback).ToList();

        decimal high = window.Max(c => c.HighPrice);
        decimal low = window.Min(c => c.LowPrice);

        if (high <= 0m)
            return 0m;

        decimal depth = (high - low) / high;
        if (depth > maxDepth)
            return 0m;

        int higherLows = 0;
        for (int i = 2; i < window.Count; i++)
        {
            if (window[i].LowPrice > window[i - 1].LowPrice)
                higherLows++;
        }

        decimal hlScore = (decimal)higherLows / window.Count;

        return (1m - depth) * 0.6m + hlScore * higherLowWeight;
    }

    // ---------------------------------------------------------
    // Prior Move Score
    // ---------------------------------------------------------
    public static decimal CalculatePriorMoveScore(List<Candle> candles)
    {
        if (candles.Count < 20)
            return 0m;

        var last = candles.Last();
        var prev = candles[^20];

        if (prev.ClosePrice <= 0m)
            return 0m;

        return (last.ClosePrice - prev.ClosePrice) / prev.ClosePrice;
    }

    // ---------------------------------------------------------
    // Earnings Reaction Score (1-day)
    // ---------------------------------------------------------
    public static decimal CalculateEarningsReactionScore(List<Candle> candles)
    {
        if (candles.Count < 5)
            return 0m;

        var today = candles.Last();
        var yesterday = candles[^2];

        decimal move = (today.ClosePrice - yesterday.ClosePrice) / yesterday.ClosePrice;

        if (move > 0.05m) return 1.0m;
        if (move > 0.03m) return 0.7m;
        if (move > 0.01m) return 0.4m;

        return 0m;
    }

    // ---------------------------------------------------------
    // ⭐ RS Percentile (using precomputed returns)
    // ---------------------------------------------------------
    public static decimal CalculateRelativeStrengthPercentile(
        string ticker,
        Dictionary<string, decimal> allReturns)
    {
        if (!allReturns.ContainsKey(ticker))
            return 0m;

        decimal stockReturn = allReturns[ticker];

        int better = allReturns.Count(r => r.Value < stockReturn);
        decimal percentile = (decimal)better / allReturns.Count;

        return Math.Clamp(percentile, 0m, 1m);
    }

    // ---------------------------------------------------------
    // ⭐ FIXED MA Respect (Bug B)
    // ---------------------------------------------------------
    public static bool RespectMovingAverages(
        List<Candle> candles,
        List<decimal> ema20,
        List<decimal> sma50,
        int lookback)
    {
        int n = candles.Count;
        int start = n - lookback;

        for (int i = start; i < n; i++)
        {
            var c = candles[i];

            if (c.ClosePrice < ema20[i]) return false;
            if (c.LowPrice < ema20[i]) return false;

            if (c.ClosePrice < sma50[i]) return false;
        }

        return true;
    }

    // ---------------------------------------------------------
    // Post-Earnings Trend Continuation
    // ---------------------------------------------------------
    public static decimal CalculatePostEarningsTrend(
        List<Candle> candles,
        int days = 5)
    {
        if (candles.Count < days + 2)
            return 0m;

        var earningsDay = candles[^2];
        var end = candles.Last();

        if (earningsDay.ClosePrice <= 0m)
            return 0m;

        return (end.ClosePrice - earningsDay.ClosePrice) / earningsDay.ClosePrice;
    }
}
