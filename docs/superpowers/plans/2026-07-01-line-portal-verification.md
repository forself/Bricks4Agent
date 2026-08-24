# LINE Portal Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Require new LINE users to bind a Portal account with a one-time verification code before using LINE interactions.

**Architecture:** Store LINE binding and a hashed verification code on `PortalUserCredential`. Portal registration returns the clear one-time code once. `HighLevelCoordinator` gates raw LINE ids until `/verify <user_id> <code>` succeeds, then uses the Portal user id for profile, history, permissions, and artifacts.

**Tech Stack:** C# .NET 8, BaseOrm, SQLite, xUnit/FluentAssertions, vanilla JavaScript user Portal.

---

## File Structure

- Modify `packages/csharp/broker-core/Models/PortalUserCredential.cs`: add LINE binding and verification code fields.

- Modify `packages/csharp/broker-core/Data/BrokerDbInitializer.cs`: add migration columns and indexes.

- Modify `packages/csharp/broker/Services/PortalAuthService.cs`: generate and verify LINE codes.

- Modify `packages/csharp/broker/Services/HighLevelCoordinator.cs`: reject unverified raw LINE users, handle `/verify`.

- Modify `packages/csharp/broker/Endpoints/PortalEndpoints.cs`: expose verification info in registration/status/me responses.

- Modify `packages/javascript/browser/user-portal/src/PortalApp.js`: show verification code after registration and in profile panel.

- Modify `tools/scripts/validate-user-portal.mjs`: assert the Portal mentions LINE verification.

- Create `packages/csharp/tests/unit/Portal/PortalLineVerificationTests.cs`: service and coordinator behavior tests.

- Modify `packages/csharp/tests/integration/Api/PortalEndpointTests.cs`: endpoint flow tests.

- Modify docs in `docs/manuals/current-user-manual.zh-TW.md` and `docs/manuals/current-technical-manual.zh-TW.md`.

## Task 1: Data Model and Portal Service Tests

- [ ] Add service-level unit tests for Portal registration code generation and verification.

- [ ] Run the filtered unit tests and confirm they fail because fields/methods are missing.

- [x] Add model columns and initializer migrations.

- [x] Add verification code generation, hashing, expiration, and binding methods via `PortalLineVerificationService`.

- [ ] Run the filtered unit tests and confirm they pass.

Current coverage note: this implementation is verified by API integration tests in `PortalEndpointTests` and frontend smoke validation. Service-level unit tests remain useful hardening work but were not added in this pass.

## Task 2: LINE Gate and Verify Command

- [ ] Add failing unit tests for unverified LINE rejection, wrong-code rejection, and correct-code success.

- [ ] Run the filtered unit tests and confirm they fail.

- [x] Implement `/verify` and `/驗證` handling in `HighLevelCoordinator`.

- [x] Resolve verified raw LINE ids to Portal user ids before normal processing.

- [ ] Run the filtered unit tests and confirm they pass.

## Task 3: API and Portal UI

- [x] Add failing integration test for register -> code -> LINE verify -> normal LINE message.

- [x] Run the filtered integration test and confirm it fails.

- [x] Return `line_verification` from Portal registration/status/me.

- [x] Display the verification code and `/verify` command in the Portal UI.

- [x] Update `validate-user-portal.mjs` for the new UI flow.

- [x] Run integration and portal validation.

## Task 4: Documentation and Full Verification

- [x] Update current user and technical manuals.

- [x] Run `dotnet build packages/csharp/ControlPlane.slnx`.

- [ ] Run signed unit, integration, broker, db validation, and user portal validation.

- [ ] Clean test artifacts.

- [ ] Commit the completed change.
