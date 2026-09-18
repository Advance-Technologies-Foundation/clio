using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>Checks actual sequence availability through the external server.</summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(SequenceContextTool.ToolName)]
[NonParallelizable]
public sealed class SequenceContextLiveE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Reads sequence context from an explicit sandbox and reports genuine schema availability.")]
	[AllureTag(SequenceContextTool.ToolName)]
	public async Task ReadContext_FromSandbox() {
		// Arrange
		var settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
		// Act
		var result = await Session.CallToolAsync(SequenceContextTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
				["environment-name"] = settings.Sandbox.EnvironmentName
			}}, timeout.Token);
		var context = EntitySchemaStructuredResultParser.Extract<SequenceContextResult>(result);
		// Assert
		result.IsError.Should().NotBeTrue(because: "the valid request must execute through the real MCP server");
		context.Availability.Should().BeOneOf(new[] { "present", "absent" },
			because: "the configured sandbox must allow the schema catalog read");
		if (context.Availability == "present") {
			context.Success.Should().BeTrue(because: "the supported sandbox must provide every context section");
			context.Sections["schema:Sequence"].State.Should().Be("complete",
				because: "an installed sequence schema must have readable effective metadata");
			var choices = (JsonElement)context.Sections["choices:SequenceStatus"].Data!;
			choices.GetArrayLength().Should().BeGreaterThan(0, because: "installed sequences need live status lookup values");
		} else {
			context.Success.Should().BeFalse(because: "absence is actionable rather than successful context");
		}
	}
}
