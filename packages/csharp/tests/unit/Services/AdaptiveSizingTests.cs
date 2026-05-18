using Broker.Services;
using FluentAssertions;

namespace Unit.Tests.Services;

/// <summary>
/// 鎖住 C2 — AutoTrader 的 confidence + Kelly multiplier sizing 契約。
///
/// 公式（pure function、互乘）：
///   factor = (confEnabled ? max(floor, clamp(conf, 0, 1)) : 1)
///          × (kellyEnabled ? clamp(kellyFraction, 0, 1)    : 1)
///   sized  = baseQty × factor
///
/// 為什麼 multiplier 互乘、不是平均：兩個 signal 同時偏弱（低信心 × 過去虧損 strategy）
/// 應該更積極縮 qty、不該被平均稀釋。
/// </summary>
public class AdaptiveSizingTests
{
    [Fact]
    public void BothDisabled_ReturnsBaseQtyUnchanged()
    {
        var sized = AutoTraderService.ApplyAdaptiveSizing(
            baseQty: 1m, confidence: 0.5m, confidenceFloor: 0.3m,
            kellyFraction: 0.5m,
            confidenceEnabled: false, kellyEnabled: false);
        sized.Should().Be(1m, "兩個 feature 都關 = 完全不縮、向後相容");
    }

    [Fact]
    public void ConfidenceEnabled_HighConfidence_NoShrink()
    {
        // confidence 0.9 > floor 0.3 → factor = 0.9
        var sized = AutoTraderService.ApplyAdaptiveSizing(
            baseQty: 100m, confidence: 0.9m, confidenceFloor: 0.3m,
            kellyFraction: 1m, confidenceEnabled: true, kellyEnabled: false);
        sized.Should().Be(90m, "高信心仍會略縮、確保 confidence=1.0 才用滿 qty");
    }

    [Fact]
    public void ConfidenceEnabled_LowConfidence_FloorsAtMinimum()
    {
        // confidence 0.1 < floor 0.3 → factor = 0.3（不縮到 0）
        var sized = AutoTraderService.ApplyAdaptiveSizing(
            baseQty: 100m, confidence: 0.1m, confidenceFloor: 0.3m,
            kellyFraction: 1m, confidenceEnabled: true, kellyEnabled: false);
        sized.Should().Be(30m, "低信心不該縮到 0、否則永遠下不出單");
    }

    [Fact]
    public void ConfidenceEnabled_FullConfidence_NoChange()
    {
        // confidence 1.0 → factor = 1.0
        var sized = AutoTraderService.ApplyAdaptiveSizing(
            baseQty: 100m, confidence: 1.0m, confidenceFloor: 0.3m,
            kellyFraction: 1m, confidenceEnabled: true, kellyEnabled: false);
        sized.Should().Be(100m);
    }

    [Fact]
    public void ConfidenceEnabled_OverOne_ClampsTo1()
    {
        // 防 strategy 漏算回 confidence > 1（理論上不該、但 sanity）
        var sized = AutoTraderService.ApplyAdaptiveSizing(
            baseQty: 100m, confidence: 1.5m, confidenceFloor: 0.3m,
            kellyFraction: 1m, confidenceEnabled: true, kellyEnabled: false);
        sized.Should().Be(100m, "confidence 上限 1.0、不該放大 baseQty");
    }

    [Fact]
    public void ConfidenceEnabled_Negative_ClampsToFloor()
    {
        var sized = AutoTraderService.ApplyAdaptiveSizing(
            baseQty: 100m, confidence: -0.5m, confidenceFloor: 0.3m,
            kellyFraction: 1m, confidenceEnabled: true, kellyEnabled: false);
        sized.Should().Be(30m, "負數 clamp 到 0、再被 floor 撐回 0.3");
    }

    [Fact]
    public void KellyOnly_ShrinksByFraction()
    {
        // Kelly fraction 0.4 → factor = 0.4
        var sized = AutoTraderService.ApplyAdaptiveSizing(
            baseQty: 100m, confidence: 0.5m, confidenceFloor: 0.3m,
            kellyFraction: 0.4m, confidenceEnabled: false, kellyEnabled: true);
        sized.Should().Be(40m);
    }

    [Fact]
    public void BothEnabled_FactorsMultiply()
    {
        // confidence 0.6 × kelly 0.5 = 0.3 → sized 30
        var sized = AutoTraderService.ApplyAdaptiveSizing(
            baseQty: 100m, confidence: 0.6m, confidenceFloor: 0.3m,
            kellyFraction: 0.5m, confidenceEnabled: true, kellyEnabled: true);
        sized.Should().Be(30m,
            "兩條都偏弱應該更積極縮、不是平均稀釋（multiplier 互乘的設計意圖）");
    }

    [Fact]
    public void KellyOverOne_ClampsTo1()
    {
        // Kelly fraction 不該 > 1（service 內已 cap 25%、但 sanity）
        var sized = AutoTraderService.ApplyAdaptiveSizing(
            baseQty: 100m, confidence: 1m, confidenceFloor: 0.3m,
            kellyFraction: 5m, confidenceEnabled: false, kellyEnabled: true);
        sized.Should().Be(100m);
    }

    // ── NormalizeKellyToFactor ─────────────────────────────────────────
    // KellyPositionSizingService.EffectiveFraction 範圍 [0, 0.25]、
    // normalize to [0, 1] 才適合餵 ApplyAdaptiveSizing（後者把 > 1 clamp 掉）。

    [Fact]
    public void NormalizeKelly_MaxFraction_ReturnsOne()
    {
        // EffectiveFraction = 0.25 (Kelly's max cap) → factor 1.0（不縮）
        AutoTraderService.NormalizeKellyToFactor(0.25m).Should().Be(1m);
    }

    [Fact]
    public void NormalizeKelly_HalfMax_ReturnsHalf()
    {
        // EffectiveFraction = 0.125 → factor 0.5（縮半）
        AutoTraderService.NormalizeKellyToFactor(0.125m).Should().Be(0.5m);
    }

    [Fact]
    public void NormalizeKelly_Zero_ReturnsZero()
    {
        // EffectiveFraction = 0 (策略 EV 負) → factor 0（完全不下單）
        AutoTraderService.NormalizeKellyToFactor(0m).Should().Be(0m);
    }

    [Fact]
    public void NormalizeKelly_AboveMax_ClampsToOne()
    {
        // Sanity：理論上 KellyPositionSizingService 不會回 > 0.25、但防呆
        AutoTraderService.NormalizeKellyToFactor(0.5m).Should().Be(1m);
    }

    [Fact]
    public void NormalizeKelly_Negative_ClampsToZero()
    {
        AutoTraderService.NormalizeKellyToFactor(-0.1m).Should().Be(0m);
    }
}
