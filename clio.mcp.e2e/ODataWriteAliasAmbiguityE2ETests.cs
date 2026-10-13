using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-103020: an odata-create / odata-update call that names its payload twice is refused as ambiguous
/// before any environment is touched, while a lone synonym is accepted and warned about.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ODataCreateTool.ToolName)]
[NonParallelizable]
public sealed class ODataWriteAliasAmbiguityE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(ODataCreateTool.ToolName)]
	[AllureName("odata-create refuses rows together with payload")]
	[AllureDescription("Sends rows and payload together through the real MCP server and verifies the ambiguity refusal precedes environment resolution.")]
	[Description("odata-create with rows and a payload synonym fails as ambiguous without reaching the environment.")]
	public async Task ODataCreate_Should_Refuse_Rows_With_Payload_Alias() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		string missingEnvironment = $"missing-alias-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ODataCreateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = missingEnvironment,
					["entity"] = "Contact",
					["rows"] = new object[] { new Dictionary<string, object?> { ["Name"] = "a" } },
					["payload"] = new Dictionary<string, object?> { ["Name"] = "b" }
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataCreateBatchResponse response = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "an ambiguous payload is a structured refusal");
		response.Error.Should().Contain("Ambiguous payload",
			because: "two names for the payload must not be silently resolved");
		response.Error.Should().NotContain(missingEnvironment,
			because: "the refusal comes before environment resolution");
		response.Warnings.Should().BeNullOrEmpty(because: "a refused call accepted no synonym");
	}

	[Test]
	[AllureTag(ODataUpdateTool.ToolName)]
	[AllureName("odata-update refuses data together with values")]
	[AllureDescription("Sends data and values together through the real MCP server and verifies the ambiguity refusal.")]
	[Description("odata-update with data and a values synonym fails as ambiguous, and a lone values synonym is accepted with a warning before the environment is resolved.")]
	public async Task ODataUpdate_Should_Refuse_Data_With_Values_Alias_And_Warn_On_A_Lone_Alias() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));
		string missingEnvironment = $"missing-alias-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> BaseArgs() => new() {
			["environment-name"] = missingEnvironment,
			["entity"] = "Contact",
			["id"] = "8ecab4a1-0ca3-4515-9399-efe0a19390bd",
			["confirm"] = true
		};
		Dictionary<string, object?> ambiguous = BaseArgs();
		ambiguous["data"] = new Dictionary<string, object?> { ["Name"] = "a" };
		ambiguous["values"] = new Dictionary<string, object?> { ["Name"] = "b" };
		Dictionary<string, object?> lone = BaseArgs();
		lone["values"] = new Dictionary<string, object?> { ["Name"] = "b" };

		// Act
		ODataWriteResponse refused = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(
			await arrange.Session.CallToolAsync(ODataUpdateTool.ToolName,
				new Dictionary<string, object?> { ["args"] = ambiguous }, arrange.CancellationTokenSource.Token));
		ODataWriteResponse accepted = EntitySchemaStructuredResultParser.Extract<ODataWriteResponse>(
			await arrange.Session.CallToolAsync(ODataUpdateTool.ToolName,
				new Dictionary<string, object?> { ["args"] = lone }, arrange.CancellationTokenSource.Token));

		// Assert
		refused.Success.Should().BeFalse(because: "data and values name the same payload");
		refused.Error.Should().Contain("Ambiguous payload", because: "the refusal names the problem");
		accepted.Warnings.Should().ContainSingle(warning => warning.Contains("'values'") && warning.Contains("'data'"),
			because: "a lone synonym is accepted and the canonical name is reported");
		accepted.Error.Should().Contain(missingEnvironment,
			because: "an accepted synonym proceeds to environment resolution, which fails for the unregistered name");
	}
}
