# call-service

## Command Type

    Development commands

## Name

call-service - Call a Creatio service endpoint

## Synopsis

```bash
clio call-service [OPTIONS]
clio call-service [OPTIONS]
```

## Description

Sends a request to any Creatio service route and prints the response, or
writes it to --destination.

--service-path is relative to the Creatio application root. Use
odata/BulkEmailCategory; clio also accepts the equivalent /odata/...,
0/odata/... and /0/odata/... forms and normalizes the optional 0/
application alias away, including a repeated 0/0/ prefix. Passing the alias
twice used to produce a double-rooted URL on .NET Framework environments.

A response that is not a successful payload is never saved. clio exits with
a non-zero code and reports the reason instead when the body is a Creatio
error envelope ({"Code":-1,"Exception":...}), an OData v4 error
({"error":{"message":...}}), an ASP.NET exception or routing error
({"Message":...,"MessageDetail":...}), an authentication rejection
({"Code":1,...}), or a server error page - including one that starts with a
byte-order mark or an XML declaration before the doctype, which is the shape
Creatio behind IIS returns for "Request Error"/"Service Unavailable".

## Options

```bash
-m, --method           HTTP method. POST when omitted
-f, --input            File to read the request body from
-b, --body             Request body JSON
-d, --destination      File to write the response to
--service-path         Route service path, relative to the application root
-v, --variables        Values substituted into {{placeholders}} of the body
```

## Examples

```bash
# Read an OData collection
clio call-service -e dev --method GET --service-path odata/BulkEmailCategory

# The same route with the optional application-root alias
clio call-service -e dev --method GET --service-path /0/odata/BulkEmailCategory

# Post a request body and save the response
clio call-service -e dev --method POST --service-path ServiceModel/EntityDataService.svc --input request.json --destination result.json
```

## Notes

- --service-path is normalized: leading /, 0/ and /0/ layers are stripped before the URL is built
- A Creatio error envelope, an OData or ASP.NET error body, an authentication rejection or a server error page makes the command exit non-zero without writing --destination
- Markup detection ignores a byte-order mark and an XML declaration, so an IIS "Request Error" page is not mistaken for a payload
- The response body is parsed once: the same document is used to classify the response and to indent what is printed or saved

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

dataservice

- [Clio Command Reference](../../Commands.md#call-service)
