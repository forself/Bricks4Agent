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

# 確保 Claude Code native build 已安裝（npm-installed 版本在 2.1.107+ 會印
# "switched from npm to native installer" 警告，且 --channels 模式下不會 spawn
# MCP plugin server，導致 bot 看似在線實則沒連 Discord Gateway）。
# native build 安裝到 ~/.local/bin/claude，PATH 加進來讓它優先生效。
if [ ! -x "$HOME/.local/bin/claude" ]; then
    echo "[entrypoint] installing Claude Code native build..."
    claude install || echo "[entrypoint] claude install failed (continuing with npm version)"
fi
export PATH="$HOME/.local/bin:$PATH"

# 寫一份路徑已展開的 mcp config 給 --mcp-config 用
# （plugin 原本的 .mcp.json 用 ${CLAUDE_PLUGIN_ROOT} 變數，但那個變數在 --channels
# 模式下不會被 substitute，導致 spawn 出來的 bun cwd 為錯誤路徑、立刻死掉。
# 寫一份固定路徑版避開這個 bug。）
PLUGIN_ROOT=$(ls -d /home/claude-user/.claude/plugins/cache/claude-plugins-official/discord/*/ 2>/dev/null | head -1 | sed 's:/$::')
if [ -n "$PLUGIN_ROOT" ] && [ -f "$PLUGIN_ROOT/.mcp.json" ]; then
    MCP_CONFIG=/tmp/discord-mcp.json
    cat > "$MCP_CONFIG" <<EOF
{
  "mcpServers": {
    "discord": {
      "command": "bun",
      "args": ["run", "--cwd", "$PLUGIN_ROOT", "--shell=bun", "--silent", "start"]
    }
  }
}
EOF
    echo "[entrypoint] mcp config written to $MCP_CONFIG (plugin_root=$PLUGIN_ROOT)"
    MCP_FLAG="--mcp-config $MCP_CONFIG"
else
    echo "[entrypoint] WARN: discord plugin not found, skipping --mcp-config"
    MCP_FLAG=""
fi

cd /home/claude-user/workspace
exec claude $MCP_FLAG --channels plugin:discord@claude-plugins-official --dangerously-skip-permissions
