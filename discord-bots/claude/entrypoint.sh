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

# ⚠️  WIP / NOT WORKING（2026-05-02 確認 Claude Code v2.1.126）⚠️
#
# 這個 entrypoint 把官方 Claude Code Discord plugin 整合到 bot 容器，已通過 5 層診斷：
#   ✓ v2.1.119 → v2.1.126（server: 來源語法支援）
#   ✓ --mcp-config + --strict-mcp-config（繞過 plugin spawn 失敗、bun 子進程確實啟動）
#   ✓ --dangerously-load-development-channels server:discord（用對的「值」語法）
#   ✓ script(1) PTY + 自動 \r（過互動 confirm prompt，無人值守可啟動）
#   ✓ Plugin 端 receive Discord 訊息正常（typing indicator 會送出）
#
# 但 runtime 仍卡在最後一哩：claude 收到 plugin 推來的 channel notification 時
# 找不到 server:discord 的 MCP 註冊（log 持續吐 "no MCP server configured with that
# name" + "server: entries need --dangerously-load-development-channels"）。
#
# 這是 Claude Code 內部 channel ↔ MCP 整合的 upstream regression / 仍未 GA 的功能
# （flag 名字本身就叫 "dangerously-load-development-channels"，Anthropic 自己定位
# 是 dev/preview）。從容器外部所有可試的繞法已試過、皆 dead end。
#
# 維持這個 entrypoint 是因為它把問題從第 0 層推到第 6 層，下一個修正版的 Claude Code
# 釋出時，理論上只要重 build image 就能 work（或者只需要再加 1-2 個 flag）。
# 在此之前，Discord bot 不會回覆任何訊息——這是 expected，不是 bug。
#
# --channels 在 Claude Code v2.1.126+ 接受兩種來源格式：
#   plugin:<name>@<marketplace>  — 由 claude 自己 spawn plugin。
#                                  v2.1.119/v2.1.126 在我們環境下都 spawn 不出 bun
#                                  子進程（無法 reproduce 在 host），訊息 silent drop。
#   server:<name>                — 由 --mcp-config 手動註冊的 MCP server。
#                                  繞過 plugin auto-spawn、直接讓 claude 連我們指定
#                                  的 MCP 配置。name 對到 mcpServers 的 key (discord)
#
# Plugin 原本的 .mcp.json 用 ${CLAUDE_PLUGIN_ROOT}，但 --mcp-config 不會 substitute
# env var，所以這裡寫一份路徑已展開的版本。
MCP_CFG=/tmp/mcp-discord.json
if [ -n "$PLUGIN_ROOT" ]; then
    cat > "$MCP_CFG" <<EOF
{
  "mcpServers": {
    "discord": {
      "command": "bun",
      "args": ["run", "--cwd", "$PLUGIN_ROOT", "--shell=bun", "--silent", "start"]
    }
  }
}
EOF
fi

# 確保 bun 在 PATH（Dockerfile 在 build 時 export 過，runtime 還是要再 export 一次）
export PATH="/home/claude-user/.bun/bin:$PATH"

# server: channel + --dangerously-load-development-channels 啟動時會跳互動確認
# （「Enter to confirm · Esc to cancel」），容器無人按鍵會永遠卡住。
# 用 script(1) 給 claude 一個 PTY；背景 subshell 等 TUI 起來再送 1 個 Enter 確認，
# 後面 sleep infinity 把 stdin 持續開著（claude 看到 EOF 會跳出 channel 監聽迴圈）。
# 只送 1 個 \r —— 多送會被當成額外按鍵在 TUI 重複觸發、製造垃圾 channel。
exec script -qfc "claude \
    --mcp-config '$MCP_CFG' \
    --strict-mcp-config \
    --channels server:discord \
    --dangerously-load-development-channels server:discord \
    --dangerously-skip-permissions" /dev/null < <(sleep 8; printf '\r'; sleep infinity)
