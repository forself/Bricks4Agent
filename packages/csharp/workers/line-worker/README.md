# LINE Worker

Canonical live path:

`LINE webhook -> ngrok public URL -> line-worker -> broker /api/v1/high-level/line/process`

The worker is only the ingress bridge.
User-facing dialogue, routing, confirmation, and query/production decisions belong to the broker high-level layer, which is configured separately from execution/runtime LLM settings through `HighLevelLlm`.

Recommended sidecar flow:

1. Copy `appsettings.sidecar.example.json` to local `appsettings.json` and fill in real LINE credentials.
2. Run `start-sidecar-stack.ps1`.
3. Run `verify-live-webhook.ps1` to send a signed synthetic webhook event.
4. Use `status-sidecar-stack.ps1` and `stop-sidecar-stack.ps1` for runtime control.

Default sidecar ports:

- broker: `5361`
- line-worker webhook: `5357`

Why `5357`:

- this machine already has a reusable `http://*:5357/` URL ACL
- it avoids the old long-running `19090` worker
- it avoids the more collision-prone `8xxx` range
- sidecar therefore binds `WebhookHost="*"` on this port

Notes:

- `appsettings.json` is local-only and ignored by git.
- `start-sidecar-stack.ps1` creates a `.run/line-sidecar` workspace and updates the LINE webhook endpoint to the active tunnel URL unless `-SkipWebhookUpdate` is used.
- `verify-live-webhook.ps1` reads the current LINE webhook endpoint from LINE first, then falls back to `.last-tunnel-url`.
- all broker and webhook verification should use UTF-8 input; use `verify-high-level-process.ps1` or `verify-live-webhook.ps1` with `-MessageFile` / `-MessageBase64Utf8` when shell encoding is unreliable.
- `start-with-tunnel.ps1` is a legacy tunnel launcher path, not the canonical sidecar flow.
- the current canonical sidecar pair is `5357` for the webhook ingress and `5361` for the broker.
