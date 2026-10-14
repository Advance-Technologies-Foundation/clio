using System.Collections.Generic;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Shared end-to-end assertions for the object-rights MCP tools. Mutating/reading real object permissions
/// needs a live Creatio environment, so the hermetic CI-safe checks are that the real clio MCP server
/// advertises the tool with the right destructive classification on the lazy surface, and binds its args
/// wrapper to a structured failure against a missing environment (no live rights access occurs). Concrete
/// fixtures supply the tool name, its expected destructive flag, and a valid args payload.
/// </summary>
public abstract class ObjectRightsToolE2ETestsBase : McpContractFixtureBase {

	protected abstract string ToolName { get; }

	protected abstract bool ExpectedDestructive { get; }

	/// <summary>A payload that binds all required args, pointing at the given (unknown) environment.</summary>
	protected abstract Dictionary<string, object?> InvalidEnvironmentArgs(string environmentName);

	// The shared tests below run once per concrete fixture, so the tool-under-test tag cannot be a compile-time
	// [AllureTag] on the base method; it is added at runtime from the fixture's ToolName instead.
	[SetUp]
	public void TagToolUnderTest() => AllureApi.AddTags(ToolName);

	[Test]
	[AllureName("object-rights tool is discoverable with the expected destructive flag")]
	[AllureDescription("The tool appears in the get-tool-contract compact index of the lazy MCP surface, flagged destructive for set-object-rights and read-only for get-object-rights.")]
	[Description("Exposes the object-rights tool via the get-tool-contract compact index on the lazy MCP surface with the expected destructive classification.")]
	public async Task Tool_Should_Be_Discoverable_On_Lazy_Surface_With_Expected_Destructive_Flag() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(entry => entry.Name == ToolName,
				because: $"the compact discovery index must carry exactly one entry for {ToolName}")
			.Which;
		if (ExpectedDestructive) {
			entry.Destructive.Should().Be(true,
				because: $"{ToolName} changes object access rights and must be flagged destructive in the discovery index");
		} else {
			entry.Destructive.Should().NotBe(true,
				because: $"{ToolName} is read-only and must not be flagged destructive in the discovery index");
		}
	}

	[Test]
	[AllureName("object-rights tool binds its args and reports an unknown environment")]
	[AllureDescription("A valid payload binds through the real MCP server and fails on the missing environment before any rights access.")]
	[Description("Binds the object-rights tool arguments through the real MCP server and returns a structured failure for an unknown environment before any rights access.")]
	public async Task Tool_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-{ToolName}-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = InvalidEnvironmentArgs(invalidEnvironmentName) },
			arrangeContext.CancellationTokenSource.Token);
		ObjectRightsToolResponse response = EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid object-rights payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution before any rights access");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}

	/// <summary>A near-miss argument name the tool must refuse (for example a typo of a real argument).</summary>
	protected abstract string UnknownArgumentName { get; }

	[Test]
	[AllureName("object-rights tool refuses a misspelled argument before any work")]
	[AllureDescription("A near-miss argument name is refused through the real MCP transport before the environment is resolved, so a typo can never be dropped silently.")]
	[Description("Refuses a misspelled argument through the real MCP transport and long-tail dispatch BEFORE environment resolution, so a typo such as 'revok' can never be dropped silently and turn a revoke into a grant.")]
	public async Task Tool_Should_Refuse_Unknown_Argument_Before_Environment_Resolution() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-{ToolName}-unknown-arg-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> args = InvalidEnvironmentArgs(invalidEnvironmentName);
		args[UnknownArgumentName] = true;

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		ObjectRightsToolResponse response = EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		response.Success.Should().BeFalse(because: "an unknown argument must be refused, not ignored");
		response.Error.Should().Contain(UnknownArgumentName,
			because: "the refusal must name the argument that was not recognised");
		response.Error.Should().NotContain(invalidEnvironmentName,
			because: "the refusal must happen before the environment is resolved, so nothing downstream ever runs");
	}

	[Test]
	[AllureName("object-rights tool returns a rename hint for a camelCase alias")]
	[AllureDescription("A camelCase environmentName is rejected with the exact environment-name rename hint.")]
	[Description("Rejects a camelCase alias of environment-name through the real MCP server with the exact rename hint.")]
	public async Task Tool_Should_Return_RenameHint_When_CamelCase_Alias_Is_Passed() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		Dictionary<string, object?> args = InvalidEnvironmentArgs("unused");
		args.Remove("environment-name");
		args["environmentName"] = $"missing-{ToolName}-alias-env";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		ObjectRightsToolResponse response = EntitySchemaStructuredResultParser.Extract<ObjectRightsToolResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		response.Success.Should().BeFalse(because: "a camelCase alias must be rejected, not silently dropped");
		response.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
	}
}
