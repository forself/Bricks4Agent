FROM node:20-bookworm-slim

WORKDIR /app

COPY tools/agent/container/mock-openai.js /app/mock-openai.js

RUN useradd --create-home --uid 10004 mockopenai \
    && chown -R mockopenai:mockopenai /app

USER mockopenai

ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["node", "/app/mock-openai.js"]
