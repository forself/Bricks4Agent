# LINE Sidecar Runbook

Date: 2026-03-26  
Scope: current local Windows sidecar operation for the live LINE ingress path  
Audience: operator / developer  

## Purpose

This runbook describes how to start, verify, operate, and troubleshoot the current live local path:

`LINE webhook -> ngrok public URL -> line-worker -> broker /api/v1/high-level/line/process`

The broker path above remains plain JSON, but it is now an authenticated worker path rather than a blind trust bypass.

This is the current canonical local operator path.

It does not describe:

- the legacy `agent --line-listen` route
- full container-only operation
- production-grade multi-host deployment

## Current Canonical Ports

- broker: `127.0.0.1:5361`
- line-worker webhook: `127.0.0.1:5357`
- ngrok tunnel name: `line5357`

## Sidecar State Persistence

- Sidecar runtime state now persists at:
  - `D:\Bricks4Agent\.run\line-sidecar\data\broker.db`
- This database stores broker-owned local state such as:
  - local admin credentials
  - shared context and high-level profiles
  - Google Drive delegated OAuth credentials
- `line-sidecar.ps1 restart` should preserve that state.
- If a Google Drive authorization was created before this persistence fix and later disappeared, re-authorize once on the current sidecar instance.

## Prerequisites

You need these available on the machine:

- Windows PowerShell 5.1 or newer
- .NET SDK/runtime sufficient to publish and run broker and line-worker
- `ngrok` installed and authenticated
- valid LINE channel credentials in local worker config

Optional but currently expected for the best live behavior:

- `Api.txt` at repo root for the high-level OpenAI-compatible API key
- Google OAuth client JSON at repo root matching `client_secret_*.json`
- a valid ngrok config at `%LOCALAPPDATA%\ngrok\ngrok.yml`

## Local-Only Files And Inputs

### 1. LINE worker config

File:

- [appsettings.json](/d:/Bricks4Agent/packages/csharp/workers/line-worker/appsettings.json)

This file is local-only and ignored by git.

At minimum, it must contain working values for:

- `Line.ChannelAccessToken`
- `Line.ChannelSecret`
- `Line.DefaultRecipientId`
- `Worker.Auth.WorkerType`
- `Worker.Auth.KeyId`
- `Worker.Auth.SharedSecret`

### 2. High-level model API key

File:

- `D:\Bricks4Agent\Api.txt`

Current sidecar behavior:

- `start-sidecar-stack.ps1` reads this file
- it injects the key into broker `HighLevelLlm.ApiKey`

### 3. Google Drive OAuth client

File pattern:

- `D:\Bricks4Agent\client_secret_*.json`

Current sidecar behavior:

- the first matching file is used
- delegated redirect URI is set to `http://127.0.0.1:5361/api/v1/google-drive/oauth/callback`
- Google Drive delivery mode is configurable:
  - `shared_delegated`
  - `user_delegated`
  - `system_account`
- if no override is provided and `Line.DefaultRecipientId` exists, sidecar now defaults to `shared_delegated`
- the shared delegated owner defaults to `Line.DefaultRecipientId`

## Canonical Operator Commands

All normal local operation should go through:

- [line-sidecar.ps1](/d:/Bricks4Agent/packages/csharp/workers/line-worker/line-sidecar.ps1)

### Start

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
```

This is the only normal startup command.

You should not need to manually start broker, line-worker, or ngrok first.

### Status

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
```

### Restart

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 restart
```

### Stop

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 down
```

### Verify broker path directly

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify-broker -UserId test-user -MessageBase64Utf8 <base64-utf8>
```

### Verify signed LINE-style webhook

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -MessageBase64Utf8 <base64-utf8>
```

## What `up` Actually Does

The start path currently performs these actions:

1. Creates `.run/line-sidecar`
2. Publishes broker into `.run/line-sidecar/broker`
3. Publishes line-worker into `.run/line-sidecar/line-worker`
4. Injects local production overrides for:
   - high-level API key
   - Google Drive OAuth settings
   - Google Drive default identity mode and shared delegated owner
5. Starts broker on `127.0.0.1:5361`
6. Starts line-worker on `*:5357`
7. Recreates ngrok tunnel `line5357`
8. Updates the LINE webhook endpoint unless `-SkipWebhookUpdate` is used
9. Waits until broker and local webhook are actually reachable before considering startup successful
10. Verifies that the named ngrok tunnel actually exists before treating startup as successful

Important clarification:

- if the local ngrok admin API on `127.0.0.1:4040` is not already available
- the script now starts an ngrok agent automatically with:
  - `ngrok start --none --config %LOCALAPPDATA%\ngrok\ngrok.yml`

So the startup document is now strict:

- if `up` does not work, the runbook must explain why
- it is no longer acceptable to assume the operator manually guessed how to bootstrap ngrok

