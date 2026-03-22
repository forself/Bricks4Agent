# Deploy to Azure VM IIS

Status: `beta`

Purpose:

- publish a .NET project
- package the publish output
- deploy the package to a broker-registered Azure VM IIS target

Rules:

- the target must be registered in broker admin
- the project path must be absolute and resolve to exactly one `.csproj`
- transport is currently `winrm_powershell`
- credentials are referenced by `secret_ref` and resolved by broker-side deployment configuration
- target deployment modes:
  - `site_root`: deploy directly to the IIS site physical path
  - `iis_application`: deploy under a parent IIS site as a child application
- when the target uses `iis_application`, broker target registration must define:
  - `application_path` such as `/apps/project-a`
  - optional `health_check_path`

This tool is broker-governed and intended for controlled deployment flows.
