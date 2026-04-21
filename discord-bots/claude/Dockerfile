FROM node:22-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl unzip git ca-certificates jq \
    && rm -rf /var/lib/apt/lists/*

RUN useradd -m -s /bin/bash claude-user

RUN npm install -g @anthropic-ai/claude-code

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

USER claude-user
WORKDIR /home/claude-user

RUN curl -fsSL https://bun.sh/install | bash
ENV PATH="/home/claude-user/.bun/bin:${PATH}"

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
