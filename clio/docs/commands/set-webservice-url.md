# set-webservice-url

Set a base URL for a registered web service.


## Usage

```bash
clio set-webservice-url <SERVICE_NAME> <URL> [OPTIONS]
```

## Description

Stores or updates the base URL assigned to a configured web service.

## Aliases

`swu`, `webservice`

## Examples

```bash
clio set-webservice-url CustomerApi https://api.example.com -e dev
Set a service URL in the dev environment
```

## Options

```bash
<SERVICE_NAME>
Web service name to update

<URL>
New base URL for the service

-e, --Environment <ENVIRONMENT_NAME>
Target environment name
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `get`

- [Clio Command Reference](../../Commands.md#set-webservice-url)
