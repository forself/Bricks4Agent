# Mac 開發機設定（code + push）

第二台機器（Mac）加入開發用。範圍 = **改 code / build / 測試 / push**；部署仍只在 VPS。

## ⚠️ 安全前提：Mac 不要跑真錢 broker

真錢 broker 在 **VPS 是唯一運行端**。若 Mac 也跑一個連同一組 BingX key 的 broker → 兩個 broker 同時對同一帳戶下單 = 重複單／打架。

- **VPS = 唯一 production**；**Mac = 開發 / build / 測試 + push**。
- 部署只在 VPS 手動 `git pull` + rebuild。Mac 不部署。
- 因此 **Mac 不需要任何機密**（`tools/.env.trading`、`tools/secrets/*` 都不用搬）——build / 測試都不吃真 key。

## 1. 工具鏈

```bash
brew install --cask dotnet-sdk    # .NET 8（global.json 鎖 8.0.100、rollForward latestMajor）
brew install git
# 編輯器隨意：VS Code / Rider。碰 JS 才需 node;此用途不需 Docker
```

## 2. 取得 repo + 設 remotes

```bash
git clone git@github.com:Anthonylee0206/Brick4Agent.git   # 私人 repo 建議走 SSH
cd Brick4Agent
git remote rename origin myorigin                              # 你的 fork = myorigin（push 目標）
git remote add origin git@github.com:forself/Bricks4Agent.git # 上游;只 fetch、永不 push
```

GitHub 私人 repo 認證（擇一）：

- SSH：`ssh-keygen -t ed25519` → `cat ~/.ssh/id_ed25519.pub` 貼到 GitHub → Settings → SSH and GPG keys
- 或 `brew install gh && gh auth login`

## 3. 驗證環境（不需任何機密）

```bash
dotnet build packages/csharp/ControlPlane.slnx               # 應 0 錯
dotnet test  packages/csharp/tests/unit/Unit.Tests.csproj   # 應全綠
```

兩個都過 = Mac 開發環境就緒。

## 4. 跨機開發鐵則（兩台都遵守）

- **開工前先 `git pull myorigin main`**（兩台靠 myorigin 同步、避免分叉）
- push 一律 `git push myorigin <branch>`，**永不 push `origin`**（origin = 組長上游）
- commit 不含 `.env` / `*.key` / token / 內嵌密鑰（`.env.trading`、`tools/secrets/` 已 gitignore，保持）
- **不在 Mac 部署**；要上線在 VPS 手動 `git pull` + rebuild，pull 前看一眼 diff

## 5. 注意

- **機密檔不在 git（故意）**：`tools/.env.trading` + `tools/secrets/*.txt`（bingx/binance/alpaca key、broker master key、line token…）只有「在機器上跑 stack」才需要。本設定（code+push）不碰，它們繼續留在 VPS。
- **Claude Code 多機共用訂閱 OAuth 會打架**：Windows + Mac + VPS bot 共用 session、一台 refresh token 會弄壞別台。Mac 也裝 Claude Code 的話要另外處理（不同登入或排程同步）。
- **`.ps1` 在 Mac 跑不了**：repo 裡 14 個 PowerShell 腳本是 Windows 端 deploy / sidecar helper;Mac 用不到（部署是 VPS `git pull`）。對應的 `.sh` 已存在。`.gitattributes` 已把 `*.sh`/`*.py` 鎖 LF、跨 OS 不會壞行尾。
