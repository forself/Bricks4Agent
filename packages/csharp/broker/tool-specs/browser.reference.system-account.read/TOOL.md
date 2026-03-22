# Browser Reference: System Account Read

Status: `planned`

Purpose:

- document a browser capability that authenticates with a broker-owned system account

Identity mode:

- `system_account`

Rules:

- credentials come from broker-managed secret storage
- credentials are never exposed to users or models
- audit must record the site binding and system-account context

This is a reference spec.
It is not active until a browser worker, credential vault binding, and session policy are implemented.
