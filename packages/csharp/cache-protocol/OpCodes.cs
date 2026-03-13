namespace CacheProtocol;

/// <summary>
/// Wire 協議 OpCode 常數
///
/// 所有快取操作的 OpCode 定義。
/// 每個 TCP frame 的第 7 個 byte 為 OpCode，決定操作類型。
/// </summary>
public static class OpCodes
{
    // ── Client ↔ Server 操作 ──

    /// <summary>讀取鍵值</summary>
    public const byte GET = 0x01;

    /// <summary>寫入鍵值（含 TTL）</summary>
    public const byte SET = 0x02;

    /// <summary>刪除鍵</summary>
    public const byte DEL = 0x03;

    /// <summary>Compare-And-Swap（精確比較）</summary>
    public const byte CAS = 0x04;

    /// <summary>Compare-And-Swap-If-Greater（seq 專用：current < newValue 才更新）</summary>
    public const byte CAS_GT = 0x05;

    /// <summary>Decrement-If-Positive（quota 專用：value > 0 才遞減）</summary>
    public const byte DECR_POS = 0x06;

    /// <summary>原子遞增</summary>
    public const byte INCR = 0x07;

    /// <summary>分散式鎖取得</summary>
    public const byte LOCK = 0x08;

    /// <summary>分散式鎖釋放</summary>
    public const byte UNLOCK = 0x09;

    /// <summary>發布訊息到頻道</summary>
    public const byte PUBLISH = 0x0A;

    /// <summary>訂閱頻道</summary>
    public const byte SUBSCRIBE = 0x0B;

    /// <summary>取消訂閱</summary>
    public const byte UNSUBSCRIBE = 0x0C;

    /// <summary>檢查鍵是否存在</summary>
    public const byte EXISTS = 0x0D;

    /// <summary>設定 TTL</summary>
    public const byte EXPIRE = 0x0E;

    // ── 心跳 ──

    /// <summary>PING 心跳</summary>
    public const byte PING = 0x10;

    /// <summary>PONG 心跳回應</summary>
    public const byte PONG = 0x11;

    // ── 叢集內部（Leader ↔ Follower） ──

    /// <summary>複製操作（Leader → Follower）</summary>
    public const byte REPLICATE = 0x20;

    /// <summary>複製確認（Follower → Leader）</summary>
    public const byte REPLICATE_ACK = 0x21;

    /// <summary>選舉投票請求（Candidate → All）</summary>
    public const byte VOTE_REQ = 0x22;

    /// <summary>選舉投票回應（Node → Candidate）</summary>
    public const byte VOTE_ACK = 0x23;

    /// <summary>Leader 宣告（Leader → All）</summary>
    public const byte LEADER_ANN = 0x24;

    /// <summary>叢集心跳（Leader → Follower）</summary>
    public const byte CLUSTER_HEARTBEAT = 0x25;

    /// <summary>全量快照請求/傳輸</summary>
    public const byte SNAPSHOT = 0x26;

    // ── 功能池（Broker ↔ Worker） ──

    /// <summary>Worker 註冊（Worker → Broker）</summary>
    public const byte WORKER_REGISTER = 0x30;

    /// <summary>Worker 註冊確認（Broker → Worker）</summary>
    public const byte WORKER_REGISTER_ACK = 0x31;

    /// <summary>分派執行（Broker → Worker）</summary>
    public const byte WORKER_EXECUTE = 0x32;

    /// <summary>執行結果（Worker → Broker）</summary>
    public const byte WORKER_RESULT = 0x33;

    /// <summary>Worker 註銷（Worker → Broker）</summary>
    public const byte WORKER_DEREGISTER = 0x34;

    /// <summary>Worker 狀態查詢（Broker → Worker）</summary>
    public const byte WORKER_STATUS = 0x35;

    /// <summary>Worker 狀態回報（Worker → Broker）</summary>
    public const byte WORKER_STATUS_ACK = 0x36;

    // ── 回應 / 控制 ──

    /// <summary>成功回應</summary>
    public const byte RESPONSE_OK = 0x80;

    /// <summary>錯誤回應</summary>
    public const byte RESPONSE_ERR = 0x81;

    /// <summary>重定向到 Leader</summary>
    public const byte REDIRECT = 0x82;

    /// <summary>Pub/Sub 推送訊息（Server → Client）</summary>
    public const byte PUB_MESSAGE = 0x83;

    /// <summary>取得 OpCode 名稱（除錯用）</summary>
    public static string GetName(byte opCode) => opCode switch
    {
        GET => "GET",
        SET => "SET",
        DEL => "DEL",
        CAS => "CAS",
        CAS_GT => "CAS_GT",
        DECR_POS => "DECR_POS",
        INCR => "INCR",
        LOCK => "LOCK",
        UNLOCK => "UNLOCK",
        PUBLISH => "PUBLISH",
        SUBSCRIBE => "SUBSCRIBE",
        UNSUBSCRIBE => "UNSUBSCRIBE",
        EXISTS => "EXISTS",
        EXPIRE => "EXPIRE",
        PING => "PING",
        PONG => "PONG",
        REPLICATE => "REPLICATE",
        REPLICATE_ACK => "REPLICATE_ACK",
        VOTE_REQ => "VOTE_REQ",
        VOTE_ACK => "VOTE_ACK",
        LEADER_ANN => "LEADER_ANN",
        CLUSTER_HEARTBEAT => "CLUSTER_HEARTBEAT",
        SNAPSHOT => "SNAPSHOT",
        WORKER_REGISTER => "WORKER_REGISTER",
        WORKER_REGISTER_ACK => "WORKER_REGISTER_ACK",
        WORKER_EXECUTE => "WORKER_EXECUTE",
        WORKER_RESULT => "WORKER_RESULT",
        WORKER_DEREGISTER => "WORKER_DEREGISTER",
        WORKER_STATUS => "WORKER_STATUS",
        WORKER_STATUS_ACK => "WORKER_STATUS_ACK",
        RESPONSE_OK => "RESPONSE_OK",
        RESPONSE_ERR => "RESPONSE_ERR",
        REDIRECT => "REDIRECT",
        PUB_MESSAGE => "PUB_MESSAGE",
        _ => $"UNKNOWN(0x{opCode:X2})"
    };

    /// <summary>是否為寫入操作（必須路由到 Leader）</summary>
    public static bool IsWriteOp(byte opCode) => opCode switch
    {
        SET or DEL or CAS or CAS_GT or DECR_POS or INCR
            or LOCK or UNLOCK or PUBLISH => true,
        _ => false
    };

    /// <summary>是否為功能池操作（0x30-0x3F）</summary>
    public static bool IsWorkerOp(byte opCode) => opCode >= 0x30 && opCode <= 0x3F;
}
