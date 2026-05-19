using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the <c>apply-static-filter</c> business-rule action variant. Each test
/// starts the real clio MCP server, calls <c>create-entity-business-rule</c> over the
/// Model Context Protocol, and verifies the deserialization / dispatch path for the friendly
/// filter contract added by ENG-89355 / ENG-88588.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateEntityBusinessRuleTool.BusinessRuleCreateToolName)]
[NonParallelizable]
public sealed class EntityBusinessRuleApplyStaticFilterE2ETests {
	private const string ToolName = CreateEntityBusinessRuleTool.BusinessRuleCreateToolName;
	private const string ApplyStaticFilterActionType = "apply-static-filter";

	[Test]
	[Description("Verifies that create-entity-business-rule advertises apply-static-filter as a polymorphic action branch with targetAttribute + filter properties (no items).")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool advertises apply-static-filter branch")]
	[AllureDescription("Starts the real clio MCP server, lists tools, and verifies the create-entity-business-rule input schema includes an apply-static-filter anyOf branch with targetAttribute + filter properties and no items array.")]
	public async Task BusinessRuleCreate_Should_Advertise_ApplyStaticFilter_Action_Branch() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		McpClientTool tool = tools.Single(t => t.Name == ToolName);
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement actionSchema = inputSchema
			.GetProperty("properties")
			.GetProperty("rule")
			.GetProperty("properties")
			.GetProperty("actions")
			.GetProperty("items");
		JsonElement anyOf = actionSchema.GetProperty("anyOf");
		JsonElement applyStaticFilterBranch = anyOf.EnumerateArray()
			.Single(branch => branch.GetProperty("properties").GetProperty("type")
				.GetProperty("const").GetString() == ApplyStaticFilterActionType);
		applyStaticFilterBranch.GetProperty("properties").TryGetProperty("targetAttribute", out _)
			.Should().BeTrue(because: "apply-static-filter action must advertise a targetAttribute property");
		applyStaticFilterBranch.GetProperty("properties").TryGetProperty("filter", out _)
			.Should().BeTrue(because: "apply-static-filter action must advertise a filter property");
	}

