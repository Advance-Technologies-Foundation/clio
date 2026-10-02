using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the create-package MCP tool that need no Creatio environment: the tool is reachable
/// on the lazy surface and its args wrapper binds to a structured validation error.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("create-package")]
[Parallelizable(ParallelScope.Self)]
public sealed class CreatePackageToolE2ETests : McpContractFixtureBase {

	private const string ToolName = CreatePackageTool.CreatePackageToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("create-package is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies create-package is discoverable via the get-tool-contract compact index.")]
	public async Task CreatePackage_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "create-package must be reachable on the lazy surface even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("create-package binds the args wrapper and returns a structured validation failure")]
	[Description("Calls create-package through the real MCP server with only environment-name and verifies the structured failure names package-name and reports that nothing was created.")]
	public async Task CreatePackage_Should_Return_Structured_Validation_Failure_When_Name_Is_Missing() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult callResult = await context.Session.CallDestructiveAsync(ToolName,
			new Dictionary<string, object?> { ["environment-name"] = "any-environment" },
			context.CancellationTokenSource.Token);
		CreatePackageResponse result = EntitySchemaStructuredResultParser.Extract<CreatePackageResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(because: "a package cannot be created without a name");
		result.PackageCreated.Should().BeFalse(because: "nothing was sent to the environment");
		result.Error.Should().Contain("package-name", because: "the failure names the field the caller has to add");
	}
}
