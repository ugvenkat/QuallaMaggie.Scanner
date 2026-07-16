namespace QualMaggie.Scanner.Models
{
    public class Candle
    {
        // Required for RS percentile + grouping
        public string Ticker { get; set; } = string.Empty;

        // Core OHLCV fields
        public DateTime TradeDate { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal ClosePrice { get; set; }
        public long Volume { get; set; }

        // Optional validation helper
        public bool IsValid(out string? reason)
        {
            if (HighPrice <= 0 || LowPrice <= 0 || OpenPrice <= 0 || ClosePrice <= 0)
            {
                reason = "NON_POSITIVE_PRICE";
                return false;
            }

            if (LowPrice > HighPrice)
            {
                reason = "LOW_ABOVE_HIGH";
                return false;
            }

            if (Volume < 0)
            {
                reason = "NEGATIVE_VOLUME";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
