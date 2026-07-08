using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the register-related-page MCP tool. NoEnvironment tier: discovery and the
/// invalid-environment failure contract are exercised against the real server process; the write happy
/// path is destructive and belongs to the sandbox tier with explicit opt-in.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(RelatedPageTool.ToolName)]
[NonParallelizable]
public sealed class RelatedPageToolE2ETests : McpContractFixtureBase {

	private const string ToolName = RelatedPageTool.ToolName;

	// register-related-page is a universal tool (registers web + mobile related pages) and is NOT
	// feature-gated, so it is resident on the default shared server — no isolated CLIO_HOME needed.

	[Test]
	[Description("Advertises register-related-page so MCP callers can discover the related-page registration tool.")]
	[AllureTag(ToolName)]
	[AllureName("register-related-page tool is discoverable")]
	[AllureDescription("Starts the real clio MCP server and verifies register-related-page is reachable on the MCP tool surface.")]
	public async Task RelatedPageTool_Should_Be_Discoverable() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "register-related-page must be advertised so MCP callers can discover the related-page registration tool");
	}

	[Test]
	[Description("Fails with human-readable diagnostics and no sandbox mutation when the target environment is not registered.")]
	[AllureTag(ToolName)]
	[AllureName("register-related-page reports invalid environment failures")]
	[AllureDescription("Calls register-related-page with an unregistered environment through the real MCP server and verifies the command reports a readable error and a non-success exit code.")]
	public async Task RelatedPageTool_Should_Report_Failure_For_Invalid_Environment() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-related-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPackage",
					["entity-schema-name"] = "UsrEntity",
					["page-schema-name"] = "UsrEntity_MobileFormPage",
					["schema-type"] = "mobile",
					["is-default"] = true
				}
			},
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		(callResult.IsError == true || execution.ExitCode != 0).Should().BeTrue(
			because: "register-related-page must fail when the requested environment is not registered");
		execution.Output.Should().NotBeNullOrEmpty(
			because: "a failed registration should emit human-readable diagnostics");
		execution.Output!.Should().Contain(
			message => message.MessageType == Clio.Common.LogDecoratorType.Error,
			because: "a failed register-related-page execution should report its diagnostics as error-level log output");
		string combinedOutput = string.Join(
			Environment.NewLine,
			(execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure diagnostics should identify that the requested environment is not registered");
	}
}
