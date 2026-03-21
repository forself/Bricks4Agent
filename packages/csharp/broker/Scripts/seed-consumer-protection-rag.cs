// 此腳本用於將消費者保護法相關法規寫入 RAG 資料庫
// 執行方式：在 broker 啟動後，透過 API 或直接在 Program.cs 中呼叫
// 以下為邏輯參考，實際整合到 BrokerDbInitializer 或獨立 endpoint

/*
使用方式（透過 memory_store API）：

每條法規條文以 key = "消費者保護法:第{N}條" 存入 SharedContextEntry
同時觸發 FTS5 索引（自動）和向量嵌入（自動）

rag_retrieve 查詢範例：
{
    "query": "消費者退貨七天猶豫期",
    "mode": "hybrid",
    "limit": 5
}
*/
