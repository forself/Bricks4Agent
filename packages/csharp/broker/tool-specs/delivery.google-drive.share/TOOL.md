# Google Drive Share Delivery

Purpose:
- upload a broker-generated artifact to a broker-configured Google Drive folder
- return a governed share link suitable for delivery back to the requesting user

Identity:
- first implementation uses a broker-owned Google service account
- no user-delegated Drive access is assumed

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
