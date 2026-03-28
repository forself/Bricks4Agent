# Broker Artifact Download API Design

Date: 2026-03-29
Status: approved design draft for first broker-owned artifact download path

## Goal

Add a broker-owned download path for generated artifacts so the system can return a first-party downloadable link when Google Drive delivery is unavailable.

This first version is intentionally narrow:

- public signed download link
- no end-user login requirement
- one-hour expiration
- repeated downloads allowed until expiry
- link only sent to the user when Google Drive upload fails

## Why This Exists

The current system can generate artifacts locally and can often upload them to Google Drive.
However, when delegated Drive delivery is unavailable, the user-facing path degrades too far:

- the file may exist locally
- the artifact may be recorded in broker state
- but the user still has no broker-owned download surface

This feature fills that gap without requiring a complete end-user web account system.

## Scope

This first implementation covers:

- generating a signed broker download URL for an existing recorded artifact
- validating the signed URL without user login
- streaming the artifact file directly from the broker host
- using the current sidecar ngrok public URL as the first public base URL source
- switching LINE notification fallback text from local-only messaging to broker download messaging when Drive upload fails and broker public download is available

This first implementation does not cover:

- authenticated user artifact history pages
- permanent public URLs
- one-time-use links
- resumable or ranged downloads
- generalized multi-channel artifact portal UX

## User-Facing Behavior

### Primary behavior

The system still attempts Google Drive delivery first.

If Google Drive upload succeeds:

- keep the current behavior
- do not replace the Drive link with a broker link

If Google Drive upload fails:

- attempt to build a broker-owned signed download URL
- if successful, enqueue a LINE notification that includes:
  - file name
  - broker download link
- if broker public URL is unavailable, keep the current degraded local-only fallback message

### Download link behavior

The first version download link:

- is valid for one hour
- may be used multiple times during that window
- is anonymous but signed
- should download the artifact directly

## Security Model

### Signed public URL

The broker download URL should look conceptually like:

`/api/v1/artifacts/download/{artifactId}?exp=<unix-seconds>&sig=<signature>`

The signature should be generated with a broker-controlled secret using stable fields such as:

- artifact id
- expiration time
- current file name

The signature must not include internal filesystem paths in any user-visible form.

### Hard requirement: never expose internal paths

This is a non-negotiable design rule.

The implementation must never expose:

- physical file paths
- managed workspace roots
- documents roots
- project roots
- broker host path layout

This applies to:

- LINE notification text
- API response bodies
- download URLs
- error responses
- logs that are surfaced to end users or local admin UI fields intended for user-facing relay

Internal path resolution is broker-only.

### Error model

The public endpoint should only return generic outcomes:

- `403` invalid or tampered signature
- `410` expired link
- `404` artifact not found or file unavailable

No error response should reveal:

- whether a path exists on disk
- where the file is stored
- internal path names

## Public Base URL Strategy

There is currently no stable public site URL for the broker.

Therefore, the first version must use the active sidecar ngrok public URL as the public download base URL.

### Resolution strategy

The broker should resolve a public base URL from currently available sidecar state.

The design assumption is:

- if the canonical local live path is active, ngrok public URL already exists
- if the ngrok URL cannot be resolved, broker public download is considered unavailable

The implementation should avoid hardcoding a fixed external host name in this first version.

## Data And Service Design

### New broker service

Add a focused service responsible for:

- generating signed download URLs
- validating download signatures
- resolving broker public base URL
- reading a recorded artifact by id
- ensuring the resolved file exists before streaming

This service should not own artifact persistence.
Artifact persistence should remain in the existing high-level workspace service.

### Existing artifact record usage

Use the existing `HighLevelLineArtifactRecord` as the source of truth for:

- artifact id
- file name
- file path
- overall status

No new artifact table is required for version one.

### Secret configuration

Introduce a broker configuration secret dedicated to signed download URLs.

This secret must be separate in purpose from unrelated credentials.
The service must fail closed if the signing secret is missing.

## Endpoint Design

### Public endpoint

Add a public broker endpoint:

- `GET /api/v1/artifacts/download/{artifactId}`

Required query parameters:

- `exp`
- `sig`

Behavior:

1. validate parameters
2. validate expiration
3. validate signature
4. resolve artifact record
5. verify local file exists
6. stream file with safe download headers

### Response headers

Set download headers using only safe file metadata:

- `Content-Type`
- `Content-Length` when available
- `Content-Disposition: attachment; filename="<safe-file-name>"`

The response must use sanitized file names only.

## Notification Fallback Design

The current fallback message when Drive upload fails still references local-only delivery.

Version one should update that branch:

- if broker signed download URL is available:
  - notify user that the file is ready
  - include safe file name
  - include broker download link
- if broker signed download URL is unavailable:
  - keep the current local-only degraded message

The notification body must not include local file path.

## Testing Strategy

This feature should be implemented with TDD.

Minimum verification coverage:

1. signed URL generation returns a broker URL when ngrok public URL is available
2. valid signed URL downloads the expected artifact bytes
3. invalid signature is rejected with `403`
4. expired link is rejected with `410`
5. missing artifact or missing file is rejected with `404`
6. user-facing fallback notification body does not contain internal path text
7. Drive failure plus broker public URL available produces broker download fallback content
8. Drive failure plus broker public URL unavailable keeps degraded local-only fallback behavior

## File-Level Change Direction

Expected implementation areas:

- broker public endpoint mapping
- new broker download signing/validation service
- artifact workspace service reuse for artifact lookup
- artifact delivery service fallback message update
- verify project coverage for signed download and no-path-leak behavior

The design intentionally keeps the change surface narrow and broker-centered.

## Risks

### Public URL instability

Because the first version depends on ngrok, any tunnel rotation invalidates newly generated base URLs after the tunnel changes.

This is acceptable for version one because:

- links are already short-lived
- the system currently has no fixed public broker host

### Anonymous signed links

Anyone with the signed URL can use it until expiry.

This is acceptable for version one because:

- the link lifetime is short
- the link is hard to guess
- the user explicitly asked for a broker-owned download fallback without requiring login

### Path leakage regression

The largest implementation risk is accidentally surfacing `filePath` or related fields through:

- fallback notification text
- API error messages
- admin-to-user copied content

This must be explicitly tested.

## Recommendation

Implement the first broker-owned download path exactly as:

- signed anonymous URL
- one-hour expiry
- repeat downloads allowed
- ngrok-based public base URL
- only used in user-facing LINE flow when Google Drive upload fails
- never expose internal paths

This is the smallest version that provides a real first-party artifact download path without waiting for a full authenticated user portal.
