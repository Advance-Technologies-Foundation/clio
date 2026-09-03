using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the generate-source-code MCP tool. Actually regenerating schema sources needs a
/// live Creatio environment, so the hermetic CI-safe assertions target the two argument-shape guards added
/// for issue #1303 — both run before any environment resolution, so they are provable against the real clio
/// MCP server with no stand: a non-positive <c>timeout</c> is refused, and a field that cannot bind is
/// reported instead of being dropped by System.Text.Json.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("generate-source-code")]
[NonParallelizable]
public sealed class GenerateSourceCodeToolE2ETests : McpContractFixtureBase {

	private const string ToolName = GenerateSourceCodeTool.GenerateSourceCodeToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("generate-source-code tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies generate-source-code is reachable via the get-tool-contract compact index.")]
	[Description("Starts the real clio MCP server and verifies generate-source-code is discoverable on the lazy tool surface so an MCP client can invoke it even though it is not resident in tools/list.")]
	public async Task GenerateSourceCode_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "the generate-source-code MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("generate-source-code advertises the timeout argument in its contract")]
	[AllureDescription("Expands the full generate-source-code contract through get-tool-contract and verifies the new timeout argument is part of the advertised schema.")]
	[Description("The timeout argument added in issue #1303 must appear in the advertised contract, otherwise a caller cannot discover that a long generation can be bounded from MCP.")]
	public async Task GenerateSourceCode_Should_Advertise_Timeout_Argument() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult contractResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { ToolName }
				}
			},
			context.CancellationTokenSource.Token);
		string contractJson = JsonSerializer.Serialize(contractResult);

		// Assert
		contractResult.IsError.Should().NotBeTrue(
			because: "get-tool-contract must expand the generate-source-code contract without a protocol error");
		contractJson.Should().Contain("timeout",
			because: "the timeout argument must be part of the advertised input schema so a caller can bound a long generation");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("generate-source-code refuses a non-positive timeout")]
	[AllureDescription("Calls generate-source-code with timeout 0 and verifies the structured failure names the timeout field and states the valid millisecond range, before any environment work.")]
	[Description("A zero or negative timeout can never bound a request, so it must be refused as a caller-actionable validation error rather than silently overwriting the 60-minute default with an unusable value (issue #1303).")]
	public async Task GenerateSourceCode_Should_Refuse_NonPositive_Timeout() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-generate-source-code-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["timeout"] = 0
				}
			},
			context.CancellationTokenSource.Token);
		string callResultJson = JsonSerializer.Serialize(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an invalid argument value is a caller-actionable structured failure, not an MCP protocol error");
		callResultJson.Should().Contain("must be between 1 and",
			because: $"the failure must tell the caller what a valid timeout looks like. Payload: {callResultJson}");
		callResultJson.Should().Contain("timeout",
			because: "the failure must name the exact kebab-case field the caller has to correct");
		callResultJson.Should().NotContain(invalidEnvironmentName,
			because: "the timeout check must run before the environment is resolved, so the unregistered name never reaches a lookup");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("generate-source-code rejects an unbindable argument with a rename hint")]
	[AllureDescription("Calls generate-source-code with camelCase environmentName plus a genuinely unknown field and verifies both are reported — the alias as a rename hint, the other under Unknown args with the valid-field list.")]
	[Description("Before issue #1303 every field that failed to bind was dropped by System.Text.Json, so a camelCase environmentName ran generation against the default environment without a word. The tool must now refuse and name the rename.")]
	public async Task GenerateSourceCode_Should_Reject_Unbindable_Arguments() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-generate-source-code-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environmentName"] = invalidEnvironmentName,
					["totally-made-up-field"] = "whatever"
				}
			},
			context.CancellationTokenSource.Token);
		string callResultJson = JsonSerializer.Serialize(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an unbindable argument is a caller-actionable structured failure, not an MCP protocol error");
		callResultJson.Should().Contain("environment-name",
			because: $"the rename hint must name the canonical kebab-case field. Payload: {callResultJson}");
		callResultJson.Should().Contain("totally-made-up-field",
			because: "a field with no canonical counterpart must be quoted back so the caller sees exactly what was rejected");
		callResultJson.Should().Contain("Valid fields:",
			because: "the caller must be able to correct the call without re-reading the whole contract");
		callResultJson.Should().Contain("Nothing was generated",
			because: "the caller must know the rejected call had no server-side effect before retrying");
	}
}
