# restart-web-app

## Summary
Restart a Creatio web application.

## Description
Use this command to forcibly restart a Creatio web site without [clearing the application cache](./clear-redis-db.md). 
It can be used during development or from a CI/CD pipeline to restart the target environment.

## Aliases
- restart

## Options

| Name                | Short | Type   | Required | Description                                    | Default  |
|---------------------|-------|--------|----------|------------------------------------------------|----------|
| `--timeout`         |       | int    | No       | Request timeout in milliseconds (inherited).   | `100000` |
| `--uri`             | `-u`  | string | No       | Application URI.                               |          |
| `--Password`        | `-p`  | string | No       | User password.                                 |          |
| `--Login`           | `-l`  | string | No       | User login.                                    |          |
| `--Maintainer`      | `-m`  | string | No       | Maintainer name.                               |          |
| `--Environment`     | `-e`  | string | No       | Environment name.                              |          |
| `--IsNetCore`       | `-i`  | bool   | No       | Use .NET Core application.                     |          |
| `--dev`             | `-c`  | bool   | No       | Developer mode state.                          |          |
| `--WorkspacePathes` |       | string | No       | Workspace path.                                |          |
| `--Safe`            | `-s`  | bool   | No       | Safe action in this environment.               |          |
| `--clientId`        |       | string | No       | OAuth client id.                               |          |
| `--clientSecret`    |       | string | No       | OAuth client secret.                           |          |
| `--authAppUri`      |       | string | No       | OAuth app URI.                                 |          |
| `--silent`          |       | bool   | No       | Use default behavior without user interaction. | `false`  |

## Examples
- Restart the default environment
```bash
clio restart-web-app
```
- Restart an environment named `MyEnv`
```bash
clio restart-web-app -e MyEnv
```
