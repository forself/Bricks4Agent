# Bricks4Agent Dev Code Signing 與 WDAC 補充策略

本文件說明如何在 Windows 開發機上建立 `Bricks4Agent Dev Code Signing` 流程，讓本機 build output 可以被簽章，並可選擇產生 WDAC / App Control for Business supplemental policy 來信任該 signer。

## 重要界線

- Smart App Control 沒有單一 app allow-list。若目前封鎖來自 Smart App Control 內建策略，簽章可改善信任訊號，但不保證能被單機 allow-list 放行。

- WDAC supplemental policy 只能補到一個允許 supplemental 的 base policy。產生 policy 時必須提供 `BasePolicyId`。

- 腳本預設不部署 WDAC policy。`Install-BricksWdacPolicy.ps1` 預設是 dry-run，只印出 `CiTool --update-policy` 命令。

- 不要提交私鑰、PFX、憑證密碼或 `.run/` 產物。`.gitignore` 已忽略 `.run/`、`*.pfx`、`*.p12`。

- Signed WDAC base policy 風險很高；本流程只產生 supplemental policy，不建立 signed base policy。

## 腳本

| Script | Purpose |
|---|---|
| `tools/windows-signing/New-BricksDevCodeSigningCert.ps1` | 建立或重用 CurrentUser code-signing cert，輸出 `.run/code-signing/Bricks4AgentDevCodeSigning.cer` |
| `tools/windows-signing/Sign-BricksAssemblies.ps1` | 只簽 repo 自家 `.csproj` 對應的 `bin/**/*.dll` / `bin/**/*.exe` |
| `tools/windows-signing/New-BricksWdacSupplementalPolicy.ps1` | 用已簽章 assemblies 產生 WDAC supplemental policy XML / CIP |
| `tools/windows-signing/Install-BricksWdacPolicy.ps1` | 預設 dry-run；加 `-Deploy` 且 elevated 時才呼叫 `CiTool --update-policy` |
| `tools/windows-signing/Repair-BricksWdacRuntimeTrust.ps1` | 修復 `.run\line-sidecar` runtime trust：補簽自家 DLL/EXE、用 Publisher + Hash fallback 掃描 runtime、部署後驗證 policy active |

## 建立開發簽章憑證

一般建立：

```powershell
npm run signing:cert
```

若要讓目前使用者信任此 dev cert：

```powershell
npm run signing:cert -- -TrustForCurrentUser
```

這會把 public cert 匯入 CurrentUser `TrustedPublisher` 與 `Root`。只適合本機開發機，不要用在正式發行。

若需要匯出 PFX，必須明確提供密碼，並確認輸出留在 `.run/code-signing/`：

```powershell
$password = Read-Host "PFX password" -AsSecureString
powershell -NoProfile -ExecutionPolicy Bypass `
  -File tools/windows-signing/New-BricksDevCodeSigningCert.ps1 `
  -ExportPfx `
  -PfxPassword $password
