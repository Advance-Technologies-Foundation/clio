# clio.mcp.e2e

This project is an end-to-end test suite for Clio MCP server. 
It uses MCP Client built with [`ModelContextProtocol`][Official MCP C# SDK documentation] library to test clio mcp-server.

Some tools require Creatio to be available during tests.
Creatio connection is configured in `appsettings.json` file, or via environment variables for CI/CD

This project uses [Allure NUnit] framework to generate test reports for improved human readability.


## Test structure

- All tests follow an AAA (Arrange, Act, Assert) pattern and are organized in test classes based on the feature being tested.
- Reusable steps are defined in `Steps` folder.
- Reusable assertions are defined in `Assertions` folder.
- Assertions must be written with `FluentAssertions`, and provide a clear and concise error message.
- This project defines a DI container from which reusable services are injected.
- Passing of arguments between AAA steps with clearly define attributes for improved readability and maintainability.




[//]: # (## Docs and references)
[Official MCP C# SDK documentation]: https://devblogs.microsoft.com/dotnet/release-v10-of-the-official-mcp-csharp-sdk/
[Demo repository]: https://github.com/mikekistler/mcp-whats-new
[Allure NUnit]: https://allurereport.org/docs/nunit/
