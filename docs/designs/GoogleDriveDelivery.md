# Google Drive Delivery

## Purpose

Provide a broker-governed delivery path that uploads a generated artifact to Google Drive and returns a share link that can be sent back to the requesting user.

## First implementation

- identity mode: `system_account`
- credential source: Google service account JSON
- delivery pattern:
  1. generate file under the user's managed workspace
  2. upload file to a broker-configured Drive folder
  3. create a share link
  4. send the share link back through LINE or another broker-mediated channel

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

## Next steps

- add shared-drive-oriented target metadata to the admin console
- add artifact registry integration so generated files can be tracked and re-delivered
- support broker-governed notification back to the user after upload
