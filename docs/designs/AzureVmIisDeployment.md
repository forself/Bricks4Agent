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
  - IIS site name / deployment mode / app pool / physical path
  - optional child application path under a parent site
  - optional health check path
  - `secret_ref`

Deployment modes:

- `site_root`
  - deploy directly into the registered IIS site physical path
  - current `restart_site` behavior still applies
- `iis_application`
  - treat `site_name` as the parent site
  - treat `application_path` such as `/apps/project-a` as the IIS child application route
  - create or update the IIS application during deployment
  - recycle app pool, but do not stop/start the whole site as the main path

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
6. If the target uses `iis_application`, create or update the child IIS application under the parent site.
7. Resolve deployment credentials from broker-side secret bindings.
8. Run the PowerShell script against the registered VM.

Secret resolution:

- preferred: broker configuration section `DeploymentSecrets:Mappings`
- fallback: environment variables
  - `BRICKS4AGENT_DEPLOY_SECRET__<NORMALIZED_SECRET_REF>__USERNAME`
  - `BRICKS4AGENT_DEPLOY_SECRET__<NORMALIZED_SECRET_REF>__PASSWORD`

Current limits:

- only `winrm_powershell` is implemented
- target project path must be absolute
- directory input must resolve to exactly one `.csproj`
- `iis_application` mode assumes the target VM already has a parent IIS site created
- application path semantics are implemented at IIS deployment level; application-specific path-base adaptation remains app-dependent
- the target VM must already have:
  - PowerShell remoting enabled
  - IIS installed
  - `WebAdministration` module available
- the current implementation assumes broker-owned credentials; interactive user-delegated deployment is not implemented yet

Operational note:

- this path is intentionally broker-governed
- deployment credentials are never passed from the model directly
- the broker builds the request, resolves the secret, and executes the remote command
