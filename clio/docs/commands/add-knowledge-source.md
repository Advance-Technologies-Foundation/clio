# add-knowledge-source

## Command Type

    Integrations & tools

## Name

add-knowledge-source - Add a trusted Git or NuGet knowledge source

## Synopsis

```bash
clio add-knowledge-source --alias <alias> --library-id <id> --type <git|nuget> --location <url>
    --trusted-key-id <id> --trusted-public-key-path <absolute-path>
    [--package-id <id>] [--branch <name>] [--tag <name>] [--commit <sha>]
    [--artifact-path <path>]
    [--priority <number>] [--participation <mode>] [--disabled] [--json]
```

## Description

Validates and atomically persists one trusted source under `knowledge.sources` in Clio's visible
`appsettings.json`. The alias uses lowercase letters, digits, dots, and hyphens. `--library-id` is
the lowercase stable reverse-DNS publisher identity and must be unique across configured sources.

For NuGet, `--package-id` is required and `--location` is the v3 service-index URL. For Git, an
explicit complete 40- or 64-character commit ID wins over tag and branch, a tag resolves to a
commit, and a branch follows its resolved commit on install/update. With no Git reference, a
successful install or update discovers and persists the remote default branch; read-only
`info-knowledge` checks do not mutate configuration. Repository code, hooks, and submodules are
never executed.

Every source requires its own `--trusted-key-id` and `--trusted-public-key-path`. The path must be
an existing local regular file of at most 16 KiB containing exactly one P-256 `PUBLIC KEY` PEM.
Clio refuses UNC and device paths, network drives, symlinks, junctions, and other reparse points
anywhere in the existing path ancestry. Public keys are not secrets, but private-key PEM is
rejected and must never be supplied or stored in Clio settings.

The MCP `add-knowledge-source` operation also requires `confirmed: true` because adding a publisher
signing key expands the local trust boundary. Both MCP add and remove operations are classified as
destructive so the host confirmation gate applies.

Sources are enabled by default. `--disabled` persists configuration without making the source
eligible for serving or bulk lifecycle operations. Credentials must not be embedded in the
location. This proof of concept supports credential-free public HTTPS Git repositories and NuGet
feeds only; private-source and credential-manager authentication are not supported yet.

Configured sources must publish signed version 1 knowledge bundles. The earlier version 0
single-source prototype is not registered as an implicit compatibility library; reinstall its
content through an explicitly configured version 1 source.

## Options

```bash
--alias <alias>             Required unique lowercase source alias
--library-id <id>           Required unique lowercase reverse-DNS library identity
--type <git|nuget>          Required transport type
--location <url>            Required public HTTPS repository or NuGet service-index URL
--trusted-key-id <id>       Required bundle signing-key ID authorized for this source
--trusted-public-key-path <absolute-path>
                            Existing bounded local P-256 PUBLIC KEY PEM; no reparse/network path
--package-id <id>           Required for NuGet sources
--branch <name>             Git branch to follow
--tag <name>                Git tag to resolve
--commit <sha>              Complete 40/64-hex immutable Git commit; takes precedence
--artifact-path <path>      Ready Git bundle path; default: knowledge-bundle.zip
--priority <number>         Resolution priority; default: 0
--participation <mode>      isolated, supplement, or authoritative; default: supplement
--disabled                  Add the source disabled
--json                      Emit indented JSON
```

## Examples

```bash
clio add-knowledge-source --alias creatio --library-id com.creatio.clio --type git \
  --location https://github.com/Advance-Technologies-Foundation/clio-knowledge.git \
  --trusted-key-id creatio-2026 \
  --trusted-public-key-path /etc/clio/keys/creatio-public.pem \
  --branch master --priority 100 --participation authoritative

clio add-knowledge-source --alias partner --library-id com.example.partner --type nuget \
  --location https://packages.example.test/v3/index.json \
  --trusted-key-id partner-2026 \
  --trusted-public-key-path /etc/clio/keys/partner-public.pem \
  --package-id Example.Partner.Knowledge --priority 50 --disabled --json
```

## Exit Codes

    0   Source configuration was added
    1   Validation, uniqueness, or atomic persistence failed

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#add-knowledge-source)
