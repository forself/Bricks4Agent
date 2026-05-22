# authorize-mac-ssh-key.ps1
# ──────────────────────────────────────────────────────────────────────────
# 用途:從「已能 ssh b4a 的這台 Windows」把 dba-anthony 的 Mac 公鑰加到 VPS(b4a)
#       的 ~/.ssh/authorized_keys,讓那台 Mac 之後也能免密碼 `ssh b4a`。
#       冪等 —— 已存在就不重複加。
#
# 前提:這台 Windows 的 ~/.ssh/config 已有 Host b4a 且現在連得進去(ssh b4a 可用)。
#
# 執行(在 repo 根目錄):
#   powershell -ExecutionPolicy Bypass -File .\scripts\authorize-mac-ssh-key.ps1
# ──────────────────────────────────────────────────────────────────────────
$ErrorActionPreference = "Stop"

# dba-anthony 的 Mac(MacBook Air)公鑰 —— 公鑰可公開,安全。
$pubkey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPfRBQcVGjVTlO7jcLft7wur76y7lC7qQuVlfs6mKrhs b4a-vps-from-mac-dba-anthony"

# 遠端(VPS)上要跑的 bash:確保 ~/.ssh 權限正確,冪等加入公鑰。
$remote = "mkdir -p ~/.ssh && chmod 700 ~/.ssh && touch ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys && " +
          "if grep -qxF '$pubkey' ~/.ssh/authorized_keys; then echo ALREADY_PRESENT; " +
          "else echo '$pubkey' >> ~/.ssh/authorized_keys && echo KEY_ADDED; fi"

Write-Host "→ 透過 ssh b4a 授權 Mac 公鑰到 VPS ..."
ssh b4a $remote
Write-Host ""
Write-Host "完成。回到那台 Mac 測試:  ssh b4a   (應該免密碼直接進入)"
