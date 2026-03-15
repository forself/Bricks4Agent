#!/bin/sh
set -eu

require_env() {
  var_name="$1"
  eval "var_value=\${$var_name:-}"
  if [ -z "$var_value" ]; then
    echo "Missing required environment variable: $var_name" >&2
    exit 2
  fi
}

if [ "${1:-}" = "--help" ] || [ "${1:-}" = "-h" ] || [ "${1:-}" = "--version" ]; then
  exec node /app/tools/agent/agent.js "$@"
fi

require_env BROKER_URL
require_env BROKER_PUB_KEY
require_env BROKER_PRINCIPAL_ID
require_env BROKER_TASK_ID

if [ "${BROKER_WAIT_FOR_HEALTH:-1}" = "1" ]; then
  BROKER_HEALTH_URL="${BROKER_HEALTH_URL:-$BROKER_URL/api/v1/health}"
  BROKER_WAIT_TIMEOUT_SECONDS="${BROKER_WAIT_TIMEOUT_SECONDS:-60}"

  node - "$BROKER_HEALTH_URL" "$BROKER_WAIT_TIMEOUT_SECONDS" <<'EOF'
const [url, timeoutSecondsText] = process.argv.slice(2);
const timeoutMs = Math.max(1000, Number(timeoutSecondsText || '60') * 1000);
const startedAt = Date.now();

async function main() {
  while (Date.now() - startedAt < timeoutMs) {
    try {
      const response = await fetch(url, {
        method: 'GET',
        headers: { 'Accept': 'application/json' },
      });
      if (response.ok) {
        process.exit(0);
      }
    } catch (_) {
      // keep polling until timeout
    }

    await new Promise((resolve) => setTimeout(resolve, 1000));
  }

  console.error(`Broker health check timed out: ${url}`);
  process.exit(3);
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(3);
});
EOF
fi

WORKSPACE_DIR="${AGENT_PROJECT_ROOT:-/workspace}"
BROKER_ROLE="${BROKER_ROLE_ID:-role_reader}"

if [ ! -d "$WORKSPACE_DIR" ]; then
  echo "Workspace directory does not exist: $WORKSPACE_DIR" >&2
  exit 2
fi

cd "$WORKSPACE_DIR"

set -- \
  node /app/tools/agent/agent.js \
  --governed \
  --broker-url "$BROKER_URL" \
  --broker-pub-key "$BROKER_PUB_KEY" \
  --principal-id "$BROKER_PRINCIPAL_ID" \
  --task-id "$BROKER_TASK_ID" \
  --role-id "$BROKER_ROLE"

if [ -n "${AGENT_MODEL:-}" ]; then
  set -- "$@" --model "$AGENT_MODEL"
fi

if [ -n "${AGENT_MAX_ITERATIONS:-}" ]; then
  set -- "$@" --max-iterations "$AGENT_MAX_ITERATIONS"
fi

if [ "${AGENT_NO_CONFIRM:-1}" = "1" ]; then
  set -- "$@" --no-confirm
fi

if [ "${AGENT_VERBOSE:-0}" = "1" ]; then
  set -- "$@" --verbose
fi

if [ -n "${AGENT_RUN:-}" ]; then
  set -- "$@" --run "$AGENT_RUN"
fi

exec "$@"
