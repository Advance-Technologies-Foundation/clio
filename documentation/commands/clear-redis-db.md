# clear-redis-db

## Summary
Clear the Redis cache of a Creatio web application.

## Description
Use this command when you need to purge the Redis database for a Creatio environment. It removes cached data without restarting the application.

## Aliases
- flushdb

## Options

| Name | Short | Type | Required | Description | Default |
|------|-------|------|----------|-------------|---------|
| `--timeout` | | int | No | Request timeout in milliseconds (inherited). | `100000` |
| `--uri` | `-u` | string | No | Application URI. | |
| `--Password` | `-p` | string | No | User password. | |
| `--Login` | `-l` | string | No | User login. | |
| `--Maintainer` | `-m` | string | No | Maintainer name. | |
| `--Environment` | `-e` | string | No | Environment name. | |
| `--IsNetCore` | `-i` | bool | No | Use .NET Core application. | |
| `--dev` | `-c` | bool | No | Developer mode state. | |
| `--WorkspacePathes` | | string | No | Workspace path. | |
| `--Safe` | `-s` | bool | No | Safe action in this environment. | |
| `--clientId` | | string | No | OAuth client id. | |
| `--clientSecret` | | string | No | OAuth client secret. | |
| `--authAppUri` | | string | No | OAuth app URI. | |
| `--silent` | | bool | No | Use default behavior without user interaction. | `false` |

## Examples
- Clear cache for the default environment
```bash
clio clear-redis-db
```
- Clear cache for an environment named `MyEnv`
```bash
clio clear-redis-db MyEnv
```
