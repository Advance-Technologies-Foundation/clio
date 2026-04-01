# set-webservice-url

## Name

set-webservice-url - Set base url for web service

## Description

Stores or updates the base URL assigned to a configured web service.

## Synopsis

```bash
clio set-webservice-url <SERVICE_NAME> <URL> [OPTIONS]
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

## Examples

```bash
clio set-webservice-url CustomerApi https://api.example.com -e dev
Set a service URL in the dev environment
```

## See Also

get-webservice-url - Read configured service URLs

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-webservice-url)
