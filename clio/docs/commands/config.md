# config

## Command Type

    System configuration

## Name

config - View and set clio configuration defaults

## Synopsis

```bash
config
config --show
config --deploy-db-server-name <name> [--deploy-redis-server-name <name>] [--deploy-site-name <name>] [--deploy-site-port <port>] [--deploy-deployment <auto|iis|dotnet>]
config --reset
```

## Description

Views and sets clio-wide defaults that are applied when a command is run without
the matching option, and persists them to clio's `appsettings.json`.

Currently the command manages the **deploy-creatio defaults** — the fallback
values used by `deploy-creatio` when an option is not supplied on the command
line. Their main purpose is to make the Windows Explorer context-menu action
("clio: deploy Creatio"), which runs `clio deploy-creatio --zip-file "%1"` with
no other arguments, deploy to a **local database and Redis** instead of falling
back to a Kubernetes cluster.

Options passed on the `deploy-creatio` command line always take precedence over
these defaults. When no default site name is configured and none is passed on
the command line, the site name is derived from the deployed zip file name.

## Options

```bash
--deploy-db-server-name <name>     Default local database server name for deploy-creatio.
                                   Must be a key in the 'db' block of appsettings.json.

--deploy-redis-server-name <name>  Default local Redis server name for deploy-creatio.
                                   Must be a key in the 'redis' block of appsettings.json.

--deploy-site-name <name>          Default site name for deploy-creatio. When unset, the
                                   site name is derived from the deployed zip file name.

--deploy-site-port <port>          Default site port for deploy-creatio.

--deploy-deployment <method>       Default deployment method for deploy-creatio: auto|iis|dotnet.

--reset                            Clear the stored deploy-creatio defaults.

--show                             Show the current configuration defaults (default when no
                                   other arguments are supplied).
```

## Examples

```bash
# Show the current configuration defaults
clio config

# Configure local deployment defaults for the Explorer right-click action
clio config --deploy-db-server-name my-local-postgres --deploy-site-port 40018 --deploy-deployment iis

# Add a default local Redis server name
clio config --deploy-redis-server-name local-redis

# Clear all deploy-creatio defaults
clio config --reset
```

After configuring the defaults above, the "clio: deploy Creatio" Windows
Explorer right-click action deploys to the local database and Redis without any
further arguments.

## Behavior

- With no arguments (or with `--show`), prints the `appsettings.json` path and a
  table of the current deploy-creatio defaults.
- With one or more `--deploy-*` arguments, updates only the supplied values,
  persists them, and prints the resulting defaults.
- With `--reset`, removes the stored deploy-creatio defaults entirely.
- `--reset` takes precedence over any `--deploy-*` arguments in the same call.

## Exit Codes

    0   Displayed or updated the configuration successfully
    1   Validation error (for example an invalid --deploy-deployment value)

## See Also

- [deploy-creatio](deploy-creatio.md) - Deploy Creatio from a zip file
- [register](register.md) - Register clio commands in the Windows context menu

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#config)
