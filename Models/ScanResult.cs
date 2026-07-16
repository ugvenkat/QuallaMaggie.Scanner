namespace QualMaggie.Scanner.Models;

public class ScanResult
{
    public string Ticker { get; set; } = "";
    public DateTime ScanDate { get; set; }
    public Guid RunId { get; set; }

    public string Signal { get; set; } = "";
    public string SignalQuality { get; set; } = "";

    public decimal ClosePrice { get; set; }
    public decimal PivotPrice { get; set; }

    public decimal EMA10 { get; set; }
    public decimal EMA20 { get; set; }
    public decimal SMA50 { get; set; }

    public decimal PercentMove { get; set; }
    public long Volume { get; set; }

    public decimal TrendCleanlinessScore { get; set; }
    public decimal VcpTightnessScore { get; set; }

    public bool VolumeDryUp { get; set; }
    public bool IsLiquid { get; set; }
    public bool IsEpisodicPivot { get; set; }
    public bool IsDailyBreakout { get; set; }
    public bool IsNearPivotBreakout { get; set; }

    public decimal BaseQualityScore { get; set; }
    public decimal PriorMoveScore { get; set; }
    public decimal EarningsReactionScore { get; set; }

    public decimal RelativeStrengthScore { get; set; }
    public decimal RsLine52WeekScore { get; set; }

    public string MarketRegime { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
