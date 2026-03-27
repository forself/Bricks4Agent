# Google Drive Delivery

## Purpose

Provide a broker-governed delivery path that uploads a generated artifact to Google Drive and returns a share link that can be sent back to the requesting user.

## First implementation

- identity modes:
  - `shared_delegated`
  - `user_delegated`
  - `system_account`
- credential sources:
  - a single broker-owned delegated OAuth credential for one Google account
  - per-user delegated OAuth credentials
  - Google service account JSON
- delivery pattern:
  1. generate file under the user's managed workspace
  2. upload file to a broker-configured Drive folder
  3. create a share link
  4. send the share link back through LINE or another broker-mediated channel

## Current preferred live mode

For the current local LINE sidecar, the preferred default is:

- `shared_delegated`

That means:

- broker stores one delegated OAuth credential for the operator's Google account
- all LINE users can have their generated artifacts uploaded into that one Google Drive
- broker metadata still records which LINE user owns the artifact
- Google Drive itself does not need to know each LINE user identity

This is the correct mode when all generated files should go into one Google Drive account.

Use `user_delegated` only when each end user should upload into their own Drive.

## Important operational constraint

Google service accounts do not have personal Drive storage quota for ordinary My Drive uploads.

This means the first implementation should use one of these:

- a Shared Drive folder, with the service account granted access
- OAuth-delegated user Drive access

If a normal My Drive folder is used, Google may return:

- `storageQuotaExceeded`

even when authentication is otherwise valid.

## Current broker surface

- tool spec: `delivery.google-drive.share`
- local admin status:
  - `GET /api/v1/local-admin/delivery/google-drive/status`
- local admin share:
  - `POST /api/v1/local-admin/delivery/google-drive/share`

## Required configuration

- `GoogleDriveDelivery:ServiceAccountJsonPath`
- `GoogleDriveDelivery:DefaultFolderId`
- optional:
  - `GoogleDriveDelivery:DefaultShareMode`
  - `GoogleDriveDelivery:DefaultPermissionRole`
  - `GoogleDriveDelivery:DefaultIdentityMode`
  - `GoogleDriveDelivery:SharedDelegatedChannel`
  - `GoogleDriveDelivery:SharedDelegatedUserId`

## Current operator reality

There is currently no end-user frontend for artifact browsing or downloading.

What exists today:

- local admin console
- LINE notification links
- broker-managed artifact records

What should exist later as a frontend feature:

- an authenticated public-facing artifact download API
- user-facing artifact history and download page
- broker-governed access checks before file delivery

This is intentionally recorded here as a missing frontend capability, not as a completed feature.

## Next steps

- add shared-drive-oriented target metadata to the admin console
- add artifact registry integration so generated files can be tracked and re-delivered
- support broker-governed notification back to the user after upload
- add authenticated frontend download API for direct external service scenarios
