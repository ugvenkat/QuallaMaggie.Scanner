using QualMaggie.Scanner.Models;

namespace QualMaggie.Scanner.Services;

public static class AtrCalculator
{
    public static List<decimal> CalculateAtr(List<Candle> candles, int period)
    {
        var result = new List<decimal>(new decimal[candles.Count]);

        if (candles.Count < 2)
            return result;

        decimal trSum = 0m;

        for (int i = 1; i < candles.Count; i++)
        {
            decimal high = candles[i].HighPrice;
            decimal low = candles[i].LowPrice;
            decimal prevClose = candles[i - 1].ClosePrice;

            decimal tr = Math.Max(high - low,
                Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));

            if (i <= period)
            {
                trSum += tr;

                if (i == period)
                    result[i] = trSum / period;
            }
            else
            {
                result[i] = (result[i - 1] * (period - 1) + tr) / period;
            }
        }

        return result;
    }
}
