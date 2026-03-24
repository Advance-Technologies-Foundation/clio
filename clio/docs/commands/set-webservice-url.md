# set-webservice-url

Set a base URL for a configured web service.

## Synopsis

```bash
clio set-webservice-url <SERVICE_NAME> <URL> [OPTIONS]
```

## Description

Stores or updates the base URL associated with a named web service in the target environment.

## Examples

```bash
clio set-webservice-url CustomerApi https://api.example.com -e dev
```

## See also

- [get-webservice-url](./get-webservice-url.md)
