# new-integration-test-project

## Name

new-integration-test-project (alias: integration-test) - Create a portable Creatio integration-test project

## Synopsis

```bash
clio new-integration-test-project --package <NAME> [--target-framework <TFM>]
```

## Options

```bash
--package <NAME>
Required workspace package name.

--target-framework <TFM>
Generated project target framework. Default: net10.0.
```

## Examples

```bash
clio new-integration-test-project --package UsrFinancialApplicatio
clio new-integration-test-project --package UsrFinancialApplicatio --target-framework net8.0
```

## Notes

The generated project reads CREATIO_URL, CREATIO_IS_NETCORE, and either
CREATIO_ACCESS_TOKEN or CREATIO_USERNAME plus CREATIO_PASSWORD from NUnit parameters
or environment variables. It does not require a registered clio environment.
Registers the generated project in tests/IntegrationTests.slnx and MainSolution.slnx.
An existing project directory is refused to preserve customizations.
Solution-write errors return a nonzero exit code. Success emits an Info message.
MCP: clio-run command=new-integration-test-project with package-name, absolute
workspace-path, and optional target-framework. Write test cases in the generated project.

- [Clio Command Reference](../../Commands.md#new-integration-test-project)
