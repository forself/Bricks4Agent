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

- local admin delegated OAuth (`packages/csharp/broker/Endpoints/LocalAdminEndpoints.cs:754-778`):

- `GET /api/v1/local-admin/delivery/google-drive/oauth/status`

- `GET /api/v1/local-admin/delivery/google-drive/oauth/credentials`

- `POST /api/v1/local-admin/delivery/google-drive/oauth/start`

- OAuth callback (loopback only): `GET /api/v1/google-drive/oauth/callback` (`packages/csharp/broker/Endpoints/GoogleDriveOAuthEndpoints.cs:13`)

## Required configuration

- `GoogleDriveDelivery:ServiceAccountJsonPath` (for `system_account`) and/or `GoogleDriveDelivery:OAuthClientJsonPath` (for `shared_delegated` / `user_delegated`); delivery is enabled when either file exists

- `GoogleDriveDelivery:DefaultFolderId`

- optional:

- `GoogleDriveDelivery:DelegatedRedirectUri` (default `http://127.0.0.1:5361/api/v1/google-drive/oauth/callback`)

- `GoogleDriveDelivery:DefaultShareMode`

- `GoogleDriveDelivery:DefaultPermissionRole`

- `GoogleDriveDelivery:DefaultIdentityMode`

- `GoogleDriveDelivery:SharedDelegatedChannel`

- `GoogleDriveDelivery:SharedDelegatedUserId`

## Current operator reality

An end-user frontend for artifact browsing and downloading now exists (checked 2026-09-26).

What exists today:

- local admin console

- LINE notification links

- broker-managed artifact records

- signed artifact download API: `GET /api/v1/artifacts/download/{artifactId}?exp=&sig=` (HMAC link signed with `ArtifactDownload:SigningSecret`; `packages/csharp/broker/Endpoints/ArtifactDownloadEndpoints.cs:9`)

- user portal served at `/portal` (`packages/javascript/browser/user-portal`), which lists the signed-in user's own artifacts with download links via `GET /api/v1/portal/artifacts` (`packages/csharp/broker/Endpoints/PortalEndpoints.cs:139-176`, owner check on the single-artifact route)

Originally planned as later frontend features (now covered by the portal session-authenticated listing plus HMAC-signed download links above):

- an authenticated public-facing artifact download API

- user-facing artifact history and download page

- broker-governed access checks before file delivery

## Next steps

- add shared-drive-oriented target metadata to the admin console

- add artifact registry integration so generated files can be tracked and re-delivered

- support broker-governed notification back to the user after upload

- add authenticated frontend download API for direct external service scenarios
