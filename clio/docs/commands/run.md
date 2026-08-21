# run

## Name

run - Run a YAML scenario and refresh environments between dependent steps

## Synopsis

```bash
clio run --file-name <scenario.yaml> [OPTIONS]
```

## Aliases

scenario, run-scenario

## Description

Runs all commands declared in a YAML scenario. Environment-dependent steps
refresh appsettings.json before resolving their target, so a deployment step
can register an environment for following steps in the same process.

A required environment that is missing fails the step instead of falling
back to localhost.
A step cannot combine a named environment with direct application or
authentication URIs.

## Options

```bash
--file-name <VALUE>
Scenario file name. Required.
```

## Environment Options

```bash
-e, --Environment <VALUE>
Default environment name for steps that omit a target
```

## Examples

```bash
Run a provisioning scenario that creates its first environment:
clio run --file-name ./Phase1.yaml

Supply a default environment for steps that omit a target:
clio run --file-name ./Phase1.yaml -e dev
```

## Notes

Only --environment is inherited as a scenario-level step default. Put direct
URIs, credentials, and other environment options on the individual step.

Scenarios run non-interactively. A step targeting an environment marked Safe
fails closed because the runner cannot request production confirmation.

## See Also

create-workspace - Create a clio workspace
list-environments - List registered environments

- [Clio Command Reference](../../Commands.md#run)