```

## Build 後簽章 assemblies

先 build：

```powershell
dotnet build packages/csharp/ControlPlane.slnx
```

再簽章：

```powershell
npm run signing:assemblies
```

若環境可連外，可加 timestamp server：

```powershell
npm run signing:assemblies -- -TimestampServer http://timestamp.digicert.com
```

若要由簽章腳本先 build：

```powershell
npm run signing:assemblies -- -Build
```

`Sign-BricksAssemblies.ps1` 會掃描 repo 內 `.csproj`，只簽自家 project assembly name 對應的 build output，不簽 NuGet 第三方 DLL。

## SAC / WDAC 下執行 DB 驗證

在 Smart App Control / WDAC enforcement 環境中，不要在簽章後直接跑一般 `dotnet run` 驗證命令。`dotnet run` 預設會先 build，可能把剛簽好的 local assemblies 覆蓋成未簽章版本，導致 `0x800711C7`。

請改用 signed validation 入口，它們會依序執行 build、sign、`dotnet run --no-build`：

```powershell
npm run validate:db:signed
npm run test:dotnet:signed
```

可拆開執行：

```powershell
npm run validate:baseorm:signed
npm run validate:baseorm-sync
npm run validate:broker-scope:signed
npm run test:broker:signed
npm run test:unit:signed
npm run test:integration:signed
```

若需要手動執行某個 verify project，順序必須維持：

```powershell
dotnet build packages/csharp/database/BaseOrm/net10/verify/BaseOrm.Verify.csproj
npm run signing:assemblies
dotnet run --no-build --project packages/csharp/database/BaseOrm/net10/verify/BaseOrm.Verify.csproj
```

## 查詢 BasePolicyId

產生 supplemental policy 前，先確認要補到哪個 WDAC base policy。可用 elevated PowerShell：

```powershell
CiTool --list-policies -json
```

也可看 active policy 目錄：

```powershell
Get-ChildItem C:\Windows\System32\CodeIntegrity\CiPolicies\Active
```

把要補充的 base policy id 記下，例如：

```text
{D6D6C2D6-E8B6-4D8F-8223-14BE1DE562FF}
```

## 產生 WDAC supplemental policy

先確保 assemblies 已簽章，再產生 policy：

```powershell
npm run signing:wdac-policy -- -BasePolicyId "{YOUR-BASE-POLICY-ID}" -ScanPath packages
```

輸出位置：

```text
.run/wdac/Bricks4Agent.DevCodeSigning.Supplemental.xml
.run/wdac/{policy-id}.cip
.run/wdac/Bricks4Agent.DevCodeSigning.Supplemental.scan.log
```

如果目前 Windows 沒有 `ConvertFrom-CIPolicy`，可只產 XML：

```powershell
npm run signing:wdac-policy -- -BasePolicyId "{YOUR-BASE-POLICY-ID}" -ScanPath packages -SkipConvert
```

## LINE sidecar 大量 DLL 被封鎖

若 `.run\line-sidecar` 啟動時看到大量 DLL 被安全性封鎖，或 Code Integrity event 顯示：

```text
did not meet the Enterprise signing level requirements
```

不要只反覆執行簽章。這通常代表 supplemental WDAC policy 沒有真的進入 active policy，或 runtime 內含未簽第三方 DLL，需要用 hash fallback 一起納入 policy。

先用一般 PowerShell 產生修復資訊：

```powershell
npm run signing:wdac-repair
```

輸出中的 `IsActive` 若是 `false`，表示目前 `.cip` 還沒有被 WDAC 載入；此時封鎖會繼續發生。

用系統管理員 PowerShell 部署並驗證 active policy：

```powershell
npm run signing:wdac-repair -- -Deploy
```

這個流程會：

- 補簽 `.run\line-sidecar` 內自家 `.dll` / `.exe`。

- 針對整個 `.run\line-sidecar` 產生 Publisher + Hash fallback WDAC supplemental policy。

- 呼叫 `CiTool --update-policy`。

- 檢查 `{policy-id}.cip` 是否出現在 `C:\Windows\System32\CodeIntegrity\CiPolicies\Active`。

只有部署後 active policy 檢查通過，才代表 WDAC 真正信任目前 runtime。若 policy active 後仍被擋，再查最新 Code Integrity event，確認被擋的是哪個新路徑或新檔案版本。

## 主工作區完整測試 trust 修復

在 Smart App Control / WDAC enforcement 環境中，完整測試常見的封鎖點不只 `Broker.Tests.exe`，也可能是 `Unit.Tests.dll`、`Integration.Tests.dll`、`ExecutionAdapterWorker.dll`、`BrokerCore.dll` 等自家 assembly。若每次 build 後都要重新補 hash，代表信任邊界太窄；主工作區測試修復流程預設改用 Publisher-level trust 來信任 `Bricks4Agent Dev Code Signing` signer，Hash 只作為第三方 DLL 或特殊 apphost 的 fallback。

`New-BricksWdacSupplementalPolicy.ps1` 預設會移除 `Enabled:Audit Mode`，產生可實際放行的 enforced supplemental policy。只有需要觀察不放行時才加 `-AuditMode`。

主工作區測試請使用專用入口產生 broker runtime、broker-tests、xUnit unit tests、xUnit integration tests 的 supplemental policy：

```powershell
npm run signing:wdac-repair-tests
```

輸出會列出四個 policy：

- `.run\wdac\main-broker-debug\{policy-id}.cip`

- `.run\wdac\main-broker-tests\{policy-id}.cip`

- `.run\wdac\main-unit-tests\{policy-id}.cip`

- `.run\wdac\main-integration-tests\{policy-id}.cip`

若要直接部署，必須使用系統管理員 PowerShell：

```powershell
npm run signing:wdac-repair-tests -- -Deploy
```

部署後執行測試時不要再讓 test command 重新 build，避免覆蓋已簽章輸出。Broker console test 可直接跑：

```powershell
dotnet run --no-build --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
```

若要跑完整 .NET 測試，優先使用 signed 入口；它們會先 build、簽章，再用 `--no-build` / `--no-restore` 執行：

```powershell
npm run test:unit:signed
npm run test:integration:signed
npm run test:broker:signed
npm run test:dotnet:signed
```

若目前 active policy 尚未包含測試輸出，可先用整合入口部署後跑 broker console test：

```powershell
npm run test:broker:trusted
```

如果仍被封鎖，先查最新 Code Integrity event 的實際檔案路徑，再重新跑 `npm run signing:wdac-repair-tests -- -Deploy`。只要封鎖的是自家 assembly，目標是讓 Publisher-level policy 生效，而不是永久追逐每次 build 變動的 hash。

## Broker.Tests.exe / 測試 apphost 被封鎖

若 `dotnet run --no-build --project packages/csharp/tests/broker-tests/Broker.Tests.csproj` 或 worktree 內的 `Broker.Tests.exe` 被封鎖，即使 `Get-AuthenticodeSignature` 顯示簽章有效，也代表目前 active policy 沒有涵蓋這個測試輸出路徑。測試 apphost 適合使用 hash-level supplemental policy。

以 `baselogger-governance` worktree 的 broker-tests 輸出為例：

```powershell
npm run signing:wdac-repair -- `
  -RuntimeRoot "D:\Bricks4Agent\.worktrees\baselogger-governance\packages\csharp\tests\broker-tests\bin\Debug\net10.0" `
  -OutputDir ".run\wdac\baselogger-worktree-broker-tests" `
  -PolicyLevel Hash
```

