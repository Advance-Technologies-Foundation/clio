using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-103025: an empty create-app / create-business-process call is answered with the required arguments and a
/// minimal valid call, over the real stdio server. The refusal happens before any environment is resolved.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ApplicationCreateTool.ApplicationCreateToolName)]
[NonParallelizable]
public sealed class ToolMiniContractE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(ApplicationCreateTool.ApplicationCreateToolName)]
	[AllureName("create-app answers an empty call with its mini-contract")]
	[AllureDescription("Calls create-app with no name or code through the real MCP server and verifies the refusal lists the required arguments and a minimal valid args object.")]
	[Description("create-app with {} returns the missing arguments, the required list and a minimal example without touching an environment.")]
	public async Task CreateApp_Should_Return_MiniContract_For_Empty_Call() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult result = await arrange.Session.CallDestructiveAsync(
			ApplicationCreateTool.ApplicationCreateToolName,
			new Dictionary<string, object?>(),
			arrange.CancellationTokenSource.Token);
		string text = Text(result);

		// Assert
		text.Should().Contain("\"success\":false", because: "an empty create-app is refused, not executed");
		text.Should().Contain("missing required arguments 'name', 'code'",
			because: "the refusal names exactly what the call lacked");
		text.Should().Contain("Minimal valid arguments:",
			because: "the caller gets a working call to copy instead of a second probe");
		text.Should().Contain("\\\"code\\\":\\\"TodoList\\\"",
			because: "the example carries the required code argument");
	}

	[Test]
	[AllureTag("create-business-process")]
	[AllureName("create-business-process answers an empty call with its mini-contract")]
	[AllureDescription("Calls create-business-process with no arguments and verifies the existing refusal sentence is kept and the mini-contract follows it.")]
	[Description("create-business-process with {} keeps its 'is required' refusal and appends the required arguments and a minimal descriptor example.")]
	public async Task CreateBusinessProcess_Should_Return_MiniContract_For_Empty_Call() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult result = await arrange.Session.CallDestructiveAsync(
			"create-business-process",
			new Dictionary<string, object?>(),
			arrange.CancellationTokenSource.Token);
		string text = Text(result);

		// Assert
		text.Should().Contain("environment-name is required and cannot be empty.",
			because: "the original refusal text is kept; the contract is appended");
		text.Should().Contain("missing required arguments 'environment-name', 'descriptor'",
			because: "the contract names what was missing");
		text.Should().Contain("Minimal valid arguments:",
			because: "the caller gets a working call to copy");
		text.Length.Should().BeLessThan(3000,
			because: "the mini-contract is bounded so a probe stays cheap");
	}

	private static string Text(CallToolResult result) =>
		string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
