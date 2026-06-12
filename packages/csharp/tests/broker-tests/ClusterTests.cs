using Broker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Broker.Tests;

/// <summary>
/// 2026-06-12:自動移轉選主層的單元測試（階段①②）。
/// 比照組長 DiaryApp 測試的做法:AAA、命名「方法_情境_預期」、用「測試替身」隔離相依、測邊界。
///   - SingleNodeLeaderElection = 純物件測試（無相依、最快、像 Pbkdf2 那組）。
///   - LeaderGuard = 以「假 ILeaderElection」隔離 → 不需要真 etcd 就能測 gate 邏輯（像 AuthService 那組 mock）。
/// </summary>
public static class ClusterTests
{
    /// <summary>
    /// 測試替身:手刻一個假的 ILeaderElection（B4A 自製 runner 沒裝 NSubstitute，用簡單 stub 取代 mock）。
    /// IsPrimary / Role 由測試直接指定，讓我們能精準擺出 PRIMARY / STANDBY 兩種情境。
    /// </summary>
    private sealed class FakeElection : ILeaderElection
    {
        public bool IsPrimary { get; init; }
        public NodeRole Role { get; init; }
        public string NodeId => "test-node";
    }

    public static (int passed, int failed) Run()
    {
        int passed = 0, failed = 0;
        void Check(string name, bool cond)
        {
            if (cond) { Console.WriteLine($"  [PASS] {name}"); passed++; }
            else { Console.Error.WriteLine($"  [FAIL] {name}"); failed++; }
        }

        Console.WriteLine("--- Cluster (LeaderElection / LeaderGuard) Tests ---");

        // ---- SingleNodeLeaderElection:無 etcd 時的預設 → 永遠 PRIMARY（保證單機現狀不變）----
        var single = new SingleNodeLeaderElection();
        Check("single→IsPrimary-true", single.IsPrimary);
        Check("single→Role-Single", single.Role == NodeRole.Single);

        // ---- LeaderGuard:依 IsPrimary 決定「該不該執行」（自我 fence 的應用層）----
        // PRIMARY → ShouldRun=true（該節點可執行週期工作）
        var guardPrimary = new LeaderGuard(
            new FakeElection { IsPrimary = true, Role = NodeRole.Primary }, NullLogger<LeaderGuard>.Instance);
        Check("guard-primary→run-true", guardPrimary.ShouldRun("health-snapshot"));

        // STANDBY → ShouldRun=false（自我跳過、避免多節點雙寫）—— 這是 gate 的核心邊界
        var guardStandby = new LeaderGuard(
            new FakeElection { IsPrimary = false, Role = NodeRole.Standby }, NullLogger<LeaderGuard>.Instance);
        Check("guard-standby→run-false", !guardStandby.ShouldRun("health-snapshot"));

        // 單機（Single 也算 IsPrimary）→ ShouldRun=true（保證沒設 etcd 時行為不變）
        var guardSingle = new LeaderGuard(single, NullLogger<LeaderGuard>.Instance);
        Check("guard-single→run-true", guardSingle.ShouldRun("any-job"));

        // ---- DeriveIdemKey:冪等 key 的「純函式」（解耦合後抽出、無 env/無時間 → 可測）----
        //      像組長 Pbkdf2 那組:純輸入→純輸出。同意圖→同 key 是 failover 不雙下單的基礎。
        var k1 = AutoTraderService.DeriveIdemKey("op", "prn_a", "bingx", "BTC-USDT", "long", "harmonic", 12345L);
        var k2 = AutoTraderService.DeriveIdemKey("op", "prn_a", "bingx", "BTC-USDT", "long", "harmonic", 12345L);
        // 相同輸入（含同時間桶）→ 完全相同 key（deterministic）—— failover 重送會撞同 key、被 BingX 擋
        Check("idem-same-input→same-key", k1 == k2);
        // 不同 side → 不同 key（不會誤把多單當空單的重送）
        var kShort = AutoTraderService.DeriveIdemKey("op", "prn_a", "bingx", "BTC-USDT", "short", "harmonic", 12345L);
        Check("idem-diff-side→diff-key", k1 != kShort);
        // 不同時間桶 → 不同 key（桶換了=新意圖、不會把幾小時後的新單誤判成重送）
        var kLaterBucket = AutoTraderService.DeriveIdemKey("op", "prn_a", "bingx", "BTC-USDT", "long", "harmonic", 12346L);
        Check("idem-diff-bucket→diff-key", k1 != kLaterBucket);
        // 長度 ≤ 36（BingX clientOrderID 上限）+ 以 prefix- 開頭
        Check("idem-len≤36", k1.Length <= 36);
        Check("idem-prefix-format", k1.StartsWith("op-"));

        Console.WriteLine($"--- Cluster: {passed} passed, {failed} failed ---");
        return (passed, failed);
    }
}