	[Test]
	[Description("Binds an apply-static-filter payload with a basic constant comparison and reports an invalid environment failure from command execution (not from MCP payload binding).")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds apply-static-filter constant payload")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with an apply-static-filter action narrowing a Lookup by a constant comparison and an intentionally missing environment, then verifies the request reaches command execution instead of failing payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_ApplyStaticFilter_Constant_Payload() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-apply-static-filter-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environmentName"] = invalidEnvironmentName,
				["packageName"] = "UsrPkg",
				["entitySchemaName"] = "UsrTest_bp_mcp",
				["rule"] = new Dictionary<string, object?> {
					["caption"] = "Country starts with U",
					["actions"] = new object[] {
						new Dictionary<string, object?> {
							["type"] = ApplyStaticFilterActionType,
							["targetAttribute"] = "UsrCountry",
							["filter"] = new Dictionary<string, object?> {
								["logicalOperation"] = "AND",
								["filters"] = new object[] {
									new Dictionary<string, object?> {
										["columnPath"] = "Name",
										["comparisonType"] = "START_WITH",
										["value"] = "U"
									}
								}
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid apply-static-filter payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution, after MCP payload binding");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the apply-static-filter payload");
	}

	[Test]
	[Description("Binds an apply-static-filter payload with a backward-reference aggregation (COUNT) through the real MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds apply-static-filter COUNT aggregation")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with an apply-static-filter action whose backwardReferenceFilters carries a COUNT aggregation, and verifies the payload reaches command execution.")]
	public async Task BusinessRuleCreate_Should_Bind_ApplyStaticFilter_Aggregation_Payload() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-apply-static-filter-agg-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environmentName"] = invalidEnvironmentName,
				["packageName"] = "UsrPkg",
				["entitySchemaName"] = "UsrTest_bp_mcp",
				["rule"] = new Dictionary<string, object?> {
					["caption"] = "Contacts with 3+ activities",
					["actions"] = new object[] {
						new Dictionary<string, object?> {
							["type"] = ApplyStaticFilterActionType,
							["targetAttribute"] = "UsrContact",
							["filter"] = new Dictionary<string, object?> {
								["logicalOperation"] = "AND",
								["backwardReferenceFilters"] = new object[] {
									new Dictionary<string, object?> {
										["referenceColumnPath"] = "[Activity:Owner]",
										["aggregationType"] = "COUNT",
										["comparisonType"] = "GREATER_OR_EQUAL",
										["aggregationValue"] = 3,
										["filter"] = new Dictionary<string, object?> {
											["logicalOperation"] = "AND"
										}
									}
								}
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid apply-static-filter aggregation payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment");
	}

	[Test]
	[Description("Binds an apply-static-filter payload with nested logical groups and multi-value Lookup IN through the real MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds apply-static-filter nested + multi-value")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with an apply-static-filter action whose filter contains nested groups[] and a multi-value array on a Lookup column. Confirms the contract surface accepts both features together.")]
	public async Task BusinessRuleCreate_Should_Bind_ApplyStaticFilter_Nested_And_MultiValue() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-apply-static-filter-nested-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environmentName"] = invalidEnvironmentName,
				["packageName"] = "UsrPkg",
				["entitySchemaName"] = "UsrTest_bp_mcp",
				["rule"] = new Dictionary<string, object?> {
					["caption"] = "Customer or Partner from USA or Ukraine",
					["actions"] = new object[] {
						new Dictionary<string, object?> {
							["type"] = ApplyStaticFilterActionType,
							["targetAttribute"] = "UsrAccount",
							["filter"] = new Dictionary<string, object?> {
								["logicalOperation"] = "OR",
								["groups"] = new object[] {
									new Dictionary<string, object?> {
										["logicalOperation"] = "AND",
										["filters"] = new object[] {
											new Dictionary<string, object?> {
												["columnPath"] = "Type",
												["comparisonType"] = "EQUAL",
												["value"] = new object[] { "Customer", "Partner" }
											},
											new Dictionary<string, object?> {
												["columnPath"] = "Country.Name",
												["comparisonType"] = "EQUAL",
												["value"] = "USA"
											}
										}
									},
									new Dictionary<string, object?> {
										["logicalOperation"] = "AND",
										["filters"] = new object[] {
											new Dictionary<string, object?> {
												["columnPath"] = "Type",
												["comparisonType"] = "EQUAL",
												["value"] = "Customer"
											},
											new Dictionary<string, object?> {
												["columnPath"] = "Country.Name",
												["comparisonType"] = "EQUAL",
												["value"] = "Ukraine"
											}
										}
									}
								}
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "nested groups + multi-value IN payloads should bind through MCP and return a command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
	}

	[Test]
	[Description("Binds an apply-static-filter payload with a relative-date macro value through the real MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds apply-static-filter macro value")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with an apply-static-filter whose value is a PREVIOUS_WEEK relative-date macro on a temporal column. Confirms MCP deserializes the macro string verbatim into the friendly filter.")]
	public async Task BusinessRuleCreate_Should_Bind_ApplyStaticFilter_Macro_Value() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-apply-static-filter-macro-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environmentName"] = invalidEnvironmentName,
				["packageName"] = "UsrPkg",
				["entitySchemaName"] = "UsrTest_bp_mcp",
				["rule"] = new Dictionary<string, object?> {
					["caption"] = "Contacts created within the previous week",
					["actions"] = new object[] {
						new Dictionary<string, object?> {
							["type"] = ApplyStaticFilterActionType,
							["targetAttribute"] = "UsrContact",
							["filter"] = new Dictionary<string, object?> {
								["logicalOperation"] = "AND",
								["filters"] = new object[] {
									new Dictionary<string, object?> {
										["columnPath"] = "CreatedOn",
										["comparisonType"] = "EQUAL",
										["value"] = "PREVIOUS_WEEK"
									}
								}
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "macro-bearing payloads should bind and return the standard command execution envelope; macro semantics are evaluated at runtime by the platform");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
	}

	private static bool ContainsText(string? value, string expectedText) =>
		value != null && value.Contains(expectedText, StringComparison.OrdinalIgnoreCase);

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
