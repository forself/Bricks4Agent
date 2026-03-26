# Browser Reference: Anonymous Read

Status: `active`

Purpose:

- define the broker-governed anonymous browser-read capability
- provide the active capability contract for public-web read/navigation under broker control

Identity mode:

- `anonymous`

Rules:

- no login
- no user-delegated session
- no system account session
- allowed actions are limited to public read/navigation semantics

Current runtime support:

- broker in-process runtime is implemented for anonymous `public_open` read/navigation
- persisted evidence is recorded under `browser.execution.*`
- this tool is the canonical active entry for that runtime path

Not yet implemented:

- authenticated browser sessions
- delegated write/submit actions
- long-lived browser worker/session orchestration