The first start can take noticeably longer because the broker may seed local RAG data before becoming ready.

## Successful Start: Expected Signals

After `up`, you should expect all of these:

- `status` shows broker PID and line-worker PID running
- `status` shows ngrok PID running
- `status` shows ngrok public URL
- `status` shows LINE webhook endpoint and `active = True`
- local admin page opens:
  - `http://127.0.0.1:5361/line-admin.html`
- local broker responds:
  - `http://127.0.0.1:5361/api/v1/local-admin/status`
- local webhook responds to signed test:
  - `verify` returns `Webhook status: 200`
- public webhook should also be routable once the named ngrok tunnel exists

If `up` returns without these conditions being true, treat startup as failed.

## Admin Console

Current local admin console:

- `http://127.0.0.1:5361/line-admin.html`

Current behavior:

- localhost-only
- local admin login required
- if no admin password exists in DB, initial password is `admin`
- first login requires password change

This console currently includes:

- LINE user list and labels
- registration policy
- per-user permissions
- browser records
- deployment targets
- tool specs
- Google Drive OAuth and delivery actions

## Current Google Drive Delivery Modes

The broker now supports three delivery identities:

- `shared_delegated`
  - one Google account is authorized once
  - all LINE users upload into that same Drive
  - broker records still preserve which LINE user owns each artifact
- `user_delegated`
  - each LINE user authorizes their own Google Drive
- `system_account`
  - Google service account path, typically for Shared Drive scenarios

For the current local sidecar, the intended default is:

- `shared_delegated`

This matches the case where one operator-owned Google Drive is used for all uploaded artifacts.

## Settings Required For Downloads

Live delivery currently has two download paths:

- Google Drive remains the primary user-facing delivery path
- broker-owned signed download links are now the fallback path when Google Drive upload fails and the sidecar has a public URL

If you want a LINE user to actually receive a downloadable link after a document or website artifact is generated, you still need all of the following:

1. A working high-level model API
- `D:\Bricks4Agent\Api.txt`
- This is required before the broker can generate the artifact itself

2. A working Google OAuth client JSON
- `D:\Bricks4Agent\client_secret_*.json`
- The callback URI must match:
  - `http://127.0.0.1:5361/api/v1/google-drive/oauth/callback`

3. The correct Google Drive delivery mode
- If the requirement is "all LINE users upload into the same Google Drive":
  - use `shared_delegated`
- If you switch to `user_delegated`, each LINE user must authorize their own Drive

4. A valid Drive credential stored in the current sidecar DB
- The persistent sidecar DB is now:
  - `D:\Bricks4Agent\.run\line-sidecar\data\broker.db`
- If `google_drive_delegated_credentials` is empty, artifacts can still be generated locally, but cloud download links will not be available

5. The sidecar must be running the latest published build
- Current artifact delivery, shared delegated owner support, and persistent runtime DB all depend on the latest sidecar publish

Current behavior:

- if Google Drive upload succeeds, the LINE reply uses the Drive link
- if Google Drive upload fails and the sidecar public URL is available, the broker returns a short-lived signed download link
- if both paths are unavailable, delivery degrades to a no-link notification

There is still no dedicated end-user download page. The current fallback is a direct signed broker download endpoint.

## Current High-Level Model

For the current live LINE path, the high-level responder uses:

- provider: `openai-compatible`
- model: `gpt-5.4-mini`

This is separate from execution-model requests recorded for downstream tasks.

## Basic Live Usage

### Conversation

Send plain text in LINE:

- `hello`
- `please help me clarify my requirements`

### Project interview

The current explicit project-interview entry is:

- `/proj`

Current happy-path sequence:

1. send `/proj`
2. reply with `#ProjectName`
3. choose the closest project scope by number
4. choose the closest site-structure direction by number
5. review the generated PDF/JSON artifacts
6. answer with `/ok`, `/revise`, or `/cancel`

Important notes:

- prompts are bilingual
- the copy is intentionally written for general LINE users
- internal identifiers such as `tool_page`, `mini_app`, `structured_app`, and `template family` are not shown to the user

### Help and profile

- `?help`
- `?profile`

### Controlled search

- `?search Central Weather Administration official site`
- `?s Central Weather Administration official site`

### Transport

- `?rail Taipei Taichung today 18:00`
- `?hsr Taipei Taichung today 18:00`
- `?bus Taipei Taichung today 18:00`
- `?flight TPE KIX tomorrow`

### Production flow

- `/create a website prototype`
- `#MyProject`
- `confirm`

## User And Workspace Behavior

Each high-level LINE user gets broker-managed paths under the configured absolute access root:

- `conversations`
- `documents`
- `projects`

The current live sidecar commonly uses:

- `.run/line-sidecar/broker/managed-workspaces`

Production broker configuration may override this with an absolute access root.

