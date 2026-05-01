#!/bin/bash
set -e

TARGET="/home/claude-user/.claude.json"

if [ -f /seed/.claude.json ]; then
    cp /seed/.claude.json "$TARGET"
    chmod 600 "$TARGET"
fi

if [ ! -f "$TARGET" ]; then
    echo '{}' > "$TARGET"
fi

tmp=$(mktemp)
jq '
  .projects = (.projects // {})
  | .projects["/home/claude-user"] = ((.projects["/home/claude-user"] // {}) + {"hasTrustDialogAccepted": true, "hasCompletedProjectOnboarding": true, "allowedTools": [], "history": [], "mcpContextUris": [], "mcpServers": {}, "enabledMcpjsonServers": [], "disabledMcpjsonServers": [], "dontCrawlDirectory": false})
  | .projects["/home/claude-user/workspace"] = ((.projects["/home/claude-user/workspace"] // {}) + {"hasTrustDialogAccepted": true, "hasCompletedProjectOnboarding": true, "allowedTools": [], "history": [], "mcpContextUris": [], "mcpServers": {}, "enabledMcpjsonServers": [], "disabledMcpjsonServers": [], "dontCrawlDirectory": false})
' "$TARGET" > "$tmp" && mv "$tmp" "$TARGET"

# 修正 plugin 路徑（Windows → Linux）
PLUGINS_FILE="/home/claude-user/.claude/plugins/installed_plugins.json"
if [ -f "$PLUGINS_FILE" ] && grep -q 'C:\\' "$PLUGINS_FILE" 2>/dev/null; then
    sed -i 's|C:\\\\Users\\\\USER\\\\.claude\\\\plugins\\\\cache|/home/claude-user/.claude/plugins/cache|g' "$PLUGINS_FILE"
    sed -i 's|C:/Users/USER/.claude/plugins/cache|/home/claude-user/.claude/plugins/cache|g' "$PLUGINS_FILE"
fi

# 確保 Discord bot token 存在
DISCORD_DIR="/home/claude-user/.claude/channels/discord"
mkdir -p "$DISCORD_DIR"
if [ -n "$DISCORD_BOT_TOKEN" ] && [ ! -f "$DISCORD_DIR/.env" ]; then
    echo "DISCORD_BOT_TOKEN=$DISCORD_BOT_TOKEN" > "$DISCORD_DIR/.env"
fi

# 用 npm-installed claude（/usr/local/bin/claude，由 Dockerfile 裝的）。
# 之前試過 `claude install` 改成 native build，但每次容器重建 ~/.local 都會重來、
# 現在 install 流程偶爾會 hang（沒 TTY 拿不到互動輸入）；而真正的 bot 不上線
# bug 是 --channels source 名跟 --mcp-config server 名對不上，跟 npm vs native
# 無關。所以這裡保留 npm 版本就好。
# 若 ~/.local/bin/claude 早已存在（先前 install 過、host volume 帶過來）也讓它優先。
[ -d "$HOME/.local/bin" ] && export PATH="$HOME/.local/bin:$PATH"

# 寫一份路徑已展開的 mcp config 給 --mcp-config 用
# （plugin 原本的 .mcp.json 用 ${CLAUDE_PLUGIN_ROOT} 變數，但那個變數在 --channels
# 模式下不會被 substitute，導致 spawn 出來的 bun cwd 為錯誤路徑、立刻死掉。
# 寫一份固定路徑版避開這個 bug。）
# ⚠️ 已知 upstream regression（2026-05-01 確認 claude code v2.1.119 npm-installed）：
# `--channels plugin:discord@claude-plugins-official` 模式下 claude 不會 spawn
# 對應的 MCP plugin server（沒有 bun 子進程）。手動 --mcp-config 註冊雖能讓 plugin
# 連上 Discord，但配 --channels server:discord 會撞「no MCP server configured」
# 錯誤跟需要 --dangerously-load-development-channels 互動確認的多重關卡。
# 目前所有可從 container 外面試的繞法都試過、皆 dead end。
# 詳細失敗清單見 git log 跟 ~/.claude/projects/.../memory/project_b4a_discord_bot.md
PLUGIN_ROOT=$(ls -d /home/claude-user/.claude/plugins/cache/claude-plugins-official/discord/*/ 2>/dev/null | head -1 | sed 's:/$::')
if [ -n "$PLUGIN_ROOT" ]; then
    export CLAUDE_PLUGIN_ROOT="$PLUGIN_ROOT"
fi

cd /home/claude-user/workspace
# --channels 在 Claude Code v2.1.126+ 接受兩種來源格式：
#   plugin:<name>@<marketplace>  — 由 claude 自己 spawn 的 plugin（但這條路徑在我們環境下
#                                  spawn 不出 bun 子進程，是這個檔案最初想修的 bug）
#   server:<name>                — 由 --mcp-config 手動註冊的 MCP server（name 對到
#                                  json 裡 mcpServers 的 key，這裡是 "discord"）
# 我們用 --mcp-config 路徑，對應 channel 來源就要寫 server:discord。寫成 plugin:... 會
# 因為 source 名對不上、claude 收到 plugin 的 notifications/claude/channel 但 silent
# drop（plugin 端看不到錯誤、bot 看似在線實則訊息進不到 claude）。
# claude 看到 --dangerously-load-development-channels 會跳互動確認（按 1 = 我是為了
# local development）。容器無人按鍵會卡住。
#
# 容器 cap_drop:ALL 擋掉 apt 安裝 expect 的權限，所以用 util-linux 的 script(1)
# 給 claude 一個 PTY、然後從 stdin pipe 餵「1\n」進去當作有人按鍵；後接 sleep infinity
# 不讓 claude 看到 EOF（它需要 stdin 持續開著才會繼續 channel 監聽迴圈）。
# sleep 5 等 claude TUI 初始化完成才送 keystroke（送太早會被 UI init 吞掉）；
# 多送幾次 Enter (\r) 容錯，因為 UI 第一次按 Enter 後可能還停在 dev-channel
# 確認頁，再按一次 Enter 才真正進 channel 監聽。
exec claude --channels plugin:discord@claude-plugins-official --dangerously-skip-permissions
