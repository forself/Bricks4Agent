FROM node:20-bookworm-slim

WORKDIR /app

COPY tools/agent/container/mock-ollama.js /app/mock-ollama.js

RUN useradd --create-home --uid 10003 mock \
    && chown -R mock:mock /app

USER mock

ENV PORT=11434
EXPOSE 11434

ENTRYPOINT ["node", "/app/mock-ollama.js"]