## UTF-8 Verification Guidance

UTF-8 is mandatory.

Use these methods when you need trustworthy multilingual verification:

- `verify-high-level-process.ps1` with `-MessageFile`
- `verify-live-webhook.ps1` with `-MessageFile`
- or `-MessageBase64Utf8`

Do not trust shell inline text if the terminal encoding is behaving badly.

For Chinese or other multilingual tests, prefer a UTF-8 message file over direct terminal typing.

## Basic Troubleshooting

### 1. LINE does not respond at all

Check:

- `line-sidecar.ps1 status`
- ngrok tunnel exists
- LINE webhook endpoint is active
- local worker PID is running

Typical causes:

- ngrok tunnel died
- webhook endpoint not updated
- line-worker stopped
- ngrok agent never came up

Fix:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 restart
```

### 2. Public webhook returns `404` or ngrok error

Symptom:

- public webhook URL returns `404`
- response includes ngrok error such as `ERR_NGROK_3200`

Cause:

- tunnel is offline or stale

Fix:

- run `status`
- then `restart`

If the tunnel still does not come back:

- check `.run/line-sidecar/logs/ngrok.out.log`
- check `.run/line-sidecar/logs/ngrok.err.log`
- confirm `%LOCALAPPDATA%\ngrok\ngrok.yml` exists and contains a valid authtoken

### 3. Broker is up but LINE still says AI service unavailable

Typical causes:

- `Api.txt` missing or unreadable
- invalid upstream API key
- high-level model upstream returned `401` or `400`
- stale sidecar publish output

Check:

- `.run/line-sidecar/logs/broker.out.log`
- `.run/line-sidecar/logs/broker.err.log`

Fix:

- confirm `Api.txt` exists and contains a valid key
- restart sidecar

### 4. Google Drive OAuth returns `invalid_state` or `state_expired`

Cause:

- old authorization URL reused
- state already consumed or timed out

Fix:

- re-initiate OAuth from the admin console
- use the new URL immediately

### 5. Google Drive OAuth callback returns `500`

Typical causes:

- sidecar broker not restarted after OAuth callback changes
- stale publish output
- missing or invalid OAuth client JSON

Fix:

- confirm `client_secret_*.json` exists at repo root
- run `line-sidecar.ps1 restart`

### 6. Google Drive upload fails with `storageQuotaExceeded`

If using service account mode:

- personal My Drive folders are not enough
- service account uploads require Shared Drive or a delegated-user flow

Current preferred path for personal Google accounts:

- delegated OAuth user Drive

### 7. LINE artifact delivery succeeds in Drive but no LINE message arrives

Cause:

- uploaded user is a synthetic/test account, not a real LINE `U...` user
- notification queue exists, but LINE cannot deliver to a fake recipient

Check:

- admin console user label
- whether the selected user is marked as `真實 LINE`

### 8. Local admin page opens but login fails

Current rules:

- first boot without stored credential: password is `admin`
- first successful login requires password reset

If this is a live sidecar with an existing DB, the password may already have been changed.

If credentials are unknown, reset must be handled at the local broker DB/admin layer rather than by guessing.

### 9. Sidecar restart fails because publish output is locked

Typical cause:

- old broker or worker process still holds files

Current mitigation:

- the restart path waits for processes to stop
- publish output is cleared before republish

If it still fails:

- stop sidecar
- confirm broker and worker processes are truly gone
- run start again

## Logs And Working Paths

Current sidecar runtime directory:

- `D:\Bricks4Agent\.run\line-sidecar`

Key logs:

- `.run/line-sidecar/logs/broker.out.log`
- `.run/line-sidecar/logs/broker.err.log`
- `.run/line-sidecar/logs/line-worker.out.log`
- `.run/line-sidecar/logs/line-worker.err.log`
- `.run/line-sidecar/logs/ngrok.out.log`
- `.run/line-sidecar/logs/ngrok.err.log`

## What This Runbook Does Not Yet Cover

- production multi-host deployment of broker and line-worker
- hardened remote admin auth
- browser worker runtime operation
- full Azure IIS deployment operations
- complete disaster recovery procedures

## Missing Frontend Capability

There is currently no end-user frontend for artifact browsing or downloading.

What exists today:

- local admin console
- LINE messages containing delivery links
- broker-managed artifact records

What should exist later as a frontend feature:

- an authenticated artifact download API
- user-facing artifact history
- governed download authorization checks

This is a recorded future frontend requirement, not a completed feature.

## Related Documents

- [CurrentArchitectureAndProgress-2026-03-26.md](../reports/CurrentArchitectureAndProgress-2026-03-26.md)
- [README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)
- [GoogleDriveDelivery.md](../designs/GoogleDriveDelivery.md)
- [AzureVmIisDeployment.md](../designs/AzureVmIisDeployment.md)
