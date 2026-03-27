# LINE Worker

Canonical live path:

`LINE webhook -> ngrok public URL -> line-worker -> broker /api/v1/high-level/line/process`

Operator runbook:

- [line-sidecar-runbook.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.md)
- [line-sidecar-runbook.zh-TW.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)

The worker is only the ingress bridge.
User-facing dialogue, routing, confirmation, and query/production decisions belong to the broker high-level layer, which is configured separately from execution/runtime LLM settings through `HighLevelLlm`.

Verified on 2026-03-22:

- signed live webhook verification returned `200`
- broker high-level processing accepted UTF-8 Chinese production input
- canonical sidecar ingress path remained functional on ports `5357/5361`

Current verified query boundary:

- explicit broker-mediated search is available through `?search <keywords>`
- plain `?query` messages still go through the high-level dialogue path
- real-time query tooling is not yet auto-selected for arbitrary query text; the current controlled live path is the explicit `?search` subcommand

Recommended sidecar flow:

1. Copy `appsettings.sidecar.example.json` to local `appsettings.json` and fill in real LINE credentials.
2. Run `line-sidecar.ps1 up`.
3. Run `line-sidecar.ps1 verify` to send a signed synthetic webhook event.
4. Use `line-sidecar.ps1 status`, `line-sidecar.ps1 restart`, and `line-sidecar.ps1 down` for runtime control.

Unified one-command entrypoint:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 restart
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -MessageBase64Utf8 <base64-utf8>
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify-broker -UserId test-user -MessageBase64Utf8 <base64-utf8>
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 down
```

The unified script is only an operator entrypoint. It still starts separate broker, line-worker, and ngrok processes.

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
- `line-sidecar.ps1` is the canonical operator entrypoint for local Windows sidecar usage.
- `start-sidecar-stack.ps1` creates a `.run/line-sidecar` workspace and updates the LINE webhook endpoint to the active tunnel URL unless `-SkipWebhookUpdate` is used.
- in sidecar mode, the observed managed workspace root is `.run/line-sidecar/broker/managed-workspaces`; production broker configuration can override this with an absolute `HighLevelCoordinator:AccessRoot`
- `verify-live-webhook.ps1` reads the current LINE webhook endpoint from LINE first, then falls back to `.last-tunnel-url`.
- all broker and webhook verification should use UTF-8 input; use `verify-high-level-process.ps1` or `verify-live-webhook.ps1` with `-MessageFile` / `-MessageBase64Utf8` when shell encoding is unreliable.
- `start-sidecar-stack.ps1`, `status-sidecar-stack.ps1`, `stop-sidecar-stack.ps1`, `verify-live-webhook.ps1`, and `verify-high-level-process.ps1` remain as lower-level scripts behind `line-sidecar.ps1`.
- `line-sidecar.ps1 restart` is the canonical reload path after broker or worker changes.
- `start-with-tunnel.ps1` is a legacy tunnel launcher path, not the canonical sidecar flow.
- the current canonical sidecar pair is `5357` for the webhook ingress and `5361` for the broker.
