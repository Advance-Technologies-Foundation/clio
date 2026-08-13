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
config --knowledge-feedback-mode <ask|auto|off> [--knowledge-feedback-destination <repository-url>] [--knowledge-feedback-reporting-scope <full|sanitized>]
config --reset
```

## Description

Views and sets clio-wide defaults that are applied when a command is run without
the matching option, and persists them to clio's `appsettings.json`.

The command manages **deploy-creatio defaults** and the **knowledge-feedback
policy** used by coding agents. Deploy defaults are the fallback
values used by `deploy-creatio` when an option is not supplied on the command
line. Their main purpose is to make the Windows Explorer context-menu action
("clio: deploy Creatio"), which runs `clio deploy-creatio --zip-file "%1"` with
no other arguments, deploy to a **local database and Redis** instead of falling
back to a Kubernetes cluster.

Options passed on the `deploy-creatio` command line always take precedence over
these defaults. When no default site name is configured and none is passed on
the command line, interactive deployment asks the user to enter a site name.
This includes the Windows Explorer right-click action.

The knowledge-feedback policy tells an agent what to do when observed behavior
contradicts guidance. `ask` asks for each discrepancy, `auto` grants standing
approval to file automatically, and `off` disables reporting. Standing approval
is versioned only by the SHA-256 of the dedicated `knowledge-feedback` article.
The repository URL and scope remain explicit configuration. An unrelated knowledge-library
version, sequence, or article change does not invalidate it. If that reporting
article changes, configured mode remains `auto` while effective mode becomes
`ask` until `auto` is explicitly approved again.

## Options

```bash
--deploy-db-server-name <name>     Default local database server name for deploy-creatio.
                                   Must be a key in the 'db' block of appsettings.json.

--deploy-redis-server-name <name>  Default local Redis server name for deploy-creatio.
                                   Must be a key in the 'redis' block of appsettings.json.

--deploy-site-name <name>          Default site name for deploy-creatio. When unset,
                                   interactive deployment prompts for the site name.

--deploy-site-port <port>          Default site port for deploy-creatio.

--deploy-deployment <method>       Default deployment method for deploy-creatio: auto|iis|dotnet.

--knowledge-feedback-mode <mode>  Feedback mode: ask|auto|off. Supplying auto
                                   approves the current reporting article for
                                   the configured repository and scope.

--knowledge-feedback-destination <url>
                                   Exact credential-free HTTPS GitHub repository URL.

--knowledge-feedback-reporting-scope <scope>
                                   full for comprehensive internal reports;
                                   sanitized for public-safe reports.

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

# Grant standing approval for comprehensive reports in a private GHE repository
clio config --knowledge-feedback-destination https://creatio.ghe.com/engineering/clio-feedback --knowledge-feedback-reporting-scope full --knowledge-feedback-mode auto

# Grant standing approval for sanitized reports in the public Clio repository
clio config --knowledge-feedback-destination https://github.com/Advance-Technologies-Foundation/clio --knowledge-feedback-reporting-scope sanitized --knowledge-feedback-mode auto

# Revoke automatic reporting but keep asking about discrepancies
clio config --knowledge-feedback-mode ask
```

After configuring the defaults above, the "clio: deploy Creatio" Windows
Explorer right-click action deploys to the local database and Redis without any
further arguments. If `--deploy-site-name` is not configured, the CLI asks for
the site name before deployment proceeds.

## Behavior

- With no arguments (or with `--show`), prints the `appsettings.json` path and a
  tables for deploy-creatio defaults and the configured/effective knowledge-feedback policy.
- With one or more `--deploy-*` arguments, updates only the supplied values,
  persists them, and prints the resulting defaults.
- With `--reset`, removes the stored deploy-creatio defaults entirely.
- `--reset` takes precedence over any `--deploy-*` arguments in the same call.
- Knowledge-feedback policy can also be inspected with the non-resident
  `get-knowledge-feedback-policy` MCP tool and changed with
  `configure-knowledge-feedback-policy`; invoke both through `clio-run`. The
  configuration tool is classified high-impact/destructive so the MCP host can
  gate changes that authorize future external reporting. For auto authorization
  or retargeting, it requires `confirmed: true` plus the exact
  `expected-policy-hash`, `expected-destination`, and `expected-reporting-scope`
  values the agent showed to the user; a stale snapshot is refused.
- Clio stores policy and approval only. The agent files the issue through its
  existing GitHub capability and credentials; Clio never submits it.

## Exit Codes

    0   Displayed or updated the configuration successfully
    1   Validation error (for example an invalid deployment, feedback mode,
        repository URL, or reporting scope; auto also fails if the reporting
        article is unavailable)

## See Also

- [deploy-creatio](deploy-creatio.md) - Deploy Creatio from a zip file
- [register](register.md) - Register clio commands in the Windows context menu

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#config)
