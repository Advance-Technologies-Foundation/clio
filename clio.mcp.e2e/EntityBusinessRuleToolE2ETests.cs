using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the entity business-rule MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateEntityBusinessRuleTool.BusinessRuleCreateToolName)]
[NonParallelizable]
public sealed class EntityBusinessRuleToolE2ETests : McpContractFixtureBase {
	private const string ToolName = CreateEntityBusinessRuleTool.BusinessRuleCreateToolName;

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes the polymorphic business-rule action contract through the get-tool-contract full contract on the lazy MCP surface.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool exposes polymorphic action contract on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server, fetches the full create-entity-business-rules contract via get-tool-contract, and verifies the supported action subtypes, the Set values item shape, and the apply-filter payload fields are described there — the lazy-surface replacement for the removed tools/list anyOf runtime schema.")]
	public async Task BusinessRuleCreate_Should_Advertise_Polymorphic_Action_AnyOf_Runtime_Schema() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		// create-entity-business-rules is not resident in tools/list on the lazy surface, so its
		// polymorphic action contract is asserted against the full get-tool-contract definition
		// (validators carry the action-subtype whitelist and per-action payload shapes) instead of
		// the SDK-generated tools/list anyOf runtime schema.
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = new[] { ToolName } }
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);
		ToolContractDefinition contract = contracts.Tools!.Single(tool => tool.Name == ToolName);

		// Assert
		IReadOnlyList<ToolContractValidator> validators = contract.InputSchema.Validators!;
		ToolContractValidator actionTypeValidator = validators.Should()
			.ContainSingle(validator => validator.Code == "unsupported-action",
				because: "the contract should carry exactly one action-subtype whitelist validator")
			.Which;
		string[] supportedActionTypes = [
			"make-editable", "make-read-only", "make-required", "make-optional",
			"set-values", "apply-filter", "apply-static-filter"
		];
		foreach (string actionType in supportedActionTypes) {
			actionTypeValidator.Context.Should().Contain(actionType,
				because: "the contract should describe each supported business-rule action subtype");
		}
		actionTypeValidator.Context.Should().NotContainAny(["hide-element", "show-element"],
			because: "page-only actions should not appear in the entity business-rule contract");
		ToolContractValidator setValuesValidator = validators.Should()
			.ContainSingle(validator => validator.Code == "invalid-set-values-item",
				because: "the contract should describe the Set values action item shape")
			.Which;
		setValuesValidator.Context.Should().Contain("expression",
			because: "Set values action items should advertise the target expression object");
		setValuesValidator.Context.Should().Contain("value",
			because: "Set values action items should advertise the value expression object");
		ToolContractValidator applyFilterValidator = validators.Should()
			.ContainSingle(validator => validator.Code == "invalid-apply-filter-action",
				because: "the contract should describe the apply-filter action payload")
			.Which;
		foreach (string fieldName in new[] { "target", "targetFilterPath", "source", "sourceFilterPath", "clearValue", "populateValue" }) {
			applyFilterValidator.Context.Should().Contain(fieldName,
				because: "apply-filter should advertise its dedicated lookup-filter payload fields through the contract");
		}
		applyFilterValidator.Context.Should().Contain("Lookup",
			because: "the contract should describe filter paths as lookup-valued paths");
		applyFilterValidator.Context.Should().Contain("not Guid",
			because: "the contract should explicitly reject Guid-valued filter paths");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a Set values business-rule payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds Set values payloads")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with Set values constant formula and AttributeValue payloads and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_SetValues_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-business-rule-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rules"] = new object[] { CreateSetValuesRule()
 }
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

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a multi-rule batch payload through the real MCP server and reports an invalid environment failure for the whole batch.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds a multi-rule batch")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rules with two rules in one call and an intentionally missing environment, then verifies the multi-element rules array binds and the structured response references the missing environment instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_Multiple_Rules_Batch_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-batch-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rules"] = new object[] { CreateSetValuesRule(), CreateSysValueConditionRule() }
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid two-rule batch payload should bind and return the structured batch response, not an MCP binding error");
		callResult.Content!.Select(content => content.ToString()).Should().Contain(message =>
				ContainsText(message, invalidEnvironmentName),
			because: "the whole batch fails on the missing environment, so the structured response should reference it");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds an apply-filter business-rule payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds apply-filter payloads")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with an apply-filter payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_ApplyFilter_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-apply-filter-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rules"] = new object[] { CreateApplyFilterRule()
 }
				}
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

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a system-variable condition payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds system-variable conditions")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with a SysValue condition payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_SysValue_Condition_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-sys-value-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rules"] = new object[] { CreateSysValueConditionRule()
 }
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

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a role-gate condition payload (CurrentUserRoles CONTAIN role on the left) through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds role-gate conditions")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with a CurrentUserRoles CONTAIN role condition and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_RoleGate_Condition_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-role-gate-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rules"] = new object[] { CreateRoleGateRule("Require status for administrators")
 }
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

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Returns readable MCP diagnostics when business-rule action payload deserialization fails before command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool surfaces action deserialization errors")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rule with an invalid polymorphic action payload, and verifies the client receives a readable deserialization error instead of a generic invocation failure.")]
	public async Task BusinessRuleCreate_Should_Surface_Action_Deserialization_Error() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "missing-business-rule-env",
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rules"] = new object[] { CreateInvalidActionRule()
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		// Lazy-surface routing: create-entity-business-rules is not resident, so the session dispatches
		// the call through clio-run. The target's args record still fails binding inside the dispatch,
		// but the executor wraps the diagnostic as "Error: tool '<name>' failed: …" instead of the
		// native "Failed to deserialize argument 'args' for MCP tool '<name>'" text — accept either.
		callResult.IsError.Should().BeTrue(
			because: "argument binding failures occur before command execution and should be returned as MCP error results");
		callResult.Content.Should().NotBeNullOrEmpty(
			because: "the MCP error result should include human-readable diagnostics");
		callResult.Content!.Select(content => content.ToString()).Should().Contain(message =>
				(message.Contains($"Failed to deserialize argument 'args' for MCP tool '{ToolName}'", StringComparison.Ordinal)
					|| message.Contains($"tool '{ToolName}' failed", StringComparison.OrdinalIgnoreCase))
				&& message.Contains("unsupported-action", StringComparison.Ordinal),
			because: "the caller should see the underlying System.Text.Json action binding error, natively or wrapped by the clio-run executor");
	}

	[Category("McpE2E.Sandbox")]
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
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
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
					["rules"] = new object[] { CreateContactEntityRule(caption)
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse batchResponse = McpCommandExecutionParser.ExtractBusinessRuleBatchResponse(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact entity rule should return the structured batch-create response, not an MCP error");
		batchResponse.Created.Should().Be(1,
			because: "the single Contact rule should be created in the configured Creatio sandbox");
		batchResponse.Failed.Should().Be(0,
			because: "no rule in the batch should fail when the payload is valid");
		batchResponse.Results.Should().ContainSingle(result => result.Success && !string.IsNullOrWhiteSpace(result.RuleName),
			because: "the per-rule result should report success and the generated internal rule name");
		await BusinessRuleAddonReadback.AssertEntityRuleExistsAsync(
			settings,
			environmentName,
			packageName,
			"Contact",
			McpCommandExecutionParser.ExtractBusinessRuleName(batchResponse),
			"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionReadonlyElement",
			["Name"],
			"Name",
			arrangeContext.CancellationTokenSource.Token);
	}

	[Category("McpE2E.Sandbox")]
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
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string caption = $"MCP E2E Contact sys-value {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "Contact",
					["rules"] = new object[] { CreateContactSysValueRule(caption)
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse batchResponse = McpCommandExecutionParser.ExtractBusinessRuleBatchResponse(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact system-variable rule should return the structured batch-create response, not an MCP error");
		batchResponse.Created.Should().Be(1,
			because: "the single Contact system-variable rule should be created in the configured Creatio sandbox");
		batchResponse.Failed.Should().Be(0,
			because: "no rule in the batch should fail when the payload is valid");
		batchResponse.Results.Should().ContainSingle(result => result.Success && !string.IsNullOrWhiteSpace(result.RuleName),
			because: "the per-rule result should report success and the generated internal rule name");
		await BusinessRuleAddonReadback.AssertEntityRuleSysValueConditionExistsAsync(
			settings,
			environmentName,
			packageName,
			"Contact",
			McpCommandExecutionParser.ExtractBusinessRuleName(batchResponse),
			"Owner",
			"CurrentUserContact",
			"Lookup",
			"Contact",
			arrangeContext.CancellationTokenSource.Token);
	}

	[Category("McpE2E.Sandbox")]
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
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string caption = $"MCP E2E Contact role-gate {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "Contact",
					["rules"] = new object[] { CreateRoleGateRule(caption)
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse batchResponse = McpCommandExecutionParser.ExtractBusinessRuleBatchResponse(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact role-gate rule should return the structured batch-create response, not an MCP error");
		batchResponse.Created.Should().Be(1,
			because: "the single Contact role-gate rule should be created in the configured Creatio sandbox");
		batchResponse.Failed.Should().Be(0,
			because: "no rule in the batch should fail when the payload is valid");
		batchResponse.Results.Should().ContainSingle(result => result.Success && !string.IsNullOrWhiteSpace(result.RuleName),
			because: "the per-rule result should report success and the generated internal rule name");
		await BusinessRuleAddonReadback.AssertEntityRuleRoleGateConditionExistsAsync(
			settings,
			environmentName,
			packageName,
			"Contact",
			McpCommandExecutionParser.ExtractBusinessRuleName(batchResponse),
			"CurrentUserRoles",
			11,
			RoleGateRoleId,
			"SysAdminUnit",
			arrangeContext.CancellationTokenSource.Token);
	}

	[Category("McpE2E.Sandbox")]
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
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string caption = $"MCP E2E Contact apply-filter {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "Contact",
					["rules"] = new object[] { CreateContactApplyFilterRule(caption)
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse batchResponse = McpCommandExecutionParser.ExtractBusinessRuleBatchResponse(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact apply-filter rule should return the structured batch-create response, not an MCP error");
		batchResponse.Created.Should().Be(1,
			because: "the single apply-filter parent rule should be created in the configured Creatio sandbox (clear/populate children are generated server-side and are not separate batch items)");
		batchResponse.Failed.Should().Be(0,
			because: "no rule in the batch should fail when the apply-filter payload is valid");
		batchResponse.Results.Should().ContainSingle(result => result.Success && !string.IsNullOrWhiteSpace(result.RuleName),
			because: "the per-rule result should report success and the generated parent rule name");
		await BusinessRuleAddonReadback.AssertEntityApplyFilterRuleFamilyExistsAsync(
			settings,
			environmentName,
			packageName,
			"Contact",
			McpCommandExecutionParser.ExtractBusinessRuleName(batchResponse),
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

}
