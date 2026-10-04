# Base image is pinned by multi-arch index digest. Refresh it with
# `node tools/agent/container/resolve-base-image-digests.mjs` (docker or podman).
FROM node:22-bookworm-slim@sha256:43ac6c60b8f89723f746e8a92ce91abd5017e627ce1ddfe4238355d3a30b772c

WORKDIR /app

COPY tools/agent/container/mock-openai.js /app/mock-openai.js

RUN groupadd --gid 10007 mockopenai \
    && useradd --uid 10007 --gid 10007 --no-create-home --shell /usr/sbin/nologin mockopenai

USER 10007:10007

ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["node", "/app/mock-openai.js"]
