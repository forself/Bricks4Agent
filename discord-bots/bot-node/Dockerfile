# B4A bot-node — Phase 1 echo bot
# Lightweight node container; no Claude Code, no extra tooling.
FROM node:20-alpine

WORKDIR /app

# 預裝相依、cache 友善
COPY package.json ./
RUN npm install --omit=dev

COPY src ./src

# 非 root 跑（多一層防護、跟 cap_drop:ALL + no-new-privileges 互補）
RUN addgroup -g 10001 botuser && adduser -D -u 10001 -G botuser botuser
RUN chown -R botuser:botuser /app
USER botuser

ENV NODE_ENV=production
ENV ACCESS_JSON_PATH=/app/access.json
ENV BOT_PHASE=1

# Discord 出口要 443、不開任何 inbound port
EXPOSE 0

CMD ["node", "src/index.js"]
