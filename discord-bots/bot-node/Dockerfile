# B4A bot-node — Phase 2: discord.js + claude --print headless
#
# 用 debian-slim 而非 alpine：claude-code npm package 在 alpine glibc 不全的環境
# 偶爾會 spawn 失敗（subprocess 連 dynamic link 都進不去），slim 比較穩。
FROM node:22-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# 跟既有 claude bot 同 user 名稱 + uid → /home/claude-user/.claude mount 路徑相容
RUN useradd -m -u 10001 -s /bin/bash claude-user

# 全裝 claude-code（headless --print 模式用）
# 不 pin 太死、用同既有 claude bot 的 v2.1.126：plugin spawn fix 那版（雖然我們不用 plugin、但語意對齊）
RUN npm install -g @anthropic-ai/claude-code@2.1.126

WORKDIR /app
COPY package.json ./
RUN npm install --omit=dev
COPY src ./src

RUN chown -R claude-user:claude-user /app
USER claude-user

ENV NODE_ENV=production
ENV ACCESS_JSON_PATH=/app/access.json
ENV BOT_PHASE=2
# 關 claude code auto-update（rootless 容器寫不進 /usr/local/lib/node_modules）
ENV DISABLE_AUTOUPDATER=1

EXPOSE 0

CMD ["node", "src/index.js"]
