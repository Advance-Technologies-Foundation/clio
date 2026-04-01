# alm-deploy

## Command Type

    Application Lifecycle Management commands

## Name

alm-deploy - Deploy and install package to Creatio environment via ALM

## Description

The alm-deploy command uploads a package file to a Creatio environment and
triggers installation through the Application Lifecycle Management (ALM)
service. This command is designed for controlled deployments to specific
sites/environments managed by ALM.

The command performs chunked file upload for reliability with large packages
and returns an operation ID for tracking the deployment progress.

## Synopsis

```bash
clio alm-deploy <FILE_PATH> --site <SITE_NAME> [OPTIONS]
clio alm-deploy <FILE_PATH> --site <SITE_NAME> [OPTIONS]
```

## Required Arguments

    FILE_PATH (pos. 0)
        Path to the package file to deploy (typically .gz format)

## Options

```bash
--site              -t          Required. Target site/environment name in ALM

--Environment       -e          Environment name (registered configuration)

--uri               -u          Application URI (for direct connection)

--Login             -l          User login (administrator permission required)

--Password          -p          User password

--clientId                      OAuth client ID

--clientSecret                  OAuth client secret

--authAppUri                    OAuth authentication app URI

--general           -g          Use non-SSP user (default: false)

--timeout                       Request timeout in milliseconds (default: 100000)
```

## Authentication

    Three authentication methods are supported:

    1. Environment-based (recommended):
       Use -e to reference a pre-configured environment

    2. Direct credentials:
       Provide --uri, --Login, and --Password

    3. OAuth:
       Provide --uri, --clientId, --clientSecret, and --authAppUri

## Examples

```bash
alm-deploy package using registered environment:
clio alm-deploy MyPackage.gz --site Production -e prod-env

alm-deploy package with direct credentials:
clio alm-deploy MyPackage.gz --site Production -u https://myapp.creatio.com -l admin -p pass

alm-deploy using OAuth authentication:
clio alm-deploy MyPackage.gz --site Production -u https://myapp.creatio.com --clientId abc123 --clientSecret xyz789 --authAppUri https://auth.app.com

alm-deploy using non-SSP user:
clio alm-deploy MyPackage.gz --site Production -e prod-env --general

alm-deploy with custom timeout:
clio alm-deploy MyPackage.gz --site Production -e prod-env --timeout 60000
```

## Deployment Process

    1. Generates unique file ID
    2. Uploads package file in chunks to target environment
    3. Triggers installation operation on specified site
    4. Returns operation ID for tracking

## Service Endpoints

    With SSP user (default):
        /0/ssp/rest/InstallPackageService/UploadFile
        /0/ssp/rest/InstallPackageService/StartOperation

    With non-SSP user (--general flag):
        /0/rest/InstallPackageService/UploadFile
        /0/rest/InstallPackageService/StartOperation

## Output

    Success:
        File uploaded. FileId: <guid>
        Command to deploy packages to environment successfully started OperationId: <guid>
        Done

    Failure:
        File not uploaded. FileId: <guid>
        Operation not started. FileId: <guid>

## Return Values

    0   Package deployed successfully
    1   Deployment failed (upload or operation start failed)

## Prerequisites

- Creatio environment with ALM installed
- Valid package file (.gz format)
- Administrator or appropriate credentials
- Target site/environment must exist in ALM

## Notes

- Package files are uploaded in chunks for reliability
- Operation ID can be used to track deployment status in Creatio
- Use --general flag to select non-SSP service endpoint
- Environment-based auth requires pre-registration with 'clio register'
- File path can be absolute or relative

## See Also

push-pkg            Push package without ALM
install-gate        Install cliogate service
reg-web-app         Register Creatio environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#alm-deploy)
