using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the page business-rule MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreatePageBusinessRuleTool.BusinessRuleCreateToolName)]
[NonParallelizable]
public sealed class PageBusinessRuleToolE2ETests : McpContractFixtureBase {
	private const string ToolName = CreatePageBusinessRuleTool.BusinessRuleCreateToolName;

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes a page-only action contract for create-page-business-rules through the full get-tool-contract payload on the lazy tool surface.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool exposes a page-only action contract")]
	[AllureDescription("Starts the real clio MCP server, expands the create-page-business-rules contract via get-tool-contract, and verifies the action enum validator lists only page actions.")]
	public async Task BusinessRuleCreate_Should_Advertise_Page_Only_Action_Runtime_Schema() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		// The tool is a hidden long-tail tool on the lazy surface, so its raw polymorphic anyOf runtime
		// schema is no longer advertised through tools/list; the equivalent page-only action guarantee is
		// carried by the curated contract's action enum validator returned by get-tool-contract.
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = new[] { ToolName } }
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		ToolContractDefinition contract = contracts.Tools.Should().ContainSingle(tool => tool.Name == ToolName,
			because: "get-tool-contract must expand the create-page-business-rules contract on the lazy surface")
			.Which;
		ToolContractValidator actionValidator = contract.InputSchema.Validators.Should()
			.ContainSingle(validator => validator.Code == "unsupported-action",
				because: "the page business-rule contract must declare the enum validator that gates action types")
			.Which;
		actionValidator.Context.Should().Contain("hide-element",
			because: "page-only visibility actions must be listed as supported page action types");
		actionValidator.Context.Should().Contain("show-element",
			because: "page-only visibility actions must be listed as supported page action types");
		actionValidator.Context.Should().NotContain("set-values",
			because: "entity-only actions should not appear in the page business-rule action contract");
		actionValidator.Context.Should().NotContain("apply-filter,",
			because: "entity-only filter actions should not appear in the page business-rule action contract");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a show-element page business-rule payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds show/hide payloads")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rule with a show-element payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_ShowElement_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-business-rule-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rules"] = new object[] { CreateShowElementRule()
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid show-element payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the page action payload");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a system-variable page condition payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds system-variable conditions")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rule with a SysValue condition payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_SysValue_Condition_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-sys-value-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rules"] = new object[] { CreateSysValueConditionRule()
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid system-variable page condition payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the SysValue page payload");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a data-source-scoped condition payload (a '<dataSource>.<column>' path) through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds data-source-scoped condition paths")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rules with a condition comparing two '<dataSource>.<column>' paths and an intentionally missing environment, then verifies the scoped payload binds and reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_Datasource_Scoped_Condition_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-scoped-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rules"] = new object[] { CreateDatasourceScopedConditionRule() }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a condition using '<dataSource>.<column>' paths should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the data-source-scoped page payload");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a page-parameter condition payload (a 'PageParameters.<name>' path) through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds page-parameter condition paths")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rules with a condition comparing a 'PageParameters.<name>' path and an intentionally missing environment, then verifies the page-parameter payload binds and reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_Page_Parameter_Condition_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-parameter-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rules"] = new object[] { CreatePageParameterConditionRule() }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a condition using a 'PageParameters.<name>' path should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the page-parameter payload");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a show-element page business-rule gated on an is-filled-in condition (unary; no rightExpression) through the real MCP server and reports an invalid environment failure from command execution. Pins the ENG-92154 fix path: 'hide/show an element until a field is entered' is a page business rule, not a visible-bound-attribute handler.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds an is-filled-in show/hide payload")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rules with a show-element action gated on comparisonType is-filled-in and an intentionally missing environment, then verifies the unary-comparison payload (no rightExpression) binds and the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_IsFilledIn_ShowElement_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-is-filled-in-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrLoan_FormPage",
					["rules"] = new object[] { CreateShowWhenFilledRule() }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a show-element payload gated on is-filled-in (a unary comparison with no rightExpression) should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the is-filled-in page payload");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a multi-rule page batch payload through the real MCP server and reports an invalid environment failure for the whole batch.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds a multi-rule batch")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rules with two rules in one call and an intentionally missing environment, then verifies the multi-element rules array binds and the structured response references the missing environment instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_Multiple_Rules_Batch_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-batch-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rules"] = new object[] { CreateShowElementRule(), CreateSysValueConditionRule() }
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid two-rule page batch payload should bind and return the structured batch response, not an MCP binding error");
		callResult.Content!.Select(content => content.ToString()).Should().Contain(message =>
				ContainsText(message, invalidEnvironmentName),
			because: "the whole batch fails on the missing environment, so the structured response should reference it");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a read-page-business-rules request through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ReadPageBusinessRuleTool.ToolName)]
	[AllureName("Page business-rule read MCP tool reports an invalid environment")]
	[AllureDescription("Starts the real clio MCP server, calls read-page-business-rules with an intentionally missing environment, then verifies the request reaches command execution and returns the standard command execution envelope referencing the environment.")]
	public async Task BusinessRulesRead_Should_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-read-rules-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ReadPageBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid page read request should bind and return the standard command execution envelope, not an MCP binding error");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from binding the page read arguments");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a named update-page-business-rules payload with block uIds through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(UpdatePageBusinessRuleTool.ToolName)]
	[AllureName("Page business-rule update MCP tool binds named rule payloads")]
	[AllureDescription("Starts the real clio MCP server, calls update-page-business-rules with a rule carrying name, enabled, and block uIds against an intentionally missing environment, then verifies the extended update contract binds and the failure comes from environment resolution.")]
	public async Task BusinessRulesUpdate_Should_Bind_Named_Rule_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-update-rules-env-{Guid.NewGuid():N}";
		PageRuleBlockIds blockIds = new(
			Guid.NewGuid().ToString("D"),
			Guid.NewGuid().ToString("D"),
			Guid.NewGuid().ToString("D"));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			UpdatePageBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rules"] = new object[] {
						CreatePageRuleUpdate(
							"Updated hide rule",
							"BusinessRule_1c48625",
							"PDS_Priority",
							"EscalateButton",
							"is-filled-in",
							blockIds,
							enabled: true)
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a page rule payload with name, enabled, and block uIds should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the named page update payload");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a delete-page-business-rules request through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(DeletePageBusinessRuleTool.ToolName)]
	[AllureName("Page business-rule delete MCP tool reports an invalid environment")]
	[AllureDescription("Starts the real clio MCP server, calls delete-page-business-rules with a rule-names array and an intentionally missing environment, then verifies the request reaches command execution and returns the standard command execution envelope referencing the environment.")]
	public async Task BusinessRulesDelete_Should_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-delete-rules-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			DeletePageBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rule-names"] = new object[] { "BusinessRule_1c48625" }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid page delete request should bind and return the standard command execution envelope, not an MCP binding error");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from binding the rule-names array");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes the page business-rule read, update, and delete maintenance contracts through get-tool-contract on the lazy MCP surface.")]
	[AllureTag(ReadPageBusinessRuleTool.ToolName)]
	[AllureTag(UpdatePageBusinessRuleTool.ToolName)]
	[AllureTag(DeletePageBusinessRuleTool.ToolName)]
	[AllureName("Page business-rule maintenance MCP tools expose contracts through get-tool-contract")]
	[AllureDescription("Starts the real clio MCP server, requests the read-page-business-rules, update-page-business-rules, and delete-page-business-rules contracts via get-tool-contract, and verifies the read output fields, the required name match key for update, and the rule-names delete key are advertised.")]
	public async Task BusinessRulesMaintenance_Should_Advertise_Read_Update_Delete_Contracts() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] {
						ReadPageBusinessRuleTool.ToolName,
						UpdatePageBusinessRuleTool.ToolName,
						DeletePageBusinessRuleTool.ToolName
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		contractResult.IsError.Should().NotBeTrue(
			because: "the registered page maintenance tool contracts should resolve through get-tool-contract without an MCP error");
		contracts.Success.Should().BeTrue(
			because: "all three page business-rule maintenance tools are registered in the contract catalog");
		ToolContractDefinition readContract = contracts.Tools!.Single(tool => tool.Name == ReadPageBusinessRuleTool.ToolName);
		readContract.OutputContract.Fields.Should().Contain(field => field.Name == "count",
			because: "the read contract should advertise the returned rule count output field");
		readContract.OutputContract.Fields.Should().Contain(field =>
				field.Name == "rules" &&
				field.Description.Contains("name", StringComparison.Ordinal),
			because: "the read contract should advertise the rules output items carrying the name match key for update/delete");
		ToolContractDefinition updateContract = contracts.Tools!.Single(tool => tool.Name == UpdatePageBusinessRuleTool.ToolName);
		updateContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "rules" &&
				field.Description.Contains("name (REQUIRED", StringComparison.Ordinal),
			because: "the update contract should advertise name as the required match key inside each replacement rule");
		ToolContractDefinition deleteContract = contracts.Tools!.Single(tool => tool.Name == DeletePageBusinessRuleTool.ToolName);
		deleteContract.InputSchema.Required.Should().Contain("rule-names",
			because: "the delete contract should require the rule-names batch of internal rule names");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Creates a page business rule for Contacts_FormPage in the Custom package through the real MCP server and Creatio environment.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool creates a Contact page rule")]
	[AllureDescription("Requires a reachable sandbox environment and destructive opt-in. Reads Contacts_FormPage, builds a valid page show/hide rule from its bundle, and verifies that Creatio command execution succeeds.")]
	public async Task BusinessRuleCreate_Should_Create_Contact_Page_Rule_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false - skipping destructive create-page-business-rule test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await ClioCliCommandRunner.EnsureCliogateInstalledAsync(settings, environmentName);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		PageRuleTarget target = await ResolvePageRuleTargetAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName);
		// Intentional destructive fixture: Contacts_FormPage in Custom is the canonical sandbox target.
		// There is no business-rule delete action yet, so each run writes a uniquely captioned rule and verifies readback.
		string caption = $"MCP E2E Contact page {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["page-schema-name"] = target.PageSchemaName,
					["rules"] = new object[] { CreateContactPageRule(target, caption)
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse batchResponse = McpCommandExecutionParser.ExtractBusinessRuleBatchResponse(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact page show/hide rule should return the structured batch-create response, not an MCP error");
		batchResponse.Succeeded.Should().Be(1,
			because: "the single Contact page rule should be created in the configured Creatio sandbox");
		batchResponse.Failed.Should().Be(0,
			because: "no rule in the batch should fail when the payload is valid");
		batchResponse.Results.Should().ContainSingle(result => result.Success && !string.IsNullOrWhiteSpace(result.RuleName),
			because: "the per-rule result should report success and the generated internal rule name");
		await BusinessRuleAddonReadback.AssertPageRuleExistsAsync(
			settings,
			environmentName,
			packageName,
			target.RootSchemaUId,
			McpCommandExecutionParser.ExtractBusinessRuleName(batchResponse),
			"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionHideElement",
			[target.ElementName],
			target.AttributeName,
			arrangeContext.CancellationTokenSource.Token);
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Runs the full page business-rule CRUD lifecycle (create with explicit name, read with uIds, update the condition with preserved uIds, disable, delete by name) through the real MCP server and Creatio environment.")]
	[AllureTag(ReadPageBusinessRuleTool.ToolName)]
	[AllureTag(UpdatePageBusinessRuleTool.ToolName)]
	[AllureTag(DeletePageBusinessRuleTool.ToolName)]
	[AllureName("Page business-rule MCP tools complete the CRUD lifecycle")]
	[AllureDescription("Requires a reachable sandbox environment and destructive opt-in. Creates a hide-element rule with an explicit name on a discovered form page, reads it back with block uIds, updates its comparison while passing the uIds back (asserting the change and uId survival), disables the rule, deletes it by name, and verifies the rule is gone on the final read.")]
	public async Task BusinessRules_Should_Complete_Page_Rule_Crud_Lifecycle_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false - skipping destructive page business-rule CRUD lifecycle test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await ClioCliCommandRunner.EnsureCliogateInstalledAsync(settings, environmentName);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(15));
		PageRuleTarget target = await ResolvePageRuleTargetAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName);
		string suffix = Guid.NewGuid().ToString("N");
		string ruleName = $"UsrMcpE2EPageRule{suffix}";
		string caption = $"MCP E2E page CRUD {suffix}";

		// Act - create a rule with an explicit caller-supplied name and enabled true
		CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["page-schema-name"] = target.PageSchemaName,
					["rules"] = new object[] {
						CreateNamedPageRule(caption, ruleName, target.AttributeName, target.ElementName, "is-filled-in")
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse createResponse = McpCommandExecutionParser.ExtractBusinessRuleBatchResponse(createResult);

		// Assert - create
		createResult.IsError.Should().NotBeTrue(
			because: "a valid named page hide-element rule should return the structured batch-create response, not an MCP error");
		createResponse.Succeeded.Should().Be(1,
			because: "the single named page rule should be created in the configured Creatio sandbox");
		createResponse.Failed.Should().Be(0,
			because: "no rule in the batch should fail when the payload is valid");
		createResponse.Results.Should().ContainSingle(result => result.Success && result.RuleName == ruleName,
			because: "create should honor the explicit caller-supplied rule name instead of generating one");

		// Act - read after create
		BusinessRulesReadResponse readAfterCreate = await ReadPageRulesAsync(
			arrangeContext.Session,
			environmentName,
			packageName,
			target.PageSchemaName,
			arrangeContext.CancellationTokenSource.Token);

		// Assert - read returns the rule in contract shape with block uIds
		BusinessRule createdModel = GetRuleByName(readAfterCreate, ruleName);
		createdModel.Enabled.Should().BeTrue(
			because: "the rule was created with enabled true");
		BusinessRuleCondition createdCondition = createdModel.Condition.Conditions.Should().ContainSingle(
				because: "the created page rule has exactly one condition")
			.Which;
		createdCondition.ComparisonType.Should().Be("is-filled-in",
			because: "read should return the comparison the rule was created with");
		createdCondition.UId.Should().NotBeNullOrWhiteSpace(
			because: "read should return the stable condition uId for update round-trips");
		createdCondition.LeftExpression.UId.Should().NotBeNullOrWhiteSpace(
			because: "read should return the stable left expression uId for update round-trips");
		BusinessRuleAction createdAction = createdModel.Actions.Should().ContainSingle(
				because: "the created page rule has exactly one action")
			.Which;
		createdAction.UId.Should().NotBeNullOrWhiteSpace(
			because: "read should return the stable action uId for update round-trips");
		PageRuleBlockIds blockIds = new(
			createdCondition.UId!,
			createdCondition.LeftExpression.UId!,
			createdAction.UId!);

		// Act - update the condition comparison while passing the block uIds back
		CallToolResult updateResult = await arrangeContext.Session.CallToolAsync(
			UpdatePageBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["page-schema-name"] = target.PageSchemaName,
					["rules"] = new object[] {
						CreatePageRuleUpdate(
							caption,
							ruleName,
							target.AttributeName,
							target.ElementName,
							"is-not-filled-in",
							blockIds,
							enabled: null)
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse updateResponse =
			EntitySchemaStructuredResultParser.Extract<BusinessRuleBatchResponse>(updateResult);

		// Assert - update
		updateResult.IsError.Should().NotBeTrue(
			because: "a valid page rule update should return the structured batch response, not an MCP error");
		updateResponse.Succeeded.Should().Be(1,
			because: "the update targets the existing page rule by name and should save");
		updateResponse.Failed.Should().Be(0,
			because: "no rule in the update batch should fail");

		// Act - read after update
		BusinessRulesReadResponse readAfterUpdate = await ReadPageRulesAsync(
			arrangeContext.Session,
			environmentName,
			packageName,
			target.PageSchemaName,
			arrangeContext.CancellationTokenSource.Token);

		// Assert - the comparison changed, the caller-supplied uIds survived, enabled stayed true
		BusinessRule updatedModel = GetRuleByName(readAfterUpdate, ruleName);
		updatedModel.Enabled.Should().BeTrue(
			because: "enabled was omitted on update, so the existing enabled value should be preserved");
		BusinessRuleCondition updatedCondition = updatedModel.Condition.Conditions.Should().ContainSingle(
				because: "the updated page rule still has exactly one condition")
			.Which;
		updatedCondition.ComparisonType.Should().Be("is-not-filled-in",
			because: "the update should replace the condition comparison with the new one");
		updatedCondition.UId.Should().Be(blockIds.ConditionUId,
			because: "the caller-supplied condition uId should survive the update");
		updatedCondition.LeftExpression.UId.Should().Be(blockIds.LeftExpressionUId,
			because: "the caller-supplied left expression uId should survive the update");
		updatedModel.Actions.Should().ContainSingle(
				because: "the updated page rule still has exactly one action")
			.Which.UId.Should().Be(blockIds.ActionUId,
				because: "the caller-supplied action uId should survive the update");

		// Act - disable the rule via update with enabled false
		CallToolResult disableResult = await arrangeContext.Session.CallToolAsync(
			UpdatePageBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["page-schema-name"] = target.PageSchemaName,
					["rules"] = new object[] {
						CreatePageRuleUpdate(
							caption,
							ruleName,
							target.AttributeName,
							target.ElementName,
							"is-not-filled-in",
							blockIds,
							enabled: false)
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse disableResponse =
			EntitySchemaStructuredResultParser.Extract<BusinessRuleBatchResponse>(disableResult);

		// Assert - disable
		disableResponse.Succeeded.Should().Be(1,
			because: "the disable update targets the existing page rule by name and should save");
		disableResponse.Failed.Should().Be(0,
			because: "no rule in the disable batch should fail");
		BusinessRulesReadResponse readAfterDisable = await ReadPageRulesAsync(
			arrangeContext.Session,
			environmentName,
			packageName,
			target.PageSchemaName,
			arrangeContext.CancellationTokenSource.Token);
		GetRuleByName(readAfterDisable, ruleName).Enabled.Should().BeFalse(
			because: "the update with enabled false should deactivate the page rule");

		// Act - delete the rule by name
		CallToolResult deleteResult = await arrangeContext.Session.CallToolAsync(
			DeletePageBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["page-schema-name"] = target.PageSchemaName,
					["rule-names"] = new object[] { ruleName }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse deleteResponse =
			EntitySchemaStructuredResultParser.Extract<BusinessRuleBatchResponse>(deleteResult);

		// Assert - delete
		deleteResult.IsError.Should().NotBeTrue(
			because: "a valid page rule delete should return the structured batch response, not an MCP error");
		deleteResponse.Succeeded.Should().Be(1,
			because: "the single existing page rule should be deleted");
		deleteResponse.Failed.Should().Be(0,
			because: "no name in the delete batch should fail");
		deleteResponse.Results.Should().ContainSingle(result => result.Success,
			because: "the per-name result should report the successful deletion");

		// Act - read after delete
		BusinessRulesReadResponse readAfterDelete = await ReadPageRulesAsync(
			arrangeContext.Session,
			environmentName,
			packageName,
			target.PageSchemaName,
			arrangeContext.CancellationTokenSource.Token);

		// Assert - the deleted rule is gone
		readAfterDelete.Rules.Should().NotContain(
			rule => string.Equals(rule.Name, ruleName, StringComparison.OrdinalIgnoreCase),
			because: "the deleted page rule should no longer be returned by read");
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
			Assert.Ignore("create-page-business-rule MCP E2E requires McpE2E:Sandbox:EnvironmentName for destructive tests.");
			return string.Empty;
		}

		if (await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		Assert.Ignore(
			$"create-page-business-rule MCP E2E requires a reachable configured sandbox environment. Environment '{configuredEnvironmentName}' was not reachable.");
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

	private static async Task<PageRuleTarget> ResolvePageRuleTargetAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		// The rule needs a real Freedom UI form page bound to an entity datasource. A base-Contact
		// page (Contacts_FormPage) only ships with CRM editions, so a bare Studio sandbox has none.
		// Discover any seeded custom form page instead (the AutoTest seed installs several) and build
		// the rule on the first one that exposes a datasource-bound attribute, preferring a
		// Contact-bound page so the test keeps its original Contact intent where the stand provides one.
		foreach (string candidate in await ResolveCandidatePageSchemaNamesAsync(
			session, cancellationToken, environmentName)) {
			PageRuleTarget? target = await TryResolvePageRuleTargetAsync(session, cancellationToken, environmentName, candidate);
			if (target is not null) {
				return target;
			}
		}

		Assert.Ignore(
			$"No seeded Freedom UI form page with a datasource-bound attribute was found on environment '{environmentName}'. " +
			"Ensure cliogate is installed and the AutoTest seed (or a Contact form page) is available in the sandbox before running this test.");
		return null!;
	}

	private static async Task<IReadOnlyList<string>> ResolveCandidatePageSchemaNamesAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		// Seeded AutoTestClioMcp_FormPage first — present on the clio MCP e2e stands including bare
		// Studio (its schema lives in a file-installed package, which the page-rule add-on flow now
		// handles by resolving the add-on in the requested writable package). Then base-Contact names
		// for CRM stands, then any other seeded custom form page. The target attribute and element are
		// resolved from the page bundle below, so no columns or controls are assumed on the page.
		List<string> candidates = ["AutoTestClioMcp_FormPage", "Contacts_FormPage", "Contact_FormPage"];

		CallToolResult listResult = await session.CallToolAsync(
			PageListTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["search-pattern"] = "Usr",
					["limit"] = 200,
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		PageListResponse pages = EntitySchemaStructuredResultParser.Extract<PageListResponse>(listResult);
		if (pages.Success && pages.Pages is not null) {
			// Prefer Contact-named pages so the rule stays Contact-bound where the seed provides one,
			// then fall back to any other seeded form page.
			IEnumerable<string> seededFormPages = pages.Pages
				.Select(page => page.SchemaName)
				.Where(name => !string.IsNullOrWhiteSpace(name)
					&& name.EndsWith("_FormPage", StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(name => name.Contains("Contact", StringComparison.OrdinalIgnoreCase));
			foreach (string name in seededFormPages) {
				if (!candidates.Contains(name, StringComparer.OrdinalIgnoreCase)) {
					candidates.Add(name);
				}
			}
		}

		return candidates;
	}

	private static async Task<PageRuleTarget?> TryResolvePageRuleTargetAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string candidatePageSchemaName) {
		CallToolResult callResult = await session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = candidatePageSchemaName,
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		PageGetResponse response = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(callResult);
		if (!response.Success || response.Files is null || !File.Exists(response.Files.BundleFile)) {
			return null;
		}

		JsonObject bundle = JsonNode.Parse(await File.ReadAllTextAsync(response.Files.BundleFile, cancellationToken))!.AsObject();
		PageAttributeCandidate? attribute = TryResolveRuleAttribute(bundle);
		if (attribute is null) {
			return null;
		}

		string? elementName = TryResolvePageElementName(bundle, attribute.AttributeName);
		if (string.IsNullOrWhiteSpace(elementName)) {
			return null;
		}

		return new PageRuleTarget(candidatePageSchemaName, response.Page.RootSchemaUId, attribute.AttributeName, elementName);
	}

	// Returns a datasource-bound attribute to drive the rule condition, or null when the page exposes none.
	// Preference: a Contact "Name" attribute (original intent) -> any Contact attribute -> any datasource-bound
	// attribute on a non-Contact seeded page. Never asserts so the caller can skip to the next candidate page.
	private static PageAttributeCandidate? TryResolveRuleAttribute(JsonObject bundle) {
		JsonObject attributes = bundle["viewModelConfig"]?["attributes"] as JsonObject ?? [];
		JsonObject dataSources = bundle["modelConfig"]?["dataSources"] as JsonObject ?? [];
		List<PageAttributeCandidate> contactCandidates = [];
		List<PageAttributeCandidate> anyCandidates = [];
		foreach ((string attributeName, JsonNode? attributeNode) in attributes) {
			if (attributeNode is not JsonObject attribute
				|| !TryResolveDatasourcePath(attribute, out string datasourceName, out string columnName)) {
				continue;
			}
			PageAttributeCandidate candidate = new(attributeName, columnName);
			anyCandidates.Add(candidate);
			if (IsContactDatasource(dataSources, datasourceName)) {
				contactCandidates.Add(candidate);
			}
		}

		static PageAttributeCandidate? PreferName(List<PageAttributeCandidate> source) =>
			source.FirstOrDefault(candidate =>
				string.Equals(candidate.ColumnName, "Name", StringComparison.OrdinalIgnoreCase))
			?? source.FirstOrDefault();

		return PreferName(contactCandidates) ?? PreferName(anyCandidates);
	}

	// Returns the page element to hide, or null when the viewConfig exposes no named element.
	private static string? TryResolvePageElementName(JsonObject bundle, string attributeName) {
		string? boundElement = EnumerateObjects(bundle["viewConfig"])
			.FirstOrDefault(obj => string.Equals(obj["control"]?.GetValue<string>(), $"${attributeName}", StringComparison.Ordinal))
			?["name"]?.GetValue<string>();
		if (!string.IsNullOrWhiteSpace(boundElement)) {
			return boundElement;
		}

		return EnumerateObjects(bundle["viewConfig"])
			.Select(obj => obj["name"]?.GetValue<string>())
			.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
	}

	private static IEnumerable<JsonObject> EnumerateObjects(JsonNode? node) {
		Stack<JsonNode?> pending = new();
		pending.Push(node);
		while (pending.Count > 0) {
			JsonNode? current = pending.Pop();
			switch (current) {
				case JsonObject obj:
					yield return obj;
					foreach (KeyValuePair<string, JsonNode?> property in obj) {
						pending.Push(property.Value);
					}
					break;
				case JsonArray array:
					foreach (JsonNode? item in array) {
						pending.Push(item);
					}
					break;
			}
		}
	}

	private static bool TryResolveDatasourcePath(
		JsonObject attribute,
		out string datasourceName,
		out string columnName) {
		datasourceName = string.Empty;
		columnName = string.Empty;
		string path = attribute["modelConfig"]?["path"]?.GetValue<string>() ?? string.Empty;
		string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length != 2) {
			return false;
		}
		datasourceName = parts[0];
		columnName = parts[1];
		return true;
	}

	private static bool IsContactDatasource(JsonObject dataSources, string datasourceName) =>
		string.Equals(
			dataSources[datasourceName]?["config"]?["entitySchemaName"]?.GetValue<string>(),
			"Contact",
			StringComparison.Ordinal);

	private static IReadOnlyDictionary<string, object?> CreateContactPageRule(PageRuleTarget target, string caption) =>
		new Dictionary<string, object?> {
			["caption"] = caption,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression(target.AttributeName),
						["comparisonType"] = "is-filled-in"
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "hide-element",
					["items"] = new object[] { target.ElementName }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateShowElementRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Show escalation for high priority",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("PDS_Priority"),
						["comparisonType"] = "equal",
						["rightExpression"] = new Dictionary<string, object?> {
							["type"] = "Const",
							["value"] = "High"
						}
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "show-element",
					["items"] = new object[] { "EscalateButton" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateSysValueConditionRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Hide reminder when due on or before today",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("PDS_UsrDueDate"),
						["comparisonType"] = "less-than-or-equal",
						["rightExpression"] = new Dictionary<string, object?> {
							["type"] = "SysValue",
							["sysValueName"] = "CurrentDate"
						}
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "hide-element",
					["items"] = new object[] { "ReminderLabel" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateDatasourceScopedConditionRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Show name when modified after created",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("PDS.ModifiedOn"),
						["comparisonType"] = "greater-than",
						["rightExpression"] = CreateAttributeExpression("PDS.CreatedOn")
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "show-element",
					["items"] = new object[] { "Name" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreatePageParameterConditionRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Show label when partner offer flag is set",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("PageParameters.UsrHasPartnerOffer"),
						["comparisonType"] = "equal",
						["rightExpression"] = new Dictionary<string, object?> {
							["type"] = "Const",
							["dataValueTypeName"] = "Boolean",
							["value"] = true
						}
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "show-element",
					["items"] = new object[] { "Name" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateShowWhenFilledRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Show calculations when loan amount is entered",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("PDS_UsrLoanAmount"),
						// is-filled-in / is-not-filled-in are unary comparisons: no rightExpression (ENG-92154).
						["comparisonType"] = "is-filled-in"
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "show-element",
					["items"] = new object[] { "LoanCalculationsHeader", "UsrFeeDisplayLabel", "UsrTotalDisplayLabel" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateAttributeExpression(string path) =>
		new Dictionary<string, object?> {
			["type"] = "AttributeValue",
			["path"] = path
		};

	private static async Task<BusinessRulesReadResponse> ReadPageRulesAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string pageSchemaName,
		CancellationToken cancellationToken) {
		CallToolResult readResult = await session.CallToolAsync(
			ReadPageBusinessRuleTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["page-schema-name"] = pageSchemaName
				}
			},
			cancellationToken);
		readResult.IsError.Should().NotBeTrue(
			because: "reading page business rules on a reachable environment should return the structured read response, not an MCP error");
		BusinessRulesReadResponse response =
			EntitySchemaStructuredResultParser.Extract<BusinessRulesReadResponse>(readResult);
		response.Error.Should().BeNull(
			because: "reading page business rules on a reachable environment should not fail at request level");
		return response;
	}

	private static BusinessRule GetRuleByName(BusinessRulesReadResponse response, string ruleName) =>
		response.Rules.Should().ContainSingle(
				rule => string.Equals(rule.Name, ruleName, StringComparison.OrdinalIgnoreCase),
				because: "the read response should contain exactly one rule with the expected internal name")
			.Which;

	private static IReadOnlyDictionary<string, object?> CreateNamedPageRule(
		string caption,
		string ruleName,
		string attributeName,
		string elementName,
		string comparisonType) =>
		new Dictionary<string, object?> {
			["caption"] = caption,
			["name"] = ruleName,
			["enabled"] = true,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression(attributeName),
						["comparisonType"] = comparisonType
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "hide-element",
					["items"] = new object[] { elementName }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreatePageRuleUpdate(
		string caption,
		string ruleName,
		string attributeName,
		string elementName,
		string comparisonType,
		PageRuleBlockIds? blockIds,
		bool? enabled) {
		Dictionary<string, object?> leftExpression = new() {
			["type"] = "AttributeValue",
			["path"] = attributeName
		};
		Dictionary<string, object?> condition = new() {
			["leftExpression"] = leftExpression,
			["comparisonType"] = comparisonType
		};
		Dictionary<string, object?> action = new() {
			["type"] = "hide-element",
			["items"] = new object[] { elementName }
		};
		if (blockIds is not null) {
			condition["uId"] = blockIds.ConditionUId;
			leftExpression["uId"] = blockIds.LeftExpressionUId;
			action["uId"] = blockIds.ActionUId;
		}
		Dictionary<string, object?> rule = new() {
			["caption"] = caption,
			["name"] = ruleName,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] { condition }
			},
			["actions"] = new object[] { action }
		};
		if (enabled is not null) {
			rule["enabled"] = enabled;
		}
		return rule;
	}

	private sealed record PageRuleBlockIds(
		string ConditionUId,
		string LeftExpressionUId,
		string ActionUId);

	private sealed record PageAttributeCandidate(string AttributeName, string ColumnName);

	private sealed record PageRuleTarget(string PageSchemaName, string RootSchemaUId, string AttributeName, string ElementName);
}
