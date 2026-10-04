# Base image is pinned by multi-arch index digest. Refresh it with
# `node tools/agent/container/resolve-base-image-digests.mjs` (docker or podman).
FROM node:22-bookworm-slim@sha256:43ac6c60b8f89723f746e8a92ce91abd5017e627ce1ddfe4238355d3a30b772c

WORKDIR /app

COPY tools/agent/container/mock-ollama.js /app/mock-ollama.js

RUN groupadd --gid 10006 mock \
    && useradd --uid 10006 --gid 10006 --no-create-home --shell /usr/sbin/nologin mock

USER 10006:10006

ENV PORT=11434
EXPOSE 11434

ENTRYPOINT ["node", "/app/mock-ollama.js"]
