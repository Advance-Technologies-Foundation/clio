# Clear redis

`clear-redis-db` command can be used in CI/CD pipeline or in development
when you need forcibly clear a web application (website) cache. Be
attentive, the command only clears web application cache and doesn't
restart it. To restart web application (website) you can use
[`restart`](./restart.md) command.

### Aliases
- flushdb

### SYNOPSIS
clear-redis-db [options]
> [!WARNING]
> Either `-e` option or `-l -u -p` options must be specified


### OPTIONS

| Option          | Short | Description                                                                                   |
|:----------------|:-----:|:----------------------------------------------------------------------------------------------|
| `--uri`         | `-u`  | Application URI. If not specified, the current web application will be used.                  |
| `--Password`    | `-p`  | User password. If not specified, the current user password will be used.                      |
| `--Login`       | `-l`  | User login. If not specified, the current user login will be used.                            |
| `--Environment` | `-e`  | [Environment name](./reg-web-app.md). If not specified, the current environment will be used. |
| `--Maintainer`  | `-m`  | Maintainer name. If not specified, the current maintainer will be used.                       |
| `--timeout`     |       | Operation timeout duration. If not specified, the default (`100,000`) timeout will be used.   |



## EXAMPLE

- Clear redis database for a default web application(website)
    ```ps
    clio clear-redis-db
    ```
- Clear redis database for a web application(website) that is registered as `myapp`, see [how to register Creatio with clio](./reg-web-app.md) for more information.
    ```ps
    clio clear-redis-db -e myapp
    ```