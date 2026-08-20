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
	[Description("Rejects a raw filter argument through the real stdio and clio-run path before resolving the requested environment.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read rejects silently ignored raw filter over stdio")]
	public async Task ODataRead_Should_Reject_Raw_Filter_Through_ClioRun() {
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
					["filter"] = "Id eq 00000000-0000-0000-0000-000000000000"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "argument validation failures use the odata-read structured response envelope");
		response.Success.Should().BeFalse(
			because: "a raw filter must fail over the real stdio binding path instead of being discarded");
		response.Error.Should().Contain("raw filter strings are not accepted",
			because: "the clio-run dispatch must explain that a raw string cannot simply be renamed");
		response.Error.Should().Contain("filters: {\"all\"",
			because: "the clio-run dispatch should preserve a valid structured-filter example");
		response.Error.Should().NotContain(invalidEnvironmentName,
			because: "argument validation must run before environment resolution or remote access");
	}

	[Test]
	[Description("Rejects an unknown nested filter member through the real stdio binding path.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read rejects silently ignored nested filter members over stdio")]
	public async Task ODataRead_Should_Reject_Unknown_Nested_Filter_Member_Through_ClioRun() {
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
					["filters"] = new Dictionary<string, object?> {
						["and"] = Array.Empty<object>()
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(
			because: "unknown nested filter members must fail over the real stdio binder instead of disappearing");
		response.Error.Should().Contain("'and' -> 'all'",
			because: "the structured response should identify the supported filter group");
		response.Error.Should().NotContain(invalidEnvironmentName,
			because: "nested filter validation must run before environment resolution or remote access");
	}

	[Test]
	[Description("Rejects an explicitly null filters object through the real stdio binding path.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read rejects null filters over stdio")]
	public async Task ODataRead_Should_Reject_Null_Filters_Through_ClioRun() {
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
					["filters"] = null
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(
			because: "explicit null must fail instead of widening to an unfiltered read");
		response.Error.Should().Contain("null is not supported",
			because: "the real binder must preserve filters member presence");
		response.Error.Should().NotContain(invalidEnvironmentName,
			because: "null-filter validation must run before environment resolution or remote access");
	}

	[Test]
	[Description("Rejects OData grammar embedded in a structured field through the real stdio binding path.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read rejects filter field grammar over stdio")]
	public async Task ODataRead_Should_Reject_Filter_Field_Grammar_Through_ClioRun() {
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
					["filters"] = new Dictionary<string, object?> {
						["all"] = new object[] {
							new Dictionary<string, object?> {
								["field"] = "Id ne null or Name",
								["value"] = "Acme"
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(
			because: "structured fields must not carry OData operators that broaden the request");
		response.Error.Should().Contain("must be an OData member path",
			because: "the real binder must preserve and reject the unsafe field expression");
		response.Error.Should().NotContain(invalidEnvironmentName,
			because: "field validation must run before environment resolution or remote access");
	}

	[Test]
	[Description("Preserves an explicit null comparison value through stdio binding and reaches normal environment resolution.")]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read preserves explicit null filter value over stdio")]
	public async Task ODataRead_Should_Preserve_Null_Filter_Value_Through_ClioRun() {
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
					["filters"] = new Dictionary<string, object?> {
						["all"] = new object[] {
							new Dictionary<string, object?> {
								["field"] = "Name",
								["op"] = "eq",
								["value"] = null
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail after argument validation succeeds");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "explicit null must remain a present comparison value through the real binder");
		response.Error.Should().NotContain("exactly one of value or in",
			because: "explicit null is a valid comparison value, not an omitted member");
	}

	[Test]
	[Description("Publishes skip, count, total-count, and raw-filter rejection in the live curated odata-read contract.")]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("odata-read curated contract describes paging and strict filters")]
	public async Task ODataRead_Contract_Should_Describe_Paging_Count_And_Filter_Strictness() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { ODataReadTool.ToolName }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse response = EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
		ToolContractDefinition contract = response.Tools.Should().ContainSingle(
			because: "the requested odata-read contract should resolve through the real MCP server").Which;

		// Assert
		contract.InputSchema.Properties.Select(field => field.Name).Should().Contain(["skip", "count"],
			because: "agents need discoverable paging and total-count inputs before calling the hidden tool");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "total-count",
			because: "the total matching count must be distinct from the returned-page count");
		contract.Aliases.Should().Contain(alias => alias.Alias == "filter" && alias.Status == "rejected",
			because: "the previously silent raw filter must be explicitly rejected with a structured-filter hint");
	}

}
