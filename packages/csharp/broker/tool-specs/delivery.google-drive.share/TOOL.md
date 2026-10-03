# Google Drive Share Delivery

Status: `beta`

Purpose:
- upload a broker-generated artifact to a broker-configured Google Drive folder
- return a governed share link suitable for delivery back to the requesting user

Identity:
- supported identity modes: `system_account` (broker-owned Google service account), `user_delegated` (OAuth credential of the requesting channel user), `shared_delegated` (one broker-configured OAuth credential, `GoogleDriveDelivery:SharedDelegatedChannel` / `SharedDelegatedUserId`)
- default is `shared_delegated` (`GoogleDriveDelivery:DefaultIdentityMode`); any other value is rejected
- delegated credentials are obtained through the broker's loopback-only OAuth callback (`/api/v1/google-drive/oauth/callback`)

Input:
- `file_path`: absolute path to a local file
- `file_name`: optional display name override
- `folder_id`: optional Drive folder override
- `share_mode`: `restricted` or `anyone_with_link`

Output:
- Drive file id
- web view link
- download link
- effective share mode

Governance:
- only broker-local files should be uploaded
- service account credentials remain broker-owned
- resulting link should be sent back as a delivery artifact, not treated as an execution instruction
