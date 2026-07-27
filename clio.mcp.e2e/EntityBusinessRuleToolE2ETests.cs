using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.BusinessRules;
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
	[Description("Binds a system-setting condition payload (SysSetting operand compared to a Boolean constant) through the real MCP server and reports an invalid environment failure from command execution (ENG-91254).")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool binds system-setting conditions")]
	[AllureDescription("Starts the real clio MCP server, calls create-entity-business-rules with a SysSetting condition payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_SysSetting_Condition_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-sys-setting-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rules"] = new object[] { CreateSysSettingConditionRule() }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid system-setting condition payload should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the SysSetting payload");
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
		batchResponse.Succeeded.Should().Be(1,
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
		batchResponse.Succeeded.Should().Be(1,
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
		batchResponse.Succeeded.Should().Be(1,
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
		batchResponse.Succeeded.Should().Be(1,
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

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a read-entity-business-rules request through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ReadEntityBusinessRuleTool.ToolName)]
	[AllureName("Entity business-rule read MCP tool reports an invalid environment")]
	[AllureDescription("Starts the real clio MCP server, calls read-entity-business-rules with an intentionally missing environment, then verifies the request reaches command execution and returns the standard command execution envelope referencing the environment.")]
	public async Task BusinessRulesRead_Should_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-read-rules-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ReadEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid read request should bind and return the standard command execution envelope, not an MCP binding error");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from binding the read arguments");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a named update-entity-business-rules payload with block uIds through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(UpdateEntityBusinessRuleTool.ToolName)]
	[AllureName("Entity business-rule update MCP tool binds named rule payloads")]
	[AllureDescription("Starts the real clio MCP server, calls update-entity-business-rules with a rule carrying name, enabled, and block uIds against an intentionally missing environment, then verifies the extended update contract binds and the failure comes from environment resolution.")]
	public async Task BusinessRulesUpdate_Should_Bind_Named_Rule_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-update-rules-env-{Guid.NewGuid():N}";
		EntityRuleBlockIds blockIds = new(
			Guid.NewGuid().ToString("D"),
			Guid.NewGuid().ToString("D"),
			Guid.NewGuid().ToString("D"),
			Guid.NewGuid().ToString("D"));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			UpdateEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rules"] = new object[] {
						CreateContactConstRuleUpdate("Updated readonly name", "BusinessRule_1c48625", "Beta", blockIds, enabled: true)
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a rule payload with name, enabled, and block uIds should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the named update payload");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a delete-entity-business-rules request through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(DeleteEntityBusinessRuleTool.ToolName)]
	[AllureName("Entity business-rule delete MCP tool reports an invalid environment")]
	[AllureDescription("Starts the real clio MCP server, calls delete-entity-business-rules with a rule-names array and an intentionally missing environment, then verifies the request reaches command execution and returns the standard command execution envelope referencing the environment.")]
	public async Task BusinessRulesDelete_Should_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-delete-rules-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			DeleteEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rule-names"] = new object[] { "BusinessRule_1c48625" }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid delete request should bind and return the standard command execution envelope, not an MCP binding error");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from binding the rule-names array");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Returns a request-level typed error without touching the environment when update-entity-business-rules is called with an empty rules array.")]
	[AllureTag(UpdateEntityBusinessRuleTool.ToolName)]
	[AllureName("Entity business-rule update MCP tool rejects an empty rules array")]
	[AllureDescription("Starts the real clio MCP server, calls update-entity-business-rules with an empty rules array, and verifies the typed batch response carries the request-level error before any environment resolution is attempted.")]
	public async Task BusinessRulesUpdate_Should_Return_Request_Error_When_Rules_Are_Empty() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			UpdateEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-update-rules-env-{Guid.NewGuid():N}",
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rules"] = Array.Empty<object>()
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse response =
			EntitySchemaStructuredResultParser.Extract<BusinessRuleBatchResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an empty rules array is an expected request-level failure carried in the typed batch response, not an MCP error");
		response.Error.Should().NotBeNullOrWhiteSpace(
			because: "the request-level error should explain that the rules array must not be empty");
		response.Error.Should().Contain("rules",
			because: "the request-level error should reference the offending 'rules' argument");
		response.Succeeded.Should().Be(0,
			because: "no per-rule work should be attempted when the request-level validation fails");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Returns a request-level typed error without touching the environment when delete-entity-business-rules is called with an empty rule-names array.")]
	[AllureTag(DeleteEntityBusinessRuleTool.ToolName)]
	[AllureName("Entity business-rule delete MCP tool rejects an empty rule-names array")]
	[AllureDescription("Starts the real clio MCP server, calls delete-entity-business-rules with an empty rule-names array, and verifies the typed batch response carries the request-level error before any environment resolution is attempted.")]
	public async Task BusinessRulesDelete_Should_Return_Request_Error_When_Rule_Names_Are_Empty() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			DeleteEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-delete-rules-env-{Guid.NewGuid():N}",
					["package-name"] = "UsrPkg",
					["entity-schema-name"] = "UsrOrder",
					["rule-names"] = Array.Empty<object>()
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse response =
			EntitySchemaStructuredResultParser.Extract<BusinessRuleBatchResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an empty rule-names array is an expected request-level failure carried in the typed batch response, not an MCP error");
		response.Error.Should().NotBeNullOrWhiteSpace(
			because: "the request-level error should explain that the rule-names array must not be empty");
		response.Error.Should().Contain("rule-names",
			because: "the request-level error should reference the offending 'rule-names' argument");
		response.Succeeded.Should().Be(0,
			because: "no per-name work should be attempted when the request-level validation fails");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes the entity business-rule read, update, and delete maintenance contracts through get-tool-contract on the lazy MCP surface.")]
	[AllureTag(ReadEntityBusinessRuleTool.ToolName)]
	[AllureTag(UpdateEntityBusinessRuleTool.ToolName)]
	[AllureTag(DeleteEntityBusinessRuleTool.ToolName)]
	[AllureName("Entity business-rule maintenance MCP tools expose contracts through get-tool-contract")]
	[AllureDescription("Starts the real clio MCP server, requests the read-entity-business-rules, update-entity-business-rules, and delete-entity-business-rules contracts via get-tool-contract, and verifies the read output fields, the required name match key for update, and the rule-names delete key are advertised.")]
	public async Task BusinessRulesMaintenance_Should_Advertise_Read_Update_Delete_Contracts() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] {
						ReadEntityBusinessRuleTool.ToolName,
						UpdateEntityBusinessRuleTool.ToolName,
						DeleteEntityBusinessRuleTool.ToolName
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		contractResult.IsError.Should().NotBeTrue(
			because: "the registered entity maintenance tool contracts should resolve through get-tool-contract without an MCP error");
		contracts.Success.Should().BeTrue(
			because: "all three entity business-rule maintenance tools are registered in the contract catalog");
		ToolContractDefinition readContract = contracts.Tools!.Single(tool => tool.Name == ReadEntityBusinessRuleTool.ToolName);
		readContract.OutputContract.Fields.Should().Contain(field => field.Name == "count",
			because: "the read contract should advertise the returned rule count output field");
		readContract.OutputContract.Fields.Should().Contain(field =>
				field.Name == "rules" &&
				field.Description.Contains("name", StringComparison.Ordinal),
			because: "the read contract should advertise the rules output items carrying the name match key for update/delete");
		ToolContractDefinition updateContract = contracts.Tools!.Single(tool => tool.Name == UpdateEntityBusinessRuleTool.ToolName);
		updateContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "rules" &&
				field.Description.Contains("name (REQUIRED", StringComparison.Ordinal),
			because: "the update contract should advertise name as the required match key inside each replacement rule");
		ToolContractDefinition deleteContract = contracts.Tools!.Single(tool => tool.Name == DeleteEntityBusinessRuleTool.ToolName);
		deleteContract.InputSchema.Required.Should().Contain("rule-names",
			because: "the delete contract should require the rule-names batch of internal rule names");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Runs the full entity business-rule CRUD lifecycle (create with explicit name, read with uIds, batch update with per-rule failure isolation, disable, batch delete with per-name failure isolation) through the real MCP server and Creatio environment.")]
	[AllureTag(ReadEntityBusinessRuleTool.ToolName)]
	[AllureTag(UpdateEntityBusinessRuleTool.ToolName)]
	[AllureTag(DeleteEntityBusinessRuleTool.ToolName)]
	[AllureName("Entity business-rule MCP tools complete the CRUD lifecycle")]
	[AllureDescription("Requires a reachable sandbox environment and destructive opt-in. Creates an AutoTestClioMcp rule with an explicit name, reads it back with block uIds, updates its constant value in a batch that also carries an unknown-name and a missing-name rule (asserting per-rule isolation and uId survival), disables the rule, deletes it in a batch that also carries an unknown name, and verifies the rule is gone on the final read.")]
	public async Task BusinessRules_Should_Complete_Entity_Rule_Crud_Lifecycle_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false - skipping destructive entity business-rule CRUD lifecycle test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(15));
		string suffix = Guid.NewGuid().ToString("N");
		string ruleName = $"UsrMcpE2ERule{suffix}";
		string unknownRuleName = $"UsrMcpE2EMissing{suffix}";
		string caption = $"MCP E2E AutoTestClioMcp CRUD {suffix}";

		// Act - create a rule with an explicit caller-supplied name and enabled true
		CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "AutoTestClioMcp",
					["rules"] = new object[] { CreateNamedContactConstRule(caption, ruleName, "Alpha") }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse createResponse = McpCommandExecutionParser.ExtractBusinessRuleBatchResponse(createResult);

		// Assert - create
		createResult.IsError.Should().NotBeTrue(
			because: "a valid named AutoTestClioMcp rule should return the structured batch-create response, not an MCP error");
		createResponse.Succeeded.Should().Be(1,
			because: "the single named AutoTestClioMcp rule should be created in the configured Creatio sandbox");
		createResponse.Failed.Should().Be(0,
			because: "no rule in the batch should fail when the payload is valid");
		createResponse.Results.Should().ContainSingle(result => result.Success && result.RuleName == ruleName,
			because: "create should honor the explicit caller-supplied rule name instead of generating one");

		// Act - read after create
		BusinessRulesReadResponse readAfterCreate = await ReadEntityRulesAsync(
			arrangeContext.Session, environmentName, packageName, "AutoTestClioMcp", arrangeContext.CancellationTokenSource.Token);

		// Assert - read returns the rule in contract shape with block uIds
		BusinessRule createdModel = GetRuleByName(readAfterCreate, ruleName);
		readAfterCreate.Count.Should().Be(readAfterCreate.Rules.Count,
			because: "the read response count should match the number of returned rules");
		createdModel.Enabled.Should().BeTrue(
			because: "the rule was created with enabled true");
		BusinessRuleCondition createdCondition = createdModel.Condition.Conditions.Should().ContainSingle(
				because: "the created rule has exactly one condition")
			.Which;
		createdCondition.UId.Should().NotBeNullOrWhiteSpace(
			because: "read should return the stable condition uId for update round-trips");
		createdCondition.LeftExpression.UId.Should().NotBeNullOrWhiteSpace(
			because: "read should return the stable left expression uId for update round-trips");
		createdCondition.RightExpression.Should().NotBeNull(
			because: "the created rule compares Name against a constant");
		createdCondition.RightExpression!.UId.Should().NotBeNullOrWhiteSpace(
			because: "read should return the stable right expression uId for update round-trips");
		createdCondition.RightExpression.Value?.GetString().Should().Be("Alpha",
			because: "read should return the constant value the rule was created with");
		BusinessRuleAction createdAction = createdModel.Actions.Should().ContainSingle(
				because: "the created rule has exactly one action")
			.Which;
		createdAction.UId.Should().NotBeNullOrWhiteSpace(
			because: "read should return the stable action uId for update round-trips");
		EntityRuleBlockIds blockIds = new(
			createdCondition.UId!,
			createdCondition.LeftExpression.UId!,
			createdCondition.RightExpression.UId!,
			createdAction.UId!);

		// Act - batch update: valid change with preserved uIds + unknown-name rule + missing-name rule
		CallToolResult updateResult = await arrangeContext.Session.CallToolAsync(
			UpdateEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "AutoTestClioMcp",
					["rules"] = new object[] {
						CreateContactConstRuleUpdate(caption, ruleName, "Beta", blockIds, enabled: null),
						CreateContactConstRuleUpdate(caption, unknownRuleName, "Gamma", null, enabled: null),
						CreateContactConstRuleUpdate(caption, null, "Delta", null, enabled: null)
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse updateResponse =
			EntitySchemaStructuredResultParser.Extract<BusinessRuleBatchResponse>(updateResult);

		// Assert - per-rule isolation: the valid rule saves, the two invalid rules fail individually
		updateResult.IsError.Should().NotBeTrue(
			because: "a partially failing update batch should return the structured batch response, not an MCP error");
		updateResponse.Succeeded.Should().Be(1,
			because: "only the existing named rule should be updated; the unknown and missing names fail individually");
		updateResponse.Failed.Should().Be(2,
			because: "the unknown-name and missing-name rules should each fail as isolated batch entries");
		updateResponse.Results.Should().HaveCount(3,
			because: "the batch response should carry one entry per input rule in input order");
		updateResponse.Results[0].Success.Should().BeTrue(
			because: "the first batch entry matches the existing rule by name and should save");
		updateResponse.Results[1].Success.Should().BeFalse(
			because: "the second batch entry references a rule name that does not exist");
		updateResponse.Results[1].Error.Should().Contain(unknownRuleName,
			because: "the per-rule error should reference the unknown rule name");
		updateResponse.Results[2].Success.Should().BeFalse(
			because: "the third batch entry omits the required name match key");
		updateResponse.Results[2].Error.Should().Contain("name",
			because: "the per-rule error should explain that name is required for update");

		// Act - read after update
		BusinessRulesReadResponse readAfterUpdate = await ReadEntityRulesAsync(
			arrangeContext.Session, environmentName, packageName, "AutoTestClioMcp", arrangeContext.CancellationTokenSource.Token);

		// Assert - the constant changed, the caller-supplied uIds survived, enabled stayed true
		BusinessRule updatedModel = GetRuleByName(readAfterUpdate, ruleName);
		updatedModel.Enabled.Should().BeTrue(
			because: "enabled was omitted on update, so the existing enabled value should be preserved");
		BusinessRuleCondition updatedCondition = updatedModel.Condition.Conditions.Should().ContainSingle(
				because: "the updated rule still has exactly one condition")
			.Which;
		updatedCondition.RightExpression?.Value?.GetString().Should().Be("Beta",
			because: "the update should replace the constant value with the new one");
		updatedCondition.UId.Should().Be(blockIds.ConditionUId,
			because: "the caller-supplied condition uId should survive the update");
		updatedCondition.LeftExpression.UId.Should().Be(blockIds.LeftExpressionUId,
			because: "the caller-supplied left expression uId should survive the update");
		updatedCondition.RightExpression!.UId.Should().Be(blockIds.RightExpressionUId,
			because: "the caller-supplied right expression uId should survive the update");
		updatedModel.Actions.Should().ContainSingle(
				because: "the updated rule still has exactly one action")
			.Which.UId.Should().Be(blockIds.ActionUId,
				because: "the caller-supplied action uId should survive the update");

		// Act - disable the rule via update with enabled false
		CallToolResult disableResult = await arrangeContext.Session.CallToolAsync(
			UpdateEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "AutoTestClioMcp",
					["rules"] = new object[] {
						CreateContactConstRuleUpdate(caption, ruleName, "Beta", blockIds, enabled: false)
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse disableResponse =
			EntitySchemaStructuredResultParser.Extract<BusinessRuleBatchResponse>(disableResult);

		// Assert - disable
		disableResponse.Succeeded.Should().Be(1,
			because: "the disable update targets the existing rule by name and should save");
		disableResponse.Failed.Should().Be(0,
			because: "no rule in the disable batch should fail");
		BusinessRulesReadResponse readAfterDisable = await ReadEntityRulesAsync(
			arrangeContext.Session, environmentName, packageName, "AutoTestClioMcp", arrangeContext.CancellationTokenSource.Token);
		GetRuleByName(readAfterDisable, ruleName).Enabled.Should().BeFalse(
			because: "the update with enabled false should deactivate the rule");

		// Act - batch delete: the existing rule plus an unknown name
		CallToolResult deleteResult = await arrangeContext.Session.CallToolAsync(
			DeleteEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "AutoTestClioMcp",
					["rule-names"] = new object[] { ruleName, unknownRuleName }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse deleteResponse =
			EntitySchemaStructuredResultParser.Extract<BusinessRuleBatchResponse>(deleteResult);

		// Assert - per-name isolation: the existing rule deletes, the unknown name fails individually
		deleteResult.IsError.Should().NotBeTrue(
			because: "a partially failing delete batch should return the structured batch response, not an MCP error");
		deleteResponse.Succeeded.Should().Be(1,
			because: "only the existing rule name should be deleted; the unknown name fails individually");
		deleteResponse.Failed.Should().Be(1,
			because: "the unknown rule name should fail as an isolated batch entry");
		deleteResponse.Results.Should().HaveCount(2,
			because: "the delete response should carry one entry per input name in input order");
		deleteResponse.Results[0].Success.Should().BeTrue(
			because: "the first entry targets the existing rule and should delete");
		deleteResponse.Results[1].Success.Should().BeFalse(
			because: "the second entry references a rule name that does not exist");
		deleteResponse.Results[1].Error.Should().Contain(unknownRuleName,
			because: "the per-name error should reference the unknown rule name");

		// Act - read after delete
		BusinessRulesReadResponse readAfterDelete = await ReadEntityRulesAsync(
			arrangeContext.Session, environmentName, packageName, "AutoTestClioMcp", arrangeContext.CancellationTokenSource.Token);

		// Assert - the deleted rule is gone
		readAfterDelete.Rules.Should().NotContain(
			rule => string.Equals(rule.Name, ruleName, StringComparison.OrdinalIgnoreCase),
			because: "the deleted rule should no longer be returned by read");
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

	private static IReadOnlyDictionary<string, object?> CreateSysSettingConditionRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Lock owner when equipment is locked after submission",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = new Dictionary<string, object?> {
							["type"] = "SysSetting",
							["sysSettingName"] = "LockEquipmentAfterSubmission"
						},
						["comparisonType"] = "equal",
						["rightExpression"] = new Dictionary<string, object?> {
							["type"] = "Const",
							["value"] = true
						}
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "make-read-only",
					["items"] = new object[] { "Owner" }
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

	[Test]
	[Category("McpE2E.Sandbox")]
	[Description("Round-trips the apply-filter clearValue/populateValue flags through create, Creatio persistence, and read. The platform drops the designer flags from the persisted action, so read must derive them from the autogenerated child rules; the rule is deleted at the end (cascade removes the children).")]
	[AllureTag(ReadEntityBusinessRuleTool.ToolName)]
	[AllureName("apply-filter clearValue/populateValue flags survive the create-read round-trip in Creatio")]
	public async Task BusinessRulesRead_Should_RoundTrip_ApplyFilter_Flags_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(10));
		string suffix = Guid.NewGuid().ToString("N");
		string ruleName = $"UsrMcpE2EFilter{suffix}";

		// Act - create apply-filter City by Country with both flags true
		CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "Contact",
					["rules"] = new object[] {
						new Dictionary<string, object?> {
							["caption"] = $"MCP E2E filter diag {suffix}",
							["name"] = ruleName,
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
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse createResponse = McpCommandExecutionParser.ExtractBusinessRuleBatchResponse(createResult);

		// Assert - create
		createResponse.Succeeded.Should().Be(1,
			because: "the apply-filter rule with both flags true should be created in the configured Creatio sandbox");

		// Act - read back the created rule
		BusinessRulesReadResponse readResponse = await ReadEntityRulesAsync(
			arrangeContext.Session, environmentName, packageName, "Contact", arrangeContext.CancellationTokenSource.Token);
		BusinessRule model = readResponse.Rules.Single(rule =>
			string.Equals(rule.Name, ruleName, StringComparison.OrdinalIgnoreCase));

		// Assert - the flags must survive the create → persist → read round-trip even though the
		// platform drops the designer flags from the persisted action (derived from child rules)
		ApplyFilterBusinessRuleAction action = model.Actions.OfType<ApplyFilterBusinessRuleAction>().Single();
		action.ClearValue.Should().BeTrue(
			because: "clearValue true was sent on create and its autogenerated child rule proves the behavior");
		action.PopulateValue.Should().BeTrue(
			because: "populateValue true was sent on create and its autogenerated child rule proves the behavior");

		// Act - delete to clean up (cascade removes the autogenerated children)
		CallToolResult deleteResult = await arrangeContext.Session.CallToolAsync(
			DeleteEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = "Contact",
					["rule-names"] = new object[] { ruleName }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse deleteResponse =
			EntitySchemaStructuredResultParser.Extract<BusinessRuleBatchResponse>(deleteResult);

		// Assert - delete
		deleteResponse.Succeeded.Should().Be(1,
			because: "the round-trip rule should be removed so the sandbox stays clean");
	}

	private static async Task<BusinessRulesReadResponse> ReadEntityRulesAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string entitySchemaName,
		CancellationToken cancellationToken) {
		CallToolResult readResult = await session.CallToolAsync(
			ReadEntityBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["entity-schema-name"] = entitySchemaName
				}
			},
			cancellationToken);
		readResult.IsError.Should().NotBeTrue(
			because: "reading entity business rules on a reachable environment should return the structured read response, not an MCP error");
		BusinessRulesReadResponse response =
			EntitySchemaStructuredResultParser.Extract<BusinessRulesReadResponse>(readResult);
		response.Error.Should().BeNull(
			because: "reading entity business rules on a reachable environment should not fail at request level");
		return response;
	}

	private static BusinessRule GetRuleByName(BusinessRulesReadResponse response, string ruleName) =>
		response.Rules.Should().ContainSingle(
				rule => string.Equals(rule.Name, ruleName, StringComparison.OrdinalIgnoreCase),
				because: "the read response should contain exactly one rule with the expected internal name")
			.Which;

	private static IReadOnlyDictionary<string, object?> CreateNamedContactConstRule(
		string caption,
		string ruleName,
		string constValue) =>
		new Dictionary<string, object?> {
			["caption"] = caption,
			["name"] = ruleName,
			["enabled"] = true,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("Name"),
						["comparisonType"] = "equal",
						["rightExpression"] = new Dictionary<string, object?> {
							["type"] = "Const",
							["value"] = constValue
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

	private static IReadOnlyDictionary<string, object?> CreateContactConstRuleUpdate(
		string caption,
		string? ruleName,
		string constValue,
		EntityRuleBlockIds? blockIds,
		bool? enabled) {
		Dictionary<string, object?> leftExpression = new() {
			["type"] = "AttributeValue",
			["path"] = "Name"
		};
		Dictionary<string, object?> rightExpression = new() {
			["type"] = "Const",
			["value"] = constValue
		};
		Dictionary<string, object?> condition = new() {
			["leftExpression"] = leftExpression,
			["comparisonType"] = "equal",
			["rightExpression"] = rightExpression
		};
		Dictionary<string, object?> action = new() {
			["type"] = "make-read-only",
			["items"] = new object[] { "Name" }
		};
		if (blockIds is not null) {
			condition["uId"] = blockIds.ConditionUId;
			leftExpression["uId"] = blockIds.LeftExpressionUId;
			rightExpression["uId"] = blockIds.RightExpressionUId;
			action["uId"] = blockIds.ActionUId;
		}
		Dictionary<string, object?> rule = new() {
			["caption"] = caption,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] { condition }
			},
			["actions"] = new object[] { action }
		};
		if (ruleName is not null) {
			rule["name"] = ruleName;
		}
		if (enabled is not null) {
			rule["enabled"] = enabled;
		}
		return rule;
	}

	private sealed record EntityRuleBlockIds(
		string ConditionUId,
		string LeftExpressionUId,
		string RightExpressionUId,
		string ActionUId);

}