確認輸出中的 `IsActive`。若是 `false`，用系統管理員 PowerShell 部署：

```powershell
npm run signing:wdac-repair -- `
  -RuntimeRoot "D:\Bricks4Agent\.worktrees\baselogger-governance\packages\csharp\tests\broker-tests\bin\Debug\net10.0" `
  -OutputDir ".run\wdac\baselogger-worktree-broker-tests" `
  -PolicyLevel Hash `
  -Deploy
```

也可以部署已產生的 CIP：

```powershell
"C:\WINDOWS\system32\CiTool.exe" --update-policy "D:\Bricks4Agent\.run\wdac\baselogger-worktree-broker-tests\{policy-id}.cip" -json
```

部署後再重跑 `dotnet run --no-build`。如果 build 後測試 apphost 被覆蓋，hash 會改變，必須重新產生並部署該測試輸出的 hash policy。

## Dry-run 部署檢查

預設不部署，只印出會執行的命令：

```powershell
npm run signing:wdac-install
```

指定 CIP：

```powershell
npm run signing:wdac-install -- -PolicyBinary ".run/wdac/{policy-id}.cip"
```

列出 active policies：

```powershell
npm run signing:wdac-install -- -ListActive
```

## 部署 supplemental policy

部署必須使用 elevated PowerShell，且必須明確加 `-Deploy`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File tools/windows-signing/Install-BricksWdacPolicy.ps1 `
  -PolicyBinary ".run/wdac/{policy-id}.cip" `
  -Deploy `
  -VerifyActive
```

部署後若仍被擋，查 Code Integrity event：

```powershell
Get-WinEvent -FilterHashtable @{
  LogName = "Microsoft-Windows-CodeIntegrity/Operational"
  StartTime = (Get-Date).AddHours(-2)
} | Select-Object TimeCreated, Id, Message
```

## 驗證流程

靜態流程驗證：

```powershell
npm run validate:code-signing-flow
```

簽章後可驗證自家 assemblies 是否都由預期 cert 簽過：

```powershell
npm run signing:assemblies -- -VerifyOnly
```

完整 DB / broker verify 可再跑：

```powershell
npm run validate:baseorm
npm run validate:broker-scope
```

若仍看到 `Smart App Control Block` 或 `did not meet the Enterprise signing level requirements`，代表目前 active policy 仍未信任此 signer 或 supplemental policy 沒有作用於該 base policy。
