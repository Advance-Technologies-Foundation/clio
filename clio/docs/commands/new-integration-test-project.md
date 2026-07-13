# new-integration-test-project

Create a portable, scenario-neutral Creatio integration-test project.

## Usage

```bash
clio new-integration-test-project --package <NAME> [--target-framework <TFM>]
```

Alias: `integration-test`.

The project uses NUnit, FluentAssertions, ATF.Repository, and Allure. It contains only
configuration and a base fixture; process models, entity models, business assertions, and
Playwright are added when a scenario requires them.

## Options

- `--package` — required workspace package name.
- `--target-framework` — generated target framework; defaults to `net10.0`.

## Runtime configuration

NUnit parameters take precedence over environment variables. Supply `CREATIO_URL`,
`CREATIO_IS_NETCORE`, and exactly one authentication mode:

- `CREATIO_ACCESS_TOKEN`; or
- `CREATIO_USERNAME` and `CREATIO_PASSWORD`.

Keep credentials in CI secret storage. The generated project does not depend on clio's local
environment registry.

## Examples

```bash
clio new-integration-test-project --package UsrFinancialApplicatio
clio integration-test --package UsrFinancialApplicatio --target-framework net8.0
```

- [Clio Command Reference](../../Commands.md#new-integration-test-project)
