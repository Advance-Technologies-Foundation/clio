# add-knowledge-source

## Command Type

    Integrations & tools

## Name

add-knowledge-source - Add a trusted GitHub Release, Git, or NuGet knowledge source

## Synopsis

```bash
clio add-knowledge-source --alias <alias> --library-id <id>
    --type <github-release|git|nuget> --location <url>
    [--repository-owner <owner>] [--repository-name <name>] [--asset-name <file.zip>]
    [--package-id <id>] [--branch <name>] [--tag <name>] [--commit <sha>]
    [--trusted-key-id <id>] [--trusted-public-key-path <absolute-path>]
    [--priority <number>] [--participation <mode>] [--disabled] [--json]
```

## Description

Validates and atomically persists one trusted source under `knowledge.sources` in Clio's visible
`appsettings.json`. The alias uses lowercase letters, digits, dots, and hyphens. `--library-id` is
the lowercase stable reverse-DNS publisher identity and must be unique across configured sources.

For `github-release`, `--location` is the GitHub REST API origin (`https://api.github.com/`) and the
repository is addressed structurally through `--repository-owner`, `--repository-name`, and
`--asset-name` rather than through an arbitrary URL. Clio reads the repository's latest stable
release, requires exactly one uploaded asset with that name, verifies the SHA-256 digest GitHub
publishes for it, and then verifies the publisher signature inside the bundle. **No Git CLI is
involved and no branch is read.** Signing trust is optional for this transport: omit both
`--trusted-key-id` and `--trusted-public-key-path` to rely on the public key Clio pins in its own
binary for the built-in `com.creatio.clio` library, or supply both to trust a third-party publisher.
A release tag must equal the library version its signed bundle declares; Clio refuses a mismatch.

For NuGet, `--package-id` is required and `--location` is the v3 service-index URL. For Git, an
explicit complete 40- or 64-character commit ID wins over tag and branch, a tag resolves to a
commit, and a branch follows its resolved commit on install/update. Git sources are cloned directly
into Clio's visible knowledge root and updated with a fast-forward-only pull, so **this transport
requires a working Git CLI on the machine**. With no Git reference, a
successful install or update discovers and persists the remote default branch; read-only
`info-knowledge` checks do not mutate configuration. Repository code, hooks, and submodules are
never executed.

NuGet sources require their own `--trusted-key-id` and `--trusted-public-key-path`, GitHub Release
sources may supply them, and Git sources must not. When supplied, both are required together. The
pinned built-in trust always wins for `com.creatio.clio`: a configured entry naming that library
cannot substitute its own signing key. The path must be
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

GitHub Release and NuGet sources must publish signed version 1 knowledge bundles. Git sources expose
their source manifest and content files directly in the repository checkout.

## Options

```bash
--alias <alias>             Required unique lowercase source alias
--library-id <id>           Required unique lowercase reverse-DNS library identity
--type <github-release|git|nuget>
                            Required transport type
--location <url>            Required public HTTPS location: the GitHub REST API origin,
                            a Git repository URL, or a NuGet v3 service-index URL
--repository-owner <owner>  Required for github-release
--repository-name <name>    Required for github-release
--asset-name <file.zip>     Required for github-release; matched exactly and exactly once
--trusted-key-id <id>       Required for NuGet; optional for github-release
--trusted-public-key-path <absolute-path>
                            Required for NuGet; optional for github-release;
                            bounded local P-256 PUBLIC KEY PEM
--package-id <id>           Required for NuGet sources
--branch <name>             Git branch to follow
--tag <name>                Git tag to resolve
--commit <sha>              Complete 40/64-hex immutable Git commit; takes precedence
--priority <number>         Resolution priority; default: 0
--participation <mode>      isolated, supplement, or authoritative; default: supplement
--disabled                  Add the source disabled
--json                      Emit indented JSON
```

## Examples

```bash
clio add-knowledge-source --alias partner-release --library-id com.example.partner --type github-release \
  --location https://api.github.com/ \
  --repository-owner example --repository-name partner-knowledge \
  --asset-name partner-knowledge-bundle.zip \
  --trusted-key-id partner-2026 \
  --trusted-public-key-path /etc/clio/keys/partner-public.pem \
  --priority 50

clio add-knowledge-source --alias creatio --library-id com.creatio.clio --type git \
  --location https://github.com/Advance-Technologies-Foundation/clio-knowledge.git \
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
