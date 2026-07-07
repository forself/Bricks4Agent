# Bricks4Agent 工程手冊

> 本文件已由目前版技術手冊取代。

請改讀：

- [目前版本完整技術手冊](current-technical-manual.zh-TW.md)
- [目前版本完整使用手冊](current-user-manual.zh-TW.md)
- [Agent Container Runbook](agent-container-runbook.md)
- [LINE Sidecar Runbook](line-sidecar-runbook.zh-TW.md)

## 為什麼保留此檔案

舊版連結仍可能指向 `docs/manuals/engineer-guide.md`。為避免讀者開到過期或亂碼內容，本檔保留作為相容入口，並將目前工程說明集中到 `current-technical-manual.zh-TW.md`。

目前主要驗證命令：

```powershell
dotnet build packages/csharp/ControlPlane.slnx
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
npm run validate:baseorm
npm run validate:baseorm-sync
npm run validate:broker-scope
npm --prefix packages/javascript/browser run metadata:check
npm --prefix packages/javascript/browser test
npm test
npm run validate:ui-state
npm run audit:ui-styles
npm run validate:ui-library
npm run validate:backend-governance
npm run validate:agent-governed
npm run validate:broker-llm-proxy
node tools/agent/tests/validate-efcore-removal.js
```

整合測試或 Podman stack 測試可能產生 `packages/csharp/broker/broker.db*` 與 `.test-output/`，測試後需清理。
