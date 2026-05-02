FROM node:22-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl unzip git ca-certificates jq \
    && rm -rf /var/lib/apt/lists/*

RUN useradd -m -s /bin/bash claude-user

# Pin v2.1.126: v2.1.119 之前 `--channels plugin:discord@claude-plugins-official`
# 路徑 spawn 不出 plugin 子進程，訊息進不到 claude session（typing 後取消的根因）。
# v2.1.126 修了這條路徑且加上 server: 來源支援。詳見 entrypoint.sh L48-58。
RUN npm install -g @anthropic-ai/claude-code@2.1.126

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

USER claude-user
WORKDIR /home/claude-user

RUN curl -fsSL https://bun.sh/install | bash
ENV PATH="/home/claude-user/.bun/bin:${PATH}"

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
