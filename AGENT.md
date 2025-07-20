## How to interact with a user:
- Answer all questions in the style of a friendly colleague, using informal language
- Use @terminal when answering questions about Git.


## When generating code always follow these guidelines:
- All methods should have XML documentation comments.
- For C# Coding Style Use code style as described in [C# Coding Style Guidelines](./.forllm/code-style.md).
- When adding or updating commands visible to a user, ensure to update relevant sections of the documentation files. All documentation files are located in [Commands](../clio/Commands.md) file.
- When changing the source code always try to add unit tests. Make sure that tests are placed in the `Clio.Tests` project. If you are adding a new command, add tests for it in the `Clio.Tests/Commands` folder.


## Unit tests
- Tests should be compatible with Windows, MacOS, and Linux.
- Command test in clio **MUST** inherit from BaseCommandTests<T> where T is the option for the command.
  This base test will check that the command has proper description in documentation.
  Make sure that command under test has a proper description in the documentation.
- Use NSubstitute for mocking dependencies.
- Use FluentAssertions for assertions.
- Always use Arrange-Act-Assert pattern in tests.
- Whenever possible, always use "because" argument for all assertions. For example:
  ```
  result.Should().Be(0, "explain why the result should be 0");
  ```

- Tests should be decorated with Description attribute, explaining what the test is doing.
 ```csharp
[Test]
[Description("Descrites the tests purpose")]
public void TestName()
 ```
## Additional guidelines
- [Documentation guidelines](./.forllm/documentation-guidelines.md) - how to create public documentation for clio commands.

## Project structure
- Main clio console application [clio](./../clio/clio.csproj)
- [Clio.Tests](./clio.tests/clio.tests.csproj) This project is a unit test project for the clio console application.
- [cliogate](./cliogate/cliogate.csproj) is an extension that is installed on Creatio. This project extends Creatio's capabilities.
- [cliogate.tests](./cliogate.tests/cliogate.tests.csproj) is a unit test project for the cliogate extension.

## CI/CD
- The CI/CD pipeline is configured in the `.github/workflows` directory.
- Repository has runners that run on windows in the corporate infrastructure.
- The CI/CD pipeline builds the solution, runs unit tests, and publishes the `clio