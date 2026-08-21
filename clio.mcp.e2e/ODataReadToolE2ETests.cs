using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the OData read MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ODataReadTool.ToolName)]
[NonParallelizable]
public sealed class ODataReadToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes odata-read via the get-tool-contract compact index with a non-destructive safety flag on the lazy tool surface.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read MCP tool is discoverable on the lazy surface")]
	public async Task ODataRead_Should_Be_Advertised() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		// The lazy surface exposes hidden tools only through the compact discovery index, which carries the
		// destructive flag; the read-only hint is no longer observable for non-resident tools.
		ToolContractIndexEntry entry = index.Should().ContainSingle(entry => entry.Name == ODataReadTool.ToolName,
			because: "odata-read must be discoverable via the get-tool-contract compact index so callers can find the query tool")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "odata-read is a read-only query tool and must not be flagged destructive");
	}

	[Test]
	[Description("Binds odata-read arguments through the real MCP server and returns a structured failure for an unknown environment.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read MCP tool binds arguments")]
	public async Task ODataRead_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-odata-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ODataReadTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["entity"] = "Contact",
					["select"] = new[] { "Id" },
					["top"] = 1
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid odata-read payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}

	[Test]
	[Description("Over the real MCP server, odata-read REJECTS an unknown TOP-LEVEL member (`fields` instead of `select`) at JSON binding instead of silently dropping it — the ENG-95706 silent-drop that turned a projected read into a whole-table read the agent misread as 'record missing'. The rejection names the offending member so the caller can correct the call (stand-free: the bind failure precedes any environment lookup).")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read rejects an unknown top-level member instead of silently dropping it")]
	public async Task ODataRead_Should_Reject_Unknown_TopLevel_Member_Over_Real_Server() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act — `fields` is not a member of ODataReadArgs (the correct key is `select`); this is the exact
		// mistake from the incident transcript that used to be silently dropped into a whole-table read.
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ODataReadTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "docker_fix2",
					["entity"] = "Contact",
					["fields"] = new[] { "Id" }
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert — the unknown member fails loudly at binding (never a silent drop), and the failure names the
		// offending key. The rejection surfaces on the protocol error channel because [JsonUnmappedMemberHandling
		// (Disallow)] rejects during argument deserialization, before the tool body runs.
		callResult.IsError.Should().BeTrue(
			because: "an unrecognized member must fail the call, not be silently dropped into an unfiltered whole-table read (ENG-95706)");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("fields",
			because: "the rejection must name the offending member so the caller can correct `fields` -> `select`");
	}

	[Test]
	[Description("Over the real MCP server, odata-read REJECTS an unknown NESTED filter-condition member (`operator` instead of `op`) instead of silently dropping it — the ENG-95706 defense-in-depth guard: a mistyped condition key would otherwise vanish, BuildCondition would emit nothing, and the read would come back unfiltered. Stand-free: the bind failure precedes any environment lookup.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read rejects an unknown nested filter-condition member instead of silently dropping it")]
	public async Task ODataRead_Should_Reject_Unknown_Nested_Filter_Member_Over_Real_Server() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act — `operator` is not a member of ODataFilterCondition (the correct key is `op`); a dropped condition
		// key silently widens the read, the same failure mode the top-level guard stops one level up.
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ODataReadTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "docker_fix2",
					["entity"] = "Contact",
					["filters"] = new Dictionary<string, object?> {
						["all"] = new[] {
							new Dictionary<string, object?> {
								["field"] = "Name",
								["operator"] = "eq",
								["value"] = "probe"
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "an unrecognized nested condition member must fail the call, not be silently dropped into an unfiltered read (ENG-95706)");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("operator",
			because: "the rejection must name the offending nested member so the caller can correct `operator` -> `op`");
	}

}
