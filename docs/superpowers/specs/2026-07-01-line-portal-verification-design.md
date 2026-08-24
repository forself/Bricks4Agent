# LINE Portal Verification Design

Date: 2026-07-01

## Goal

Change LINE onboarding so a new LINE user cannot self-register only by sending a message. A user must first register on the user Portal, receive a one-time verification code, then send the Portal account id and code in LINE to bind that LINE account.

## Flow

1. User registers in the Portal with `user_id`, password, and optional display name.

2. Portal registration creates the Portal credential and returns a short-lived one-time LINE verification code.

3. The user sends `/verify <user_id> <code>` or `/驗證 <user_id> <code>` in LINE.

4. Broker checks the Portal credential and code.

5. If the code is invalid, expired, already used, or for another account, Broker returns a rejection message and does not create an approved LINE profile.

6. If the code is valid, Broker binds the current LINE `userId` to the Portal account, marks the verification code used, and creates or updates the high-level profile as approved Basic.

7. Normal LINE commands are allowed only after this binding exists.

## Architecture

Use `PortalUserCredential` as the account-level source of truth and add LINE binding metadata plus a hashed verification code. The clear verification code is returned only once in the Portal registration response. `PortalAuthService` owns code generation and verification because it owns Portal credentials. `HighLevelCoordinator` owns LINE message gating and profile creation.

The logical LINE user used by high-level work after binding is the Portal `user_id`, not the raw LINE `U...` id. This keeps Portal and LINE histories/artifacts in one user workspace.

## Data Model

Add columns to `portal_user_credentials`:

- `line_user_id`: raw LINE user id after successful binding.

- `line_verification_code_hash`: SHA-256 hash of the current verification code.

- `line_verification_code_expires_at`: expiration timestamp.

- `line_verified_at`: timestamp of successful LINE verification.

Verification code:

- Generated as a random 6-digit numeric code.

- Valid for 10 minutes.

- Hashed before persistence.

- Cleared after success.

## Commands

Supported LINE commands:

```text
/verify <user_id> <code>
/驗證 <user_id> <code>
```

Failure messages are user-facing and concise:

- Account not found.

- Verification code is invalid or expired.

- This Portal account is already linked to another LINE user.

## Tests

Unit tests cover:

- Portal registration returns a verification code and stores only a hash.

- LINE messages before verification are denied.

- Wrong verification code is rejected.

- Correct verification code binds LINE to the Portal account.

- After binding, LINE messages use the Portal account workspace.

Integration tests cover:

- `/api/v1/portal/auth/register` returns `line_verification`.

- `/api/v1/high-level/line/process` rejects unverified LINE users.

- `/verify <user_id> <code>` succeeds only with the Portal-issued code.

## Documentation

Update the current user and technical manuals to describe:

- Website-first registration.

- One-time verification code.

- LINE `/verify` command.

- Admin/user expectations for Basic vs Member permissions.
