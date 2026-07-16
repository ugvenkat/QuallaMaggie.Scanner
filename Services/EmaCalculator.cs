namespace QualMaggie.Scanner.Services;

public static class EmaCalculator
{
    public static List<decimal> CalculateEMA(List<decimal> prices, int period)
    {
        if (prices == null || prices.Count == 0)
            return new List<decimal>();

        var ema = new List<decimal>(prices.Count);

        decimal multiplier = 2m / (period + 1);

        // Seed EMA with the first price
        decimal previousEma = prices[0];
        ema.Add(previousEma);

        // Calculate EMA for each subsequent price
        for (int i = 1; i < prices.Count; i++)
        {
            decimal currentEma = (prices[i] - previousEma) * multiplier + previousEma;
            ema.Add(currentEma);
            previousEma = currentEma;
        }

        return ema;
    }
}