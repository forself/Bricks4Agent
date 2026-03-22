# Azure VM IIS Deployment

Status: implemented as a broker-governed admin/deployment path.

Current scope:

- register Azure VM IIS deployment targets in broker
- build canonical deployment requests from a tool spec
- preview deployment plans and generated PowerShell remoting script
- execute `dotnet publish`, package the output, and run a WinRM/PowerShell deployment script
- dispatch through broker route `deploy_azure_vm_iis`

Current broker entities:

- `AzureIisDeploymentTarget`
  - provider: `azure_vm_iis`
  - transport: `winrm_powershell`
  - VM host / port / TLS
  - IIS site name / app pool / physical path
  - `secret_ref`

Current broker admin endpoints:

- `POST /api/v1/deployment-admin/targets/list`
- `POST /api/v1/deployment-admin/targets/get`
- `POST /api/v1/deployment-admin/targets/upsert`
- `POST /api/v1/deployment-admin/requests/build`
- `POST /api/v1/deployment-admin/requests/preview`
- `POST /api/v1/deployment-admin/requests/execute`

Tool spec:

- `deploy.azure-vm-iis`
- broker route: `deploy_azure_vm_iis`
- status: `beta`

Execution flow:

1. Resolve tool spec and deployment target.
2. Resolve the project file from an absolute project path.
3. Run `dotnet publish`.
4. Zip the publish output.
5. Generate a PowerShell remoting script.
6. Resolve deployment credentials from broker-side secret bindings.
7. Run the PowerShell script against the registered VM.

Secret resolution:

- preferred: broker configuration section `DeploymentSecrets:Mappings`
- fallback: environment variables
  - `BRICKS4AGENT_DEPLOY_SECRET__<NORMALIZED_SECRET_REF>__USERNAME`
  - `BRICKS4AGENT_DEPLOY_SECRET__<NORMALIZED_SECRET_REF>__PASSWORD`

Current limits:

- only `winrm_powershell` is implemented
- target project path must be absolute
- directory input must resolve to exactly one `.csproj`
- the target VM must already have:
  - PowerShell remoting enabled
  - IIS installed
  - `WebAdministration` module available
- the current implementation assumes broker-owned credentials; interactive user-delegated deployment is not implemented yet

Operational note:

- this path is intentionally broker-governed
- deployment credentials are never passed from the model directly
- the broker builds the request, resolves the secret, and executes the remote command
