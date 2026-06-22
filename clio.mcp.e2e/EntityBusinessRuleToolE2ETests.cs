using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the entity business-rule MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateEntityBusinessRuleTool.BusinessRuleCreateToolName)]
[NonParallelizable]
public sealed class EntityBusinessRuleToolE2ETests {
	private const string ToolName = CreateEntityBusinessRuleTool.BusinessRuleCreateToolName;

	[Test]
	[Description("Advertises polymorphic anyOf runtime schema branches for business-rule actions through the real MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool advertises polymorphic action schema")]
	[AllureDescription("Starts the real clio MCP server, lists tools, and verifies create-entity-business-rule exposes anyOf action branches for field-state actions and Set values assignments.")]
	public async Task BusinessRuleCreate_Should_Advertise_Polymorphic_Action_AnyOf_Runtime_Schema() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		McpClientTool tool = tools.Single(tool => tool.Name == ToolName);
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement actionSchema = inputSchema
			.GetProperty("properties")
			.GetProperty("args")
			.GetProperty("properties")
			.GetProperty("rule")
			.GetProperty("properties")
			.GetProperty("actions")
			.GetProperty("items");
		JsonElement anyOf = actionSchema.GetProperty("anyOf");
		anyOf.GetArrayLength().Should().Be(7,
			because: "the real MCP tools/list schema should describe each supported business-rule action subtype");
		anyOf.EnumerateArray().Select(GetActionType).Should().NotContain(["hide-element", "show-element"],
			because: "page-only actions should not appear in the entity business-rule runtime schema");
		anyOf.EnumerateArray().Should().Contain(branch =>
				branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "make-required"
				&& branch.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("type").GetString() == "string",
			because: "field-state action branches should use string-array action items");
		JsonElement setValuesItemsSchema = anyOf.EnumerateArray()
			.Single(branch => branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "set-values")
			.GetProperty("properties")
			.GetProperty("items")
			.GetProperty("items");
		setValuesItemsSchema.GetProperty("properties").EnumerateObject()
			.Select(property => property.Name).Should().Contain(["expression", "value"],
				because: "Set values action items should advertise target and value expression objects");
		JsonElement applyFilterSchema = anyOf.EnumerateArray()
			.Single(branch => branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "apply-filter");
		applyFilterSchema.GetProperty("properties").EnumerateObject()
			.Select(property => property.Name).Should().Contain([
				"type",
				"target",
				"targetFilterPath",
				"source",
				"sourceFilterPath",
				"clearValue",
				"populateValue"
			],
			because: "apply-filter should advertise its dedicated lookup-filter payload fields through the runtime schema");
		applyFilterSchema.GetProperty("properties").GetProperty("targetFilterPath").GetProperty("description").GetString()
			.Should().Contain("Lookup",
				because: "runtime MCP schema should describe targetFilterPath as a lookup-valued path");
		applyFilterSchema.GetProperty("properties").GetProperty("targetFilterPath").GetProperty("description").GetString()
			.Should().Contain("not Guid",
				because: "runtime MCP schema should explicitly reject Guid-valued targetFilterPath paths");
		applyFilterSchema.GetProperty("properties").GetProperty("sourceFilterPath").GetProperty("description").GetString()
			.Should().Contain("Lookup",
				because: "runtime MCP schema should describe sourceFilterPath as a lookup-valued path");
		applyFilterSchema.GetProperty("properties").GetProperty("sourceFilterPath").GetProperty("description").GetString()
			.Should().Contain("not Guid",
				because: "runtime MCP schema should explicitly reject Guid-valued sourceFilterPath paths");
	}

	[Test]
	[Description("Binds a Set values business-rule payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds Set values payloads")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with Set values constant formula and AttributeValue payloads and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_SetValues_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-business-rule-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rule"] = CreateSetValuesRule()
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid Set values payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the Set values payload");
	}

	[Test]
	[Description("Binds an apply-filter business-rule payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds apply-filter payloads")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with an apply-filter payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_ApplyFilter_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-apply-filter-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environmentName"] = invalidEnvironmentName,
				["packageName"] = "UsrPkg",
				["entitySchemaName"] = "UsrOrder",
				["rule"] = CreateApplyFilterRule()
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid apply-filter payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the apply-filter payload");
	}

	[Test]
	[Description("Binds a system-variable condition payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds system-variable conditions")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with a SysValue condition payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_SysValue_Condition_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-sys-value-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rule"] = CreateSysValueConditionRule()
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid system-variable condition payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the SysValue payload");
	}

	[Test]
	[Description("Binds a role-gate condition payload (CurrentUserRoles CONTAIN role on the left) through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds role-gate conditions")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with a CurrentUserRoles CONTAIN role condition and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_RoleGate_Condition_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-role-gate-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rule"] = CreateRoleGateRule("Require status for administrators")
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid role-gate payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the role-gate payload");
	}

	[Test]
	[Description("Returns readable MCP diagnostics when business-rule action payload deserialization fails before command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool surfaces action deserialization errors")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with an invalid polymorphic action payload, and verifies the client receives a readable deserialization error instead of a generic invocation failure.")]
	public async Task BusinessRuleCreate_Should_Surface_Action_Deserialization_Error() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "missing-business-rule-env",
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rule"] = CreateInvalidActionRule()
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "argument binding failures occur before command execution and should be returned as MCP error results");
		callResult.Content.Should().NotBeNullOrEmpty(
			because: "the MCP error result should include human-readable diagnostics");
		callResult.Content!.Select(content => content.ToString()).Should().Contain(message =>
				message.Contains($"Failed to deserialize argument 'args' for MCP tool '{ToolName}'", StringComparison.Ordinal)
				&& message.Contains("unsupported-action", StringComparison.Ordinal),
			because: "the caller should see the underlying System.Text.Json action binding error");
	}

	[Test]
	[Description("Creates an entity business rule for Contact in the Custom package through the real MCP server and Creatio environment.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool creates a Contact rule")]
	[AllureDescription("Requires a reachable sandbox environment and destructive opt-in. Calls create-entity-business-rule for Contact in Custom and verifies that Creatio command execution succeeds.")]
	public async Task BusinessRuleCreate_Should_Create_Contact_Rule_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false - skipping destructive create-entity-business-rule test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(5));
		// Intentional destructive fixture: Contact in Custom is the canonical sandbox target.
		// There is no business-rule delete action yet, so each run writes a uniquely captioned rule and verifies readback.
		string caption = $"MCP E2E Contact entity {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "Contact",
					["rule"] = CreateContactEntityRule(caption)
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact entity rule should return the standard command execution envelope");
		execution.ExitCode.Should().Be(0,
			because: "the Contact rule should be created in the configured Creatio sandbox");
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: "successful command-path MCP calls should include at least one info log message");
		execution.Output.Should().Contain(message => ContainsText(message.Value, "Rule name:"),
			because: "successful business-rule creation should report the generated rule name");
		await BusinessRuleAddonReadback.AssertEntityRuleExistsAsync(
			settings,
			environmentName,
			packageName,
			"Contact",
			McpCommandExecutionParser.ExtractBusinessRuleName(execution),
			"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionReadonlyElement",
			["Name"],
			"Name",
			arrangeContext.CancellationTokenSource.Token);
	}

	[Test]
	[Description("Creates an entity business rule with a system-variable condition for Contact through the real MCP server and verifies the persisted SysValue right expression.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool creates a system-variable condition rule")]
	[AllureDescription("Requires a reachable sandbox environment and destructive opt-in. Calls create-entity-business-rule for Contact with an Owner equals CurrentUserContact condition and verifies the persisted BusinessRuleSysValueExpression through add-on readback.")]
	public async Task BusinessRuleCreate_Should_Create_SysValue_Condition_Rule_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false - skipping destructive system-variable create-entity-business-rule test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(5));
		string caption = $"MCP E2E Contact sys-value {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "Contact",
					["rule"] = CreateContactSysValueRule(caption)
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact system-variable rule should return the standard command execution envelope");
		execution.ExitCode.Should().Be(0,
			because: "the Contact system-variable rule should be created in the configured Creatio sandbox");
		execution.Output.Should().Contain(message => ContainsText(message.Value, "Rule name:"),
			because: "successful business-rule creation should report the generated rule name");
		await BusinessRuleAddonReadback.AssertEntityRuleSysValueConditionExistsAsync(
			settings,
			environmentName,
			packageName,
			"Contact",
			McpCommandExecutionParser.ExtractBusinessRuleName(execution),
			"Owner",
			"CurrentUserContact",
			"Lookup",
			"Contact",
			arrangeContext.CancellationTokenSource.Token);
	}

	[Test]
	[Description("Creates a role-gate entity business rule (CurrentUserRoles CONTAIN role) for Contact through the real MCP server and verifies the persisted left SysValue + contain condition.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool creates a role-gate rule")]
	[AllureDescription("Requires a reachable sandbox environment and destructive opt-in. Calls create-entity-business-rule for Contact with a CurrentUserRoles CONTAIN role condition and verifies the persisted SysValue-left contain condition through add-on readback.")]
	public async Task BusinessRuleCreate_Should_Create_RoleGate_Rule_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false - skipping destructive role-gate create-entity-business-rule test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(5));
		string caption = $"MCP E2E Contact role-gate {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "Contact",
					["rule"] = CreateRoleGateRule(caption)
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact role-gate rule should return the standard command execution envelope");
		execution.ExitCode.Should().Be(0,
			because: "the Contact role-gate rule should be created in the configured Creatio sandbox");
		execution.Output.Should().Contain(message => ContainsText(message.Value, "Rule name:"),
			because: "successful business-rule creation should report the generated rule name");
		await BusinessRuleAddonReadback.AssertEntityRuleRoleGateConditionExistsAsync(
			settings,
			environmentName,
			packageName,
			"Contact",
			McpCommandExecutionParser.ExtractBusinessRuleName(execution),
			"CurrentUserRoles",
			11,
			RoleGateRoleId,
			"SysAdminUnit",
			arrangeContext.CancellationTokenSource.Token);
	}

	[Test]
	[Description("Creates an apply-filter entity business-rule family for Contact in the target package through the real MCP server and verifies persisted parent and child metadata.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool creates an apply-filter rule family")]
	[AllureDescription("Requires a reachable sandbox environment and destructive opt-in. Calls create-entity-business-rule with an apply-filter payload for Contact and verifies the persisted parent filter-lookup rule plus autogenerated clear and populate child rules through add-on readback.")]
	public async Task BusinessRuleCreate_Should_Create_ApplyFilter_Rule_Family_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false - skipping destructive apply-filter create-entity-business-rule test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(5));
		string caption = $"MCP E2E Contact apply-filter {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "Contact",
					["rule"] = CreateContactApplyFilterRule(caption)
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact apply-filter rule should return the standard command execution envelope");
		execution.ExitCode.Should().Be(0,
			because: "the apply-filter rule family should be created in the configured Creatio sandbox");
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: "successful command-path MCP calls should include at least one info log message");
		execution.Output.Should().Contain(message => ContainsText(message.Value, "Rule name:"),
			because: "successful apply-filter creation should report the generated parent rule name");
		await BusinessRuleAddonReadback.AssertEntityApplyFilterRuleFamilyExistsAsync(
			settings,
			environmentName,
			packageName,
			"Contact",
			McpCommandExecutionParser.ExtractBusinessRuleName(execution),
			"City",
			"Country",
			"Country",
			null,
			expectClearChild: true,
			expectPopulateChild: true,
			arrangeContext.CancellationTokenSource.Token);
	}

	private static bool ContainsText(string? value, string expectedText) =>
		value != null && value.Contains(expectedText, StringComparison.OrdinalIgnoreCase);

	private static string? GetActionType(JsonElement branch) =>
		branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString();

	private static string ResolvePackageName(McpE2ESettings settings) =>
		string.IsNullOrWhiteSpace(settings.Sandbox.PackageName)
			? "Custom"
			: settings.Sandbox.PackageName;

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore("create-entity-business-rule MCP E2E requires McpE2E:Sandbox:EnvironmentName for destructive tests.");
			return string.Empty;
		}

		if (await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		Assert.Ignore(
			$"create-entity-business-rule MCP E2E requires a reachable configured sandbox environment. Environment '{configuredEnvironmentName}' was not reachable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	private static IReadOnlyDictionary<string, object?> CreateContactEntityRule(string caption) =>
		new Dictionary<string, object?> {
			["caption"] = caption,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("Name"),
						["comparisonType"] = "is-filled-in"
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "make-read-only",
					["items"] = new object[] { "Name" }
				}
			}
		};

	// A SysAdminUnit role id; business-rule metadata save does not enforce the FK, so readback only
	// asserts the value round-trips. (This is the System Administrators role id on the reference sandbox.)
	private const string RoleGateRoleId = "83a43ebc-f36b-1410-298d-001e8c82bcad";

	private static IReadOnlyDictionary<string, object?> CreateRoleGateRule(string caption) =>
		new Dictionary<string, object?> {
			["caption"] = caption,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateSysValueExpression("CurrentUserRoles"),
						["comparisonType"] = "contain",
						["rightExpression"] = new Dictionary<string, object?> {
							["type"] = "Const",
							["value"] = RoleGateRoleId
						}
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "make-read-only",
					["items"] = new object[] { "Name" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateContactSysValueRule(string caption) =>
		new Dictionary<string, object?> {
			["caption"] = caption,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("Owner"),
						["comparisonType"] = "equal",
						["rightExpression"] = CreateSysValueExpression("CurrentUserContact")
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "make-read-only",
					["items"] = new object[] { "Name" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateSysValueConditionRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Require status when owner is the current user",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("Owner"),
						["comparisonType"] = "equal",
						["rightExpression"] = CreateSysValueExpression("CurrentUserContact")
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "make-required",
					["items"] = new object[] { "Status" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateSetValuesRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Populate defaults",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("Name"),
						["comparisonType"] = "is-filled-in"
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "set-values",
					["items"] = new object[] {
						CreateSetValuesItem("UsrTextResult", "Ready"),
						CreateSetValuesItem("UsrScore", 42),
						CreateSetValuesItem("UsrCompleted", true),
						CreateSetValuesItem("UsrPlannedOn", "2025-01-01T00:00:00Z"),
						CreateFormulaSetValuesItem("UsrTotalScore", "UsrScore + UsrBonusScore"),
						CreateAttributeSetValuesItem("UsrCreatorAge", "CreatedBy.Age")
					}
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateApplyFilterRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Filter city by country",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = Array.Empty<object>()
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "apply-filter",
					["target"] = "City",
					["targetFilterPath"] = "Country",
					["source"] = "Country",
					["clearValue"] = true,
					["populateValue"] = true
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateInvalidActionRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Invalid action",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = Array.Empty<object>()
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["items"] = new object[] { "Name" },
					["type"] = "unsupported-action"
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateContactApplyFilterRule(string caption) =>
		new Dictionary<string, object?> {
			["caption"] = caption,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = Array.Empty<object>()
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "apply-filter",
					["target"] = "City",
					["targetFilterPath"] = "Country",
					["source"] = "Country",
					["clearValue"] = true,
					["populateValue"] = true
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateSetValuesItem(string path, object value) =>
		new Dictionary<string, object?> {
			["expression"] = CreateAttributeExpression(path),
			["value"] = new Dictionary<string, object?> {
				["type"] = "Const",
				["value"] = value
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateFormulaSetValuesItem(string path, string formula) =>
		new Dictionary<string, object?> {
			["expression"] = CreateAttributeExpression(path),
			["value"] = new Dictionary<string, object?> {
				["type"] = "Formula",
				["expression"] = formula
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateAttributeSetValuesItem(string path, string sourcePath) =>
		new Dictionary<string, object?> {
			["expression"] = CreateAttributeExpression(path),
			["value"] = CreateAttributeExpression(sourcePath)
		};

	private static IReadOnlyDictionary<string, object?> CreateAttributeExpression(string path) =>
		new Dictionary<string, object?> {
			["type"] = "AttributeValue",
			["path"] = path
		};

	private static IReadOnlyDictionary<string, object?> CreateSysValueExpression(string sysValueName) =>
		new Dictionary<string, object?> {
			["type"] = "SysValue",
			["sysValueName"] = sysValueName
		};

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
