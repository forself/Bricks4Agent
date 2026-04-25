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

cd /home/claude-user/workspace
exec claude --channels plugin:discord@claude-plugins-official --dangerously-skip-permissions
