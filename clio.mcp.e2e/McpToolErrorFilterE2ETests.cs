using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for MCP tool argument error handling.
/// Verifies that the <see cref="Clio.Command.McpServer.McpToolErrorFilter"/> intercepts
/// flat arguments (sent without the composite wrapper) and returns actionable diagnostics.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("error-filter")]
[Parallelizable(ParallelScope.Self)]
public sealed class McpToolErrorFilterE2ETests : McpContractFixtureBase
{
	[Test]
	[AllureTag("list-apps")]
	[AllureName("Flat arguments produce a helpful wrapper hint instead of an opaque SDK error")]
	[Description("Sends flat arguments (environment-name at top level) to a composite-args tool and verifies the error filter returns a structured hint showing the correct wrapping format.")]
	public async Task FlatArgs_ShouldReturnWrapperHint_WhenCompositeParameterIsMissing()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = ApplicationGetListTool.ApplicationGetListToolName;

		// Act — send flat args WITHOUT the "args" wrapper
		CallToolResult callResult;
		try {
			callResult = await arrangeContext.Session.CallToolAsync(
				toolName,
				new Dictionary<string, object?> {
					["environment-name"] = "some-env"
				},
				arrangeContext.CancellationTokenSource.Token);
		} catch (Exception ex) {
			Assert.Fail(
				$"The MCP SDK threw a protocol-level exception instead of returning a tool result. "
				+ $"This means the filter does not intercept missing composite parameters. "
				+ $"Exception: {ex.GetType().Name}: {ex.Message}");
			return;
		}

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "flat arguments should be detected as a caller error");

		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		responseText.Should().Contain("args",
			because: "the error should name the wrapper parameter the caller should use");

		responseText.Should().Contain("environment-name",
			because: "the error should show the flat key that was incorrectly placed at the top level");
	}

	[Test]
	[AllureTag("get-guidance")]
	[AllureName("Wrapper hint advertises only wire-contract properties, not [JsonExtensionData] buckets")]
	[Description("Sends a flat get-guidance call and verifies the hint's correct-format example lists real arguments only, excluding the [JsonExtensionData] overflow property.")]
	public async Task FlatArgs_ShouldExcludeExtensionDataFromHint_WhenArgsTypeHasNonContractProperties()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = GuidanceGetTool.ToolName;

		// Act — send flat args WITHOUT the "args" wrapper
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["name"] = "routing"
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "flat arguments should be detected as a caller error");

		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		responseText.Should().Contain("\"name\"",
			because: "the hint should advertise the real wire-contract argument");

		responseText.Should().NotContain("ExtensionData",
			because: "the [JsonExtensionData] overflow bucket is not a real argument and must not be advertised");
	}

	[Test]
	[AllureTag("list-apps")]
	[AllureName("Correctly wrapped arguments are not intercepted by the flat-args filter")]
	[Description("Sends correctly wrapped arguments to a composite-args tool and verifies the call proceeds past the error filter to normal execution.")]
	public async Task WrappedArgs_ShouldNotTriggerHint_WhenCompositeParameterIsPresent()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = ApplicationGetListTool.ApplicationGetListToolName;

		// Act — send correctly wrapped args
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-env-{Guid.NewGuid():N}"
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert — should reach past the filter to actual execution (which fails on missing env, not on wrapping)
		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		responseText.Should().NotContain("expects arguments wrapped inside",
			because: "correctly wrapped arguments should pass through the filter without a wrapper hint");
	}
}
