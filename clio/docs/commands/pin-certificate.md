# pin-certificate

## Command Type

System configuration

## Name

`pin-certificate` - Select the preferred local IIS certificate for HTTPS deployments.

## Synopsis

```shell
clio pin-certificate
clio pin-certificate --thumbprint <sha1-thumbprint>
clio pin-certificate --clear
```

## Description

`pin-certificate` stores the preferred IIS certificate thumbprint in clio's
`appsettings.json`. With no thumbprint, it lists eligible certificates and lets
you select one interactively.

Eligible certificates come from `LocalMachine/My`, match the machine FQDN by
SAN or common name, are currently valid, contain a private key, and support
TLS server authentication. A pin resolves ambiguity when more than one
certificate is eligible. Without a pin, clio deterministically prefers the
certificate with the latest expiration.

## Options

- `--thumbprint <value>`: Persist an eligible installed SHA-1 thumbprint. Spaces
  and separators are ignored.
- `--clear`: Remove the persisted preference.

## Appsettings schema

The optional root property is:

```json
{
  "iis-certificate-thumbprint": "DFC3141FAA198BA485538E2406CF52D90E812709"
}
```

The generated `schema.json` defines this value as exactly 40
uppercase hexadecimal characters. clio refreshes an existing schema file from
its bundled template when settings are loaded, so upgrades receive this field
without requiring a new settings file.

## Deployment behavior

`deploy-creatio --use-https` treats HTTPS as a preference for local IIS:

1. Use the pinned certificate when it remains eligible.
2. Otherwise, warn about a stale pin and choose the deterministic best match.
3. If no usable certificate exists, warn and complete the deployment over HTTP.

The resulting IIS site has one binding only: HTTP or HTTPS. For .NET Framework
deployments, clio also switches the root ServiceModel behavior and binding
configuration sources to their HTTPS variants and enables encrypted Microsoft
WebSocket connections. .NET 8 deployments need no additional web.config edits.

## Examples

```shell
clio pin-certificate
clio pin-certificate --thumbprint DFC3141FAA198BA485538E2406CF52D90E812709
clio deploy-creatio --site-name secure-local --site-port 40087 --use-https --zip-file C:\builds\creatio.zip
clio pin-certificate --clear
```

- [Clio Command Reference](../../Commands.md#pin-certificate)
