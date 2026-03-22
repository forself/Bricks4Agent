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

This tool is broker-governed and intended for controlled deployment flows.
