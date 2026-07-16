namespace QualMaggie.Scanner.Models;

public class QualMaggieSettings
{
    // ATR / Risk
    public int AtrPeriod { get; set; }
    public decimal MaxStopAtr { get; set; }

    // Breakout (ORH)
    public int OrhLookback { get; set; }
    public decimal OrhCloseStrength { get; set; }
    public bool AllowHighTouchBreakouts { get; set; }
    public decimal HighTouchTolerance { get; set; }

    // Volume Dry-Up
    public int VolumeLookback { get; set; }
    public decimal VolumeDryUpRatio { get; set; }

    // VCP Tightness
    public int VcpLookback { get; set; }
    public decimal VcpMinTightness { get; set; }

    // Base Quality
    public int BaseLookback { get; set; }
    public decimal BaseMaxDepth { get; set; }
    public decimal BaseHigherLowWeight { get; set; }

    // Relative Strength
    public int RsLookback { get; set; }
    public decimal RsMinScore { get; set; }

    // Liquidity
    public long MinAvgVolume { get; set; }
    public decimal MinPrice { get; set; }

    // Scoring thresholds
    public decimal EliteMinScore { get; set; }
    public decimal StrongMinScore { get; set; }
    public decimal ModerateMinScore { get; set; }

    // ⭐ NEW: MA Respect Lookback (EMA20 + SMA50)
    public int MaRespectLookback { get; set; }

    // ⭐ NEW: Multi-day Post-Earnings Trend Lookback
    public int PostEarningsTrendDays { get; set; }
}
