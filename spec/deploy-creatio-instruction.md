# Draft: Creatio Deployment on macOS Instruction

> **📖 Final Documentation:** [DeployCreatioMacOS.md](../clio/DeployCreatioMacOS.md)

---

## Prerequisites

- Rancher Desktop latest version - recommended 6GB Memory + 2 CPU for Rancher virtual machine
- .NET 8 SDK from Microsoft website
- clio version 8.0.1.71 or higher
- Creatio release ZIP file on local disk

## Commands

Execute `clio deploy-infrastructure` command (can be run from any directory), short form `clio di`

After that, from the directory you will use to store deployed applications, run:
```bash
clio deploy-creatio --ZipFile {PathToCreatioBuild}.zip
```

Enter the parameters that the application will request.

After the command completes, the deployed application will be registered in clio and automatically started.

To work with the application later, use these commands:

`clio hosts` - shows list of local applications

To start the application:
```bash
clio start -e ENV_NAME
```

To stop:
```bash
clio stop -e ENV_NAME
```

Or `clio stop --all` to stop all applications.

Access to Creatio database is available through pgAdmin at http://localhost:1080 with login root@creatio.com and password root


