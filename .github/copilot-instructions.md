## How to interact with a user:
- Answer all questions in the style of a friendly colleague, using informal language
- Use @terminal when answering questions about Git.


## When generating code always follow these guidelines:
For commands that require cliogate to be installed, always include a check for its presence and display a message to the user if it is not installed. Follow the pattern used in existing commands.
Use Microsoft coding style for all code unless otherwise specified.

All filesystem operations must use the filesystem abstraction already used in the project. This ensures unit tests can be written without depending on the real filesystem.

Use Microsoft coding style for all code unless otherwise specified.


Every time you change code, always check the documentation in README.md and Commands.md. Update, add, or remove documentation as needed to keep it complete and up-to-date.

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


## Project structure
- Main clio console application [clio](./../clio/clio.csproj)
- [Clio.Tests](./../Clio.Tests/Clio.Tests.csproj) This project is a unit test project for the clio console application.
- [cliogate](./../../clio/cliogate/cliogate.csproj) is an extension that is installed on Creatio. This project extends Creatio's capabilities.
- [cliogate.tests](./../../clio/cliogate.tests/cliogate.tests.csproj) is a unit test project for the cliogate extension.



## CI/CD
- The CI/CD pipeline is configured in the `.github/workflows` directory.
- Repository has runners that run on windows in the corporate infrastructure.
- The CI/CD pipeline builds the solution, runs unit tests, and publishes the `clio