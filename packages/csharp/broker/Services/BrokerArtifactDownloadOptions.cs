namespace Broker.Services;

/// <summary>
/// 設定:broker 自有的「簽章 + 短期過期」artifact 下載 API(2026-05-29, AnthonyLee)。
///
/// 執行 Benson 2026-03-29 plan(docs/superpowers/plans/2026-03-29-broker-artifact-download-api.md)
/// 的「新檔」部分:讓本地 artifact 能透過 signed/expiring 公開連結下載,當作 Google Drive 交付失敗時的 fallback。
/// ⚠ 本次只新增新檔、reuse(不改)Benson 原有服務;plan 中「改 LineArtifactDeliveryService 接 fallback」
///    那段會動到 Benson 原檔、需白名單,故本次跳過(見 [[feedback_no_modify_existing]])。
///
/// 來源:env ARTIFACT_DOWNLOAD_SIGNING_SECRET 或 config ArtifactDownload:*。
/// </summary>
public sealed class BrokerArtifactDownloadOptions
{
    /// <summary>HMAC 簽章密鑰。空 = 啟動時隨機生成(重啟後舊連結失效;短 TTL 下可接受,正式請設固定值)。</summary>
    public string SigningSecret { get; init; } = "";

    /// <summary>簽章連結有效秒數,預設 3600(1 小時)。</summary>
    public int TtlSeconds { get; init; } = 3600;

    /// <summary>公開 base URL(e.g. https://xxx.ngrok.io)。空 = 回相對路徑、由呼叫端自行補。</summary>
    public string PublicBaseUrl { get; init; } = "";
}
