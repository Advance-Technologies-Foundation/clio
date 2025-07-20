# reg-web-app

## Summary
Register a Creatio web application in the local configuration file. 
Use this command to add a new environment, update credentials or set the active environment.

## Description
The configuration is stored in an `appsettings.json` file located in the application data folder 
 - On Windows `%LOCALAPPDATA%/creatio/clio` 
 - On Linux/macOS `$HOME/creatio/clio`. 

Run `clio cfg open` to open this file for manual editing, or `clio envs -s` to list all registered environments.
When invoked with `--add-from-iis`, the command scans the specified host (or localhost) for Creatio sites and adds each one. 
Use `--checkLogin` to validate credentials immediately after registration.

## Aliases
- reg
- cfg

## Options

| Name                  | Short | Type   | Required | Description                                                            | Default |
|-----------------------|-------|--------|----------|------------------------------------------------------------------------|---------|
| `EnvironmentName`     | n/a   | string | No       | Environment identifier. Specify `open` to open the configuration file. |         |
| `--ActiveEnvironment` | `-a`  | string | No       | Set the specified environment as active after registration.            |         |
| `--checkLogin`        |       | bool   | No       | Verify credentials after registration.                                 | `false` |
| `--add-from-iis`      |       | bool   | No       | Register all Creatio instances found in IIS.                           | `false` |
| `--host`              |       | string | No       | Machine name for `--add-from-iis`.                                     |         |
| `--uri`               | `-u`  | string | No       | Application URI.                                                       |         |
| `--Password`          | `-p`  | string | No       | User password.                                                         |         |
| `--Login`             | `-l`  | string | No       | User login.                                                            |         |
| `--Maintainer`        | `-m`  | string | No       | Maintainer name.                                                       |         |
| `--Environment`       | `-e`  | string | No       | Environment name (inherited).                                          |         |
| `--IsNetCore`         | `-i`  | bool   | No       | Use .NET Core application. Defaults to `false`.                        |         |
| `--dev`               | `-c`  | bool   | No       | Developer mode state.                                                  |         |
| `--WorkspacePathes`   |       | string | No       | Workspace path.                                                        |         |
| `--Safe`              | `-s`  | bool   | No       | Safe action in this environment.                                       |         |
| `--clientId`          |       | string | No       | OAuth client id.                                                       |         |
| `--clientSecret`      |       | string | No       | OAuth client secret.                                                   |         |
| `--authAppUri`        |       | string | No       | OAuth app URI.                                                         |         |
| `--silent`            |       | bool   | No       | Use default behavior without user interaction.                         | `false` |

## Examples
- Register a new environment named `MyEnv` with the specified URI, login, and password
```bash
clio reg-web-app MyEnv -u https://mysite.creatio.com -l administrator -p password
```
- Update credentials for an existing environment named `MyEnv`
```bash
clio reg-web-app MyEnv -l Supervisor -p Supervisor
```
- Set `MyEnv` environment as the active one
```bash
clio reg-web-app -a MyEnv
```
- Open the configuration file (`appsettings.json`) file in your default editor
```bash
clio cfg open
```
