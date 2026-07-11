using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ToolContractGetToolTests {
	// Builds the get-tool-contract tool over the REAL invoker registry so contracts for uncurated tools
	// derive from the same MCP tool input schema clio-run dispatches against (Codex review #1, story-6).
	private static ToolContractGetTool BuildToolWithRegistry() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		McpToolInvokerRegistry registry = new(
			provider,
			typeof(SchemaSyncTool).Assembly,
			featureToggle,
			JsonSerializerOptions.Default);
		return new ToolContractGetTool(registry);
	}
	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for get-tool-contract.")]
	public void ToolContractGet_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ToolContractGetTool)
			.GetMethod(nameof(ToolContractGetTool.GetToolContracts))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ToolContractGetTool.ToolName,
			because: "the MCP tool name must stay stable for clients that bootstrap from the contract tool");
	}

	// Codex review #1 (PR #743): in lazy mode the long tail is hidden from tools/list but stays
	// invokable via clio-run / clio-run-destructive. The review claimed high-impact hidden tools have
	// NO discoverable contract. They DO: get-tool-contract resolves a contract entry for every one
	// (curated, or auto-generated from the tool input schema). This guard refutes the "no contract"
	// claim and pins it against regression.
	[Test]
	[Category("Unit")]
	[TestCase("stop-creatio")]
	[TestCase("stop-all-creatio")]
	[TestCase("clear-redis-db-by-environment")]
	[TestCase("restart-by-environment-name")]
	[TestCase("uninstall-creatio")]
	[TestCase("sync-schemas")]
	[TestCase("sync-pages")]
	[TestCase("odata-read")]
	[TestCase("execute-esq")]
	[TestCase("create-user-task")]
	[TestCase("create-entity-business-rules")]
	[Description("Every hidden, clio-run-invokable tool resolves to a contract entry with a schema via get-tool-contract, so a lazy-mode client always has something to inspect before dispatch.")]
	public void ToolContractGet_Should_ResolveContract_ForHiddenInvokableTool(string toolName) {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([toolName]));

		// Assert
		result.Success.Should().BeTrue(
			because: $"'{toolName}' is invokable via clio-run and must expose a discoverable contract in lazy mode");
		result.Tools.Should().NotBeNullOrEmpty(
			because: "a known invokable tool must return a contract entry, not an empty result");
		ToolContractDefinition entry = result.Tools!.Single();
		entry.Name.Should().Be(toolName, because: "the returned contract must be for the requested tool");
		entry.InputSchema.Should().NotBeNull(because: "the contract must carry an argument schema object");
	}

	// Arg-bearing tools must expose a USABLE (non-empty) property schema, not an empty fallback.
	// story-6 (Codex review #1, gap closed): uncurated contracts are now derived from the SAME registered
	// MCP tool input schema clio-run dispatches against. The single-scalar env tools (stop-creatio,
	// clear-redis-db-by-environment, restart-by-environment-name) — which the lossy reflection fallback
	// dropped to an EMPTY property list — now expose their real `environmentName` property, so they are
	// merged into this non-empty coverage set alongside the richer arg-bearing tools.
	[Test]
	[Category("Unit")]
	[TestCase("stop-creatio")]
	[TestCase("clear-redis-db-by-environment")]
	[TestCase("restart-by-environment-name")]
	[TestCase("sync-schemas")]
	[TestCase("sync-pages")]
	[TestCase("odata-read")]
	[TestCase("execute-esq")]
	[TestCase("create-user-task")]
	[TestCase("create-entity-business-rules")]
	[Description("An arg-bearing hidden tool exposes a non-empty property schema via get-tool-contract, so the client can build a valid clio-run call.")]
	public void ToolContractGet_Should_ReturnNonEmptyProperties_ForArgBearingHiddenTool(string toolName) {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([toolName]));

		// Assert
		result.Tools.Should().NotBeNullOrEmpty(because: "a known invokable tool must return a contract entry");
		result.Tools!.Single().InputSchema.Properties.Should().NotBeEmpty(
			because: "an arg-bearing tool must expose a usable property schema, not an empty fallback");
	}

	// Pins the Codex #1 fix: the uncurated contract for a single-scalar env tool now derives from the real
	// dispatched MCP input schema, exposing the `environmentName` property the lossy reflection fallback
	// dropped. This is the exact mismatch the review flagged — advertised contract vs what clio-run accepts.
	[Test]
	[Category("Unit")]
	[Description("get-tool-contract derives stop-creatio's contract from the real dispatched MCP input schema and exposes the environmentName property, matching what clio-run actually accepts.")]
	public void ToolContractGet_Should_ExposeEnvironmentNameProperty_ForStopCreatio() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(["stop-creatio"]));

		// Assert
		result.Success.Should().BeTrue(
			because: "stop-creatio is invokable via clio-run-destructive and must expose a discoverable contract");
		ToolContractDefinition entry = result.Tools!.Single();
		entry.InputSchema.Properties.Select(property => property.Name).Should().Contain("environmentName",
			because: "the contract must derive from the real dispatched MCP schema, which carries the environmentName argument clio-run binds");
		entry.InputSchema.Required.Should().Contain("environmentName",
			because: "environmentName is marked [Required] on the tool method, so the derived contract must mark it required");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical clio MCP full contract set when the request omits tool names and asks for detail=full (legacy back-compat behavior).")]
	public void ToolContractGet_Should_Return_Canonical_Contracts_When_Request_Is_Empty_And_DetailIsFull() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(Detail: "full"));

		// Assert
		result.Success.Should().BeTrue(
			because: "an empty request with detail=full should still be a valid bootstrap entry point for canonical full-contract discovery");
		result.Error.Should().BeNull(
			because: "a successful bootstrap lookup should not return a structured error");
		result.Index.Should().BeNull(
			because: "detail=full preserves the legacy behavior and must not emit the compact index alongside the full contracts");
		result.Tools.Should().NotBeNull(
			because: "the detail=full bootstrap response should include the canonical full contract set");
		result.Tools!.Select(contract => contract.Name).Should().Contain([
				GuidanceGetTool.ToolName,
				ExecuteEsqTool.ToolName,
				SettingsHealthTool.ToolName,
				ApplicationGetListTool.ApplicationGetListToolName,
				ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
				ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName,
				CreateEntityBusinessRuleTool.BusinessRuleCreateToolName,
				CreatePageBusinessRuleTool.BusinessRuleCreateToolName,
				DataForgeTool.DataForgeContextToolName,
				ODataReadTool.ToolName,
				PageSyncTool.ToolName,
				PageUpdateTool.ToolName,
				ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
				SchemaNamePrefixTool.GetSchemaNamePrefixToolName
			],
			because: "the canonical contract set should include bootstrap diagnostics, read-only Data Forge discovery/context tools, the key existing-app discovery and page mutation tools, and the prefix discovery tool");
		result.Tools!.Select(contract => contract.Name).Should().NotContain([
				DataForgeTool.DataForgeInitializeToolName,
				DataForgeTool.DataForgeUpdateToolName
			],
			because: "destructive Data Forge maintenance tools should stay available only through explicit contract lookup rather than the default bootstrap set");
		result.Tools!.Select(contract => contract.Name).Should().NotContain(ToolContractGetTool.ToolName,
			because: "get-tool-contract should not include itself in the default returned contract set");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a compact index of all canonical tools (names + one-line purpose, Tools null) when the request omits tool names and detail.")]
	public void ToolContractGet_Should_Return_Compact_Index_When_Request_Is_Empty() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Success.Should().BeTrue(
			because: "a no-args request is the default compact-discovery entry point");
		result.Error.Should().BeNull(
			because: "a successful index lookup should not return a structured error");
		result.Tools.Should().BeNull(
			because: "the compact index must NOT pay for the heavy full contracts — that is the whole point of defer_loading");
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index so an agent can see what tools exist");
		result.Index!.Select(entry => entry.Name).Should().Contain([
				GuidanceGetTool.ToolName,
				ExecuteEsqTool.ToolName,
				SettingsHealthTool.ToolName,
				ApplicationGetListTool.ApplicationGetListToolName,
				CreateEntityBusinessRuleTool.BusinessRuleCreateToolName,
				PageSyncTool.ToolName,
				PageUpdateTool.ToolName,
				ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
				SchemaNamePrefixTool.GetSchemaNamePrefixToolName
			],
			because: "the compact index must cover the same canonical tool surface the full default used to expose");
		result.Index!.Select(entry => entry.Name).Should().NotContain(ToolContractGetTool.ToolName,
			because: "get-tool-contract should not index itself in the default discovery set");
		result.Index!.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.Purpose),
			because: "every index entry must carry a non-empty one-line purpose so the agent can choose a tool without the full schema");
		result.Index!.Should().OnlyContain(entry => entry.Purpose.Length <= 120,
			because: "the purpose must be truncated to a single short line to keep the index cheap");
		result.Index!.Should().OnlyContain(entry => entry.ContractAvailable,
			because: "every canonical index entry has a full curated contract reachable by naming the tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the compact index (Index populated, Tools null) when called with a null args object, proving the no-arguments discovery call is null-safe and yields the index path.")]
	public void ToolContractGet_Should_Return_Compact_Index_When_Args_Is_Null() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(null);

		// Assert
		result.Should().NotBeNull(
			because: "a null args object is the natural no-arguments discovery call and must not throw");
		result.Success.Should().BeTrue(
			because: "the no-arguments discovery call is the default compact-discovery entry point");
		result.Error.Should().BeNull(
			because: "a successful index lookup should not return a structured error");
		result.Tools.Should().BeNull(
			because: "the no-arguments call must yield the compact index, not the heavy full contracts");
		result.Index.Should().NotBeNullOrEmpty(
			because: "a null args object must resolve to the same compact index as an omitted-tool-names call");
		result.Index!.Select(entry => entry.Name).Should().NotContain(ToolContractGetTool.ToolName,
			because: "get-tool-contract should not index itself in the default discovery set");
	}

	[Test]
	[Category("Unit")]
	[Description("Populates the destructive safety flag in the compact index from the MCP tool annotation when an invoker registry is available.")]
	public void ToolContractGet_Should_Populate_Destructive_Flag_In_Index_When_Registry_Available() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		result.Index!.Should().OnlyContain(entry => entry.Destructive != null,
			because: "with an invoker registry the destructive hint is cheaply available for every indexed tool");
		ToolContractIndexEntry executeEsq = result.Index!.Single(entry => entry.Name == ExecuteEsqTool.ToolName);
		executeEsq.Destructive.Should().BeFalse(
			because: "execute-esq is a read-only ESQ query tool annotated as non-destructive");
	}

	// ENG-92761 (F2): the compact index must let an agent tell WHICH tools are called natively (present
	// in tools/list) vs. reached only through clio-run, without depending on an invoker registry.
	[Test]
	[Category("Unit")]
	[Description("A core tool (list-apps) is marked resident=true in the compact index, matching its membership in McpCoreToolProfile.CoreToolTypes.")]
	public void ToolContractGet_Should_MarkResident_True_ForCoreToolInIndex() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		ToolContractIndexEntry listApps = result.Index!.Single(entry => entry.Name == ApplicationGetListTool.ApplicationGetListToolName);
		listApps.Resident.Should().BeTrue(
			because: "list-apps is declared on ApplicationGetListTool, a member of McpCoreToolProfile.CoreToolTypes");
	}

	[Test]
	[Category("Unit")]
	[Description("A long-tail tool (sync-schemas) is marked resident=false in the compact index, matching its absence from McpCoreToolProfile.CoreToolTypes and AlwaysOnLazyToolTypes.")]
	public void ToolContractGet_Should_MarkResident_False_ForLongTailToolInIndex() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		ToolContractIndexEntry syncSchemas = result.Index!.Single(entry => entry.Name == SchemaSyncTool.ToolName);
		syncSchemas.Resident.Should().BeFalse(
			because: "sync-schemas is hidden from tools/list and reachable only via clio-run/clio-run-destructive");
	}

	[Test]
	[Category("Unit")]
	[Description("The always-on lazy-mode executors (clio-run, clio-run-destructive) surface in the compact index as resident=true, matching McpCoreToolProfile.AlwaysOnLazyToolTypes.")]
	public void ToolContractGet_Should_MarkResident_True_ForAlwaysOnExecutorsInIndex() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		result.Index!.Single(entry => entry.Name == ClioRunTool.ToolName).Resident.Should().BeTrue(
			because: "clio-run is one of the always-on lazy-mode executor types");
		result.Index!.Single(entry => entry.Name == ClioRunDestructiveTool.ToolName).Resident.Should().BeTrue(
			because: "clio-run-destructive is one of the always-on lazy-mode executor types");
	}

	[Test]
	[Category("Unit")]
	[Description("get-tool-contract itself is classified resident=true by McpCoreToolProfile even though it is excluded from its own compact index (a tool does not index itself).")]
	public void McpCoreToolProfile_Should_ClassifyGetToolContract_AsResident() {
		// Arrange

		// Act
		bool isResident = McpCoreToolProfile.IsResident(ToolContractGetTool.ToolName);

		// Assert
		isResident.Should().BeTrue(
			because: "get-tool-contract is both a core tool and an always-on lazy-mode discovery surface");
	}

	[Test]
	[Category("Unit")]
	[Description("Adding the resident flag does not disturb the compact index's ordinal name ordering, which must stay deterministic for prompt-cache prefix stability.")]
	public void ToolContractGet_Should_KeepIndexOrdering_Stable_AfterAddingResidentFlag() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		result.Index!.Select(entry => entry.Name).Should().BeInAscendingOrder(StringComparer.Ordinal,
			because: "the index must stay ordinally sorted by name so its prefix is stable for prompt caching");
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes the compact index to materially fewer bytes than the full contract dump, proving the token win.")]
	public void ToolContractGet_Should_Serialize_Index_Much_Smaller_Than_Full_Contracts() {
		// Arrange
		ToolContractGetTool tool = new();
		JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

		// Act
		ToolContractGetResponse indexResult = tool.GetToolContracts(new ToolContractGetArgs());
		ToolContractGetResponse fullResult = tool.GetToolContracts(new ToolContractGetArgs(Detail: "full"));
		int indexLength = JsonSerializer.Serialize(indexResult, options).Length;
		int fullLength = JsonSerializer.Serialize(fullResult, options).Length;

		// Assert
		indexLength.Should().BeLessThan(fullLength / 5,
			because: "the compact index must be a fraction of the full contract payload to justify the defer_loading default");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical compile-creatio contract with explicit preconditions and anti-patterns so callers can decide when compilation is required.")]
	public void ToolContractGet_Should_Return_CompileCreatio_Contract_With_Preconditions_And_AntiPatterns() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			CompileCreatioTool.CompileCreatioToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "compile-creatio is part of the canonical executable contract surface");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(CompileCreatioTool.CompileCreatioToolName);
		contract.Preconditions.Should().NotBeNullOrEmpty(
			because: "the contract must spell out when compilation is actually required");
		contract.Preconditions!.Should().Contain(precondition => precondition.Contains("set-fsm-mode", StringComparison.Ordinal),
			because: "FSM-mode toggles are a canonical trigger for full compilation");
		contract.Preconditions!.Should().Contain(precondition => precondition.Contains("C# schemas", StringComparison.Ordinal),
			because: "C# schema changes are the primary precondition for package compilation");
		contract.AntiPatterns.Should().NotBeNullOrEmpty(
			because: "the contract must call out flows where compilation is never required");
		contract.AntiPatterns!.Should().Contain(pattern => pattern.Pattern.Contains(PageUpdateTool.ToolName, StringComparison.Ordinal),
			because: "page-body edits applied through update-page must never be followed by compile-creatio");
		contract.AntiPatterns!.Should().Contain(pattern => pattern.Pattern.Contains(ApplicationCreateTool.ApplicationCreateToolName, StringComparison.Ordinal),
			because: "create-app never requires a follow-up compilation");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical execute-esq contract with its required inputs, rows/count/success output, and the get-guidance preferred flow.")]
	public void ToolContractGet_Should_Return_ExecuteEsq_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ExecuteEsqTool.ToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "execute-esq is part of the executable clio MCP contract surface and must be discoverable by contract lookup");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(ExecuteEsqTool.ToolName,
			because: "the contract name must stay stable for callers that bootstrap from the contract tool");
		contract.InputSchema.Required.Should().Contain("query",
			because: "execute-esq cannot run without a SelectQuery");
		contract.InputSchema.Required.Should().Contain("environment-name",
			because: "execute-esq must target a registered environment");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "rows",
			because: "a successful query returns the rows array");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "count",
			because: "a successful query reports the number of returned rows");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "success",
			because: "the envelope must expose the success flag");
		contract.Description.Should().Contain("get-guidance",
			because: "the contract should steer callers to read the esq guidance before composing a query");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical get-guidance contract so callers can retrieve guidance through a tool instead of docs URI routing.")]
	public void ToolContractGet_Should_Return_Guidance_Get_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			GuidanceGetTool.ToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-guidance is part of the executable clio MCP contract surface");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.InputSchema.Required.Should().ContainSingle(required => required == "name",
			because: "guidance lookup should require the stable guide name");
		contract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "name" &&
				field.Description.Contains("page-modification", StringComparison.Ordinal) &&
				field.Description.Contains("page-schema-handlers", StringComparison.Ordinal) &&
				field.Description.Contains("page-schema-validators", StringComparison.Ordinal),
			because: "the contract should advertise the stable guidance-name selector");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "guidance",
			because: "successful lookups should return the resolved article payload");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "available-guides",
			because: "failed lookups should expose recovery names");
		contract.Examples.Any(example =>
			example.Arguments.TryGetValue("name", out object? value)
			&& string.Equals(value?.ToString(), "page-schema-handlers", StringComparison.Ordinal)).Should().BeTrue(
			because: "the contract should advertise the canonical handler guidance lookup example");
		contract.Examples.Any(example =>
			example.Arguments.TryGetValue("name", out object? value)
			&& string.Equals(value?.ToString(), "page-modification", StringComparison.Ordinal)).Should().BeTrue(
			because: "the contract should advertise the canonical general page modification guidance lookup example");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical create-entity-business-rule contract with generated-name rejection, actual response fields, and entity-rule workflow guidance.")]
	public void ToolContractGet_Should_Return_BusinessRuleCreate_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			CreateEntityBusinessRuleTool.BusinessRuleCreateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "tool-contract-get should expose the create-entity-business-rule contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.InputSchema.Required.Should().Contain(["environment-name", "package-name", "entity-schema-name", "rules"],
			because: "entity-business-rule creation requires environment package entity and rules payload");
		contract.InputSchema.Validators.Should().Contain(validator =>

				validator.Name == "enum" &&
				validator.Field == "rules[*].condition.logicalOperation",
			because: "the contract should validate the target architecture logicalOperation field");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rules[*].condition.conditions[*].comparisonType",
			because: "the contract should validate target-architecture comparisonType fields");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "conditional-field" &&
				validator.Field == "rules[*].condition.conditions[*].rightExpression" &&
				validator.Context!.Contains("Omit or null for is-filled-in and is-not-filled-in", StringComparison.Ordinal),
			because: "the contract should advertise the unary-versus-binary rightExpression rule");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "comparison-family" &&
				validator.Field == "rules[*].condition.conditions[*]" &&
				validator.Context!.Contains("date/time left attributes", StringComparison.Ordinal),
			because: "the contract should advertise the numeric and date/time scope of relational comparisons");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "comparison-family" &&
				validator.Code == "unsupported-equality-operands" &&
				validator.Field == "rules[*].condition.conditions[*]" &&
				validator.Context!.Contains("RichText or Image", StringComparison.Ordinal),
			because: "the contract should advertise Creatio's equality limitation for rich text and image columns");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "date-time-constant" &&
				validator.Field == "rules[*].condition.conditions[*].rightExpression.value" &&
				validator.Context!.Contains("timezone suffix", StringComparison.Ordinal),
			because: "the contract should explicitly require timezone-aware DateTime and Time constants");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "lookup-record" &&
				validator.Field == "rules[*].condition.conditions[*].rightExpression.value" &&
				validator.Context!.Contains(ODataReadTool.ToolName, StringComparison.Ordinal),
			because: "the contract should tell callers to resolve lookup condition constants with odata-read");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rules[*].actions[*].type",
			because: "the contract should validate target-architecture action type fields");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rules[*].actions[*].type" &&
				validator.Context!.Contains("set-values", StringComparison.Ordinal),
			because: "the contract should advertise the Set values action type");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "set-values-shape" &&
				validator.Field == "rules[*].actions[*].items[*]" &&
				validator.Context!.Contains("forward reference paths like LookupColumn.SourceColumn", StringComparison.Ordinal),
			because: "the contract should advertise AttributeValue source assignments in the current set-values scope");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "set-values-constant" &&
				validator.Field == "rules[*].actions[*].items[*].value.value" &&
				validator.Context!.Contains("GUID string constants for Lookup targets", StringComparison.Ordinal),
			because: "the contract should document typed constant payloads for set-values including lookup targets");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "set-values-formula" &&
				validator.Field == "rules[*].actions[*].items[*].value.expression" &&
				validator.Context!.Contains("ExpressionService.svc/Validate", StringComparison.Ordinal),
			because: "the contract should document remote formula validation after expression-schema translation");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "lookup-record" &&
				validator.Field == "rules[*].actions[*].items[*].value.value" &&
				validator.Context!.Contains(ODataReadTool.ToolName, StringComparison.Ordinal),
			because: "the contract should tell callers to resolve lookup set-values constants with odata-read");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "set-values-shape" &&
				validator.Context!.Contains("direct-field arithmetic expression", StringComparison.Ordinal),
			because: "the contract should document the current simple direct-field formula scope");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rules[*].condition.conditions[*].comparisonType" &&
				validator.Context!.Contains("greater-than-or-equal", StringComparison.Ordinal),
			because: "the contract should advertise the full supported comparison set");
		contract.Defaults.Should().BeEmpty(
			because: "the contract should not have defaults after enabled was removed");
		contract.Aliases.Should().BeEmpty(
			because: "the contract should avoid duplicating rejected aliases already represented by the runtime tool schema");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "created",
			because: "the batch output contract advertises the created count the tool returns");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "failed",
			because: "the batch output contract advertises the failed count");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "results",
			because: "the batch output contract advertises the per-rule results array");
		contract.OutputContract.Kind.Should().Be("business-rule-batch-result",
			because: "create-entity-business-rules returns the batch result payload");
		contract.OutputContract.SuccessField.Should().BeNull(
			because: "batch result payloads do not include a single success field");
		contract.OutputContract.FailureSignals.Should().Contain("failed > 0",
			because: "contract-driven clients should detect batch failures from the failed count");
		contract.OutputContract.FailureSignals.Should().NotContain("success == false",
			because: "create-entity-business-rules does not emit a single success field");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "rule",
			because: "tool-contract-get should not advertise a structured rule payload that create-entity-business-rules does not return");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "package-u-id",
			because: "tool-contract-get should not advertise package identifiers that the create-entity-business-rule tool does not return");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "entity-schema-u-id",
			because: "tool-contract-get should not advertise entity identifiers that the create-entity-business-rule tool does not return");
		contract.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ToolContractGetTool.ToolName,
					GuidanceGetTool.ToolName,
					CreateEntityBusinessRuleTool.BusinessRuleCreateToolName
				},
				because: "the contract should advertise app discovery before business-rule creation when the entity belongs to an existing app");
		contract.FallbackFlow.Should().Contain(flow =>
				flow.Tools.SequenceEqual(new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					FindEntitySchemaTool.FindEntitySchemaToolName,
					DataForgeTool.DataForgeFindTablesToolName,
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
					ODataReadTool.ToolName,
					CreateEntityBusinessRuleTool.BusinessRuleCreateToolName
				}),
			because: "the contract should advertise entity discovery through find-entity or Data Forge when the entity is not part of the app context");
		contract.FallbackFlow.Should().Contain(flow =>
				flow.Tools.SequenceEqual(new[] {
					ApplicationCreateTool.ApplicationCreateToolName,
					FindEntitySchemaTool.FindEntitySchemaToolName,
					DataForgeTool.DataForgeFindTablesToolName,
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
					CreateEntityBusinessRuleTool.BusinessRuleCreateToolName
				}),
			because: "the contract should advertise the non-existing-application guidance path before rule creation");
		bool hasLookupExample = contract.Examples.Any(example =>
			FirstRule(example) is Dictionary<string, object?> rule
			&& rule.TryGetValue("condition", out object? conditionValue)
			&& conditionValue is Dictionary<string, object?> condition
			&& condition.TryGetValue("conditions", out object? conditionsValue)
			&& conditionsValue is object[] conditions
			&& conditions.Single() is Dictionary<string, object?> predicate
			&& predicate.TryGetValue("rightExpression", out object? rightExpressionValue)
			&& rightExpressionValue is Dictionary<string, object?> rightExpression
			&& rightExpression.TryGetValue("value", out object? lookupValueObject)
			&& lookupValueObject?.ToString() == "00000000-0000-0000-0000-000000000001"
			);
		hasLookupExample.Should().BeTrue(
			because: "the contract should document that lookup constants are passed as raw GUID strings");
		bool hasUnaryExample = contract.Examples.Any(example =>
			FirstRule(example) is Dictionary<string, object?> rule
			&& rule.TryGetValue("condition", out object? conditionValue)
			&& conditionValue is Dictionary<string, object?> condition
			&& condition.TryGetValue("conditions", out object? conditionsValue)
			&& conditionsValue is object[] conditions
			&& conditions.Single() is Dictionary<string, object?> predicate
			&& predicate.TryGetValue("comparisonType", out object? comparisonTypeValue)
			&& comparisonTypeValue?.ToString() == "is-filled-in"
			&& !predicate.ContainsKey("rightExpression"));
		hasUnaryExample.Should().BeTrue(
			because: "the contract should include a unary filled-in example without rightExpression");
		bool hasRelationalExample = contract.Examples.Any(example =>
			FirstRule(example) is Dictionary<string, object?> rule
			&& rule.TryGetValue("condition", out object? conditionValue)
			&& conditionValue is Dictionary<string, object?> condition
			&& condition.TryGetValue("conditions", out object? conditionsValue)
			&& conditionsValue is object[] conditions
			&& conditions.Single() is Dictionary<string, object?> predicate
			&& predicate.TryGetValue("comparisonType", out object? comparisonTypeValue)
			&& comparisonTypeValue?.ToString() == "less-than-or-equal");
		hasRelationalExample.Should().BeTrue(
			because: "the contract should include a relational example for numeric or date/time comparisons");
		bool hasBooleanExample = contract.Examples.Any(example =>
			FirstRule(example) is Dictionary<string, object?> rule
			&& rule.TryGetValue("condition", out object? conditionValue)
			&& conditionValue is Dictionary<string, object?> condition
			&& condition.TryGetValue("conditions", out object? conditionsValue)
			&& conditionsValue is object[] conditions
			&& conditions.Single() is Dictionary<string, object?> predicate
			&& predicate.TryGetValue("rightExpression", out object? rightExpressionValue)
			&& rightExpressionValue is Dictionary<string, object?> rightExpression
			&& rightExpression.TryGetValue("value", out object? constantValue)
			&& constantValue is true);
		hasBooleanExample.Should().BeTrue(
			because: "the contract should include a boolean constant example using a JSON boolean literal");
		bool hasNumericExample = contract.Examples.Any(example =>
			FirstRule(example) is Dictionary<string, object?> rule
			&& rule.TryGetValue("condition", out object? conditionValue)
			&& conditionValue is Dictionary<string, object?> condition
			&& condition.TryGetValue("conditions", out object? conditionsValue)
			&& conditionsValue is object[] conditions
			&& conditions.Single() is Dictionary<string, object?> predicate
			&& predicate.TryGetValue("comparisonType", out object? comparisonTypeValue)
			&& comparisonTypeValue?.ToString() == "greater-than-or-equal"
			&& predicate.TryGetValue("rightExpression", out object? rightExpressionValue)
			&& rightExpressionValue is Dictionary<string, object?> rightExpression
			&& rightExpression.TryGetValue("value", out object? constantValue)
			&& constantValue is 1000000);
		hasNumericExample.Should().BeTrue(
			because: "the contract should include a numeric threshold example using a JSON numeric literal");
		bool hasTimezoneAwareTimeExample = contract.Examples.Any(example =>
			FirstRule(example) is Dictionary<string, object?> rule
			&& rule.TryGetValue("condition", out object? conditionValue)
			&& conditionValue is Dictionary<string, object?> condition
			&& condition.TryGetValue("conditions", out object? conditionsValue)
			&& conditionsValue is object[] conditions
			&& conditions.Single() is Dictionary<string, object?> predicate
			&& predicate.TryGetValue("rightExpression", out object? rightExpressionValue)
			&& rightExpressionValue is Dictionary<string, object?> rightExpression
			&& rightExpression.TryGetValue("value", out object? constantValue)
			&& string.Equals(constantValue?.ToString(), "12:00:00+02:00", StringComparison.Ordinal));
		hasTimezoneAwareTimeExample.Should().BeTrue(
			because: "the contract should show a timezone-aware Time constant example for coding agents");
		bool hasSetValuesExample = contract.Examples.Any(HasSetValuesConstantExample);
		hasSetValuesExample.Should().BeTrue(
			because: "the contract should include a set-values example with text number boolean Date DateTime Time and lookup constants");
		bool hasSetValuesFormulaExample = contract.Examples.Any(HasSetValuesFormulaExample);
		hasSetValuesFormulaExample.Should().BeTrue(
			because: "the contract should include a set-values formula example using direct field names");
		bool hasSetValuesAttributeExample = contract.Examples.Any(HasSetValuesAttributeExample);
		hasSetValuesAttributeExample.Should().BeTrue(
			because: "the contract should include a set-values AttributeValue example using a forward reference source path");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical create-page-business-rule contract with page attribute validation and page-action workflow guidance.")]
	public void ToolContractGet_Should_Return_PageBusinessRuleCreate_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			CreatePageBusinessRuleTool.BusinessRuleCreateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "tool-contract-get should expose the create-page-business-rule contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.InputSchema.Required.Should().Contain(["environment-name", "package-name", "page-schema-name", "rules"],
			because: "page-business-rule creation requires environment package page and rules payload");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rules[*].actions[*].type" &&
				validator.Context!.Contains("hide-element", StringComparison.Ordinal) &&
				validator.Context.Contains("show-element", StringComparison.Ordinal) &&
				validator.Context.Contains("make-editable", StringComparison.Ordinal) &&
				validator.Context.Contains("make-read-only", StringComparison.Ordinal) &&
				validator.Context.Contains("make-required", StringComparison.Ordinal) &&
				validator.Context.Contains("make-optional", StringComparison.Ordinal),
			because: "the page rule contract should advertise all supported page actions");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "page-element" &&
				validator.Field == "rules[*].actions[*].items" &&
				validator.Context!.Contains("recursive get-page bundle.viewConfig", StringComparison.Ordinal),
			because: "the contract should direct callers to recursive page element discovery");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "page-attribute" &&
				validator.Field == "rules[*].condition.conditions[*].leftExpression.path",
			because: "page rule conditions must use declared page view-model attributes");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "conditional-field" &&
				validator.Field == "rules[*].condition.conditions[*].rightExpression" &&
				validator.Context!.Contains("Omit or null for is-filled-in and is-not-filled-in", StringComparison.Ordinal),
			because: "page rules share the business-rule unary-versus-binary rightExpression contract");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "comparison-family" &&
				validator.Code == "unsupported-relational-operands" &&
				validator.Field == "rules[*].condition.conditions[*]" &&
				validator.Context!.Contains("date/time left attributes", StringComparison.Ordinal),
			because: "page rules share the numeric and date/time scope for relational comparisons");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "date-time-constant" &&
				validator.Field == "rules[*].condition.conditions[*].rightExpression.value" &&
				validator.Context!.Contains("timezone suffix", StringComparison.Ordinal),
			because: "page rules share the DateTime and Time constant timezone requirement");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "lookup-record" &&
				validator.Field == "rules[*].condition.conditions[*].rightExpression.value" &&
				validator.Context!.Contains(ODataReadTool.ToolName, StringComparison.Ordinal),
			because: "page rules should tell callers to resolve lookup condition constants with odata-read");
		contract.OutputContract.Kind.Should().Be("business-rule-batch-result",
			because: "create-page-business-rules returns the batch result payload");
		contract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					ToolContractGetTool.ToolName,
					GuidanceGetTool.ToolName,
					CreatePageBusinessRuleTool.BusinessRuleCreateToolName
				},
				because: "the contract should require page discovery before rule creation");
		bool hasHideElementExample = contract.Examples.Any(HasPageActionExample("hide-element"));
		hasHideElementExample.Should().BeTrue(
			because: "the contract should include a hide-element page rule example");
		bool hasShowElementExample = contract.Examples.Any(HasPageActionExample("show-element"));
		hasShowElementExample.Should().BeTrue(
			because: "the contract should include a show-element page rule example");
		bool hasMakeEditableExample = contract.Examples.Any(HasPageActionExample("make-editable"));
		hasMakeEditableExample.Should().BeTrue(
			because: "the contract should include a make-editable page rule example");
		bool hasMakeReadOnlyExample = contract.Examples.Any(HasPageActionExample("make-read-only"));
		hasMakeReadOnlyExample.Should().BeTrue(
			because: "the contract should include a make-read-only page rule example");
		bool hasMakeRequiredExample = contract.Examples.Any(HasPageActionExample("make-required"));
		hasMakeRequiredExample.Should().BeTrue(
			because: "the contract should include a make-required page rule example");
		bool hasMakeOptionalExample = contract.Examples.Any(HasPageActionExample("make-optional"));
		hasMakeOptionalExample.Should().BeTrue(
			because: "the contract should include a make-optional page rule example");
	}

	private static Dictionary<string, object?>? FirstRule(ToolContractExample example) =>
		example.Arguments.TryGetValue("rules", out object? value) && value is object[] { Length: > 0 } rules
			? rules[0] as Dictionary<string, object?>
			: null;

	private static Func<ToolContractExample, bool> HasPageActionExample(string actionType) =>
		example =>
			FirstRule(example) is Dictionary<string, object?> rule
			&& rule.TryGetValue("actions", out object? actionsValue)
			&& actionsValue is object[] actions
			&& actions.OfType<Dictionary<string, object?>>().Any(action =>
				string.Equals(action["type"]?.ToString(), actionType, StringComparison.Ordinal));

	private static bool HasSetValuesConstantExample(ToolContractExample example) {
		if (FirstRule(example) is not Dictionary<string, object?> rule
			|| !rule.TryGetValue("actions", out object? actionsValue)
			|| actionsValue is not object[] actions
			|| actions.SingleOrDefault() is not Dictionary<string, object?> action
			|| !string.Equals(action["type"]?.ToString(), "set-values", StringComparison.Ordinal)
			|| !action.TryGetValue("items", out object? itemsValue)
			|| itemsValue is not object[] items) {
			return false;
		}

		object?[] values = items
			.OfType<Dictionary<string, object?>>()
			.Select(item => item["value"])
			.OfType<Dictionary<string, object?>>()
			.Select(valueExpression => valueExpression["value"])
			.ToArray();
		return values.Contains("Ready")
			&& values.Contains(42)
			&& values.Contains(true)
			&& values.Contains("2025-01-01")
			&& values.Contains("12:00:00+02:00")
			&& values.Contains("2025-01-01T00:00:00Z")
			&& values.Contains("00000000-0000-0000-0000-000000000001");
	}

	private static bool HasSetValuesFormulaExample(ToolContractExample example) {
		if (FirstRule(example) is not Dictionary<string, object?> rule
			|| !rule.TryGetValue("actions", out object? actionsValue)
			|| actionsValue is not object[] actions
			|| actions.SingleOrDefault() is not Dictionary<string, object?> action
			|| !string.Equals(action["type"]?.ToString(), "set-values", StringComparison.Ordinal)
			|| !action.TryGetValue("items", out object? itemsValue)
			|| itemsValue is not object[] items) {
			return false;
		}

		return items
			.OfType<Dictionary<string, object?>>()
			.Select(item => item["value"])
			.OfType<Dictionary<string, object?>>()
			.Any(valueExpression =>
				string.Equals(valueExpression["type"]?.ToString(), "Formula", StringComparison.Ordinal)
				&& string.Equals(valueExpression["expression"]?.ToString(), "UsrEstimatedEffort + UsrExtraEffort",
					StringComparison.Ordinal));
	}

	private static bool HasSetValuesAttributeExample(ToolContractExample example) {
		if (FirstRule(example) is not Dictionary<string, object?> rule
			|| !rule.TryGetValue("actions", out object? actionsValue)
			|| actionsValue is not object[] actions
			|| actions.SingleOrDefault() is not Dictionary<string, object?> action
			|| !string.Equals(action["type"]?.ToString(), "set-values", StringComparison.Ordinal)
			|| !action.TryGetValue("items", out object? itemsValue)
			|| itemsValue is not object[] items) {
			return false;
		}

		return items
			.OfType<Dictionary<string, object?>>()
			.Select(item => item["value"])
			.OfType<Dictionary<string, object?>>()
			.Any(valueExpression =>
				string.Equals(valueExpression["type"]?.ToString(), "AttributeValue", StringComparison.Ordinal)
				&& string.Equals(valueExpression["path"]?.ToString(), "CreatedBy.Age", StringComparison.Ordinal));
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the structured check-settings-health output contract for bootstrap diagnostics.")]
	public void ToolContractGet_Should_Advertise_Settings_Health_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			SettingsHealthTool.ToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the check-settings-health contract should be available through get-tool-contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "status",
			because: "bootstrap diagnostics should advertise their high-level health state");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "settings-file-path",
			because: "bootstrap diagnostics should expose the appsettings.json file path");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "repairs-applied",
			because: "bootstrap diagnostics should expose automatic repairs in structured form");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "can-execute-env-tools",
			because: "bootstrap diagnostics should tell callers whether named-environment tools can run");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns maintenance-oriented canonical flows for discovery inspection and canonical page mutation tools.")]
	public void ToolContractGet_Should_Return_Maintenance_Oriented_Canonical_Flows() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationGetListTool.ApplicationGetListToolName,
			PageListTool.ToolName,
			PageGetTool.ToolName,
			PageSyncTool.ToolName,
			PageUpdateTool.ToolName,
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the requested maintenance-oriented contracts are all registered by clio MCP");
		ToolContractDefinition[] contracts = result.Tools!.ToArray();
		ToolContractDefinition applicationListContract = contracts.Single(contract => contract.Name == ApplicationGetListTool.ApplicationGetListToolName);
		applicationListContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				},
				because: "application discovery should flow into application inspection for existing-app edits");
		applicationListContract.Examples.Should().ContainSingle(example =>
				example.Arguments.Keys.SequenceEqual(new[] { "environment-name" }),
			because: "list-apps should advertise the minimal top-level payload explicitly");
		ToolContractDefinition pageListContract = contracts.Single(contract => contract.Name == PageListTool.ToolName);
			pageListContract.PreferredFlow.Tools.Should().Equal(
					new[] {
						PageListTool.ToolName,
						PageGetTool.ToolName,
						PageSyncTool.ToolName,
						PageGetTool.ToolName
					},
					because: "list-pages should advertise the canonical clio page workflow after discovery");
			pageListContract.Aliases.Should().Contain(alias =>
					alias.CanonicalName == "code"
					&& alias.Alias == "app-code"
					&& alias.Status == "rejected",
				because: "list-pages should reject the legacy app-code selector through the canonical contract");
			pageListContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				}),
				because: "list-pages should keep the legacy update-page fallback as a single-save sequence after discovery");
		ToolContractDefinition pageGetContract = contracts.Single(contract => contract.Name == PageGetTool.ToolName);
		pageGetContract.Description.Should().Contain("page-modification",
			because: "get-page should route planned body edits to the general page modification guide through the contract surface");
		pageGetContract.Description.Should().NotContain("page-schema-resources",
			because: "get-page should route through the general page-modification guide instead of a localizable-string leaf guide");
		pageGetContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "get-page should advertise sync-pages as the canonical save path after inspection");
		ToolContractDefinition pageSyncContract = contracts.Single(contract => contract.Name == PageSyncTool.ToolName);
		pageSyncContract.Description.Should().Contain("page-modification",
			because: "sync-pages should route body and resource-payload edits through the general page modification guide");
		pageSyncContract.Description.Should().NotContain("page-schema-resources",
			because: "sync-pages should avoid surfacing localizable-string leaf guidance directly in the broad contract description");
		pageSyncContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "sync-pages should advertise itself as the canonical page write path");
		ToolContractDefinition pageUpdateContract = contracts.Single(contract => contract.Name == PageUpdateTool.ToolName);
		pageUpdateContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				},
				because: "update-page still needs a concrete fallback flow for callers that explicitly require it");
		pageUpdateContract.Deprecations.Should().ContainSingle(deprecation =>
				deprecation.ReplacementTools.SequenceEqual(new[] { PageSyncTool.ToolName }) &&
				deprecation.Message.Contains("fallback"),
			because: "update-page should advertise sync-pages as the canonical replacement");
		pageUpdateContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				PageListTool.ToolName,
				PageGetTool.ToolName,
				PageSyncTool.ToolName,
				PageGetTool.ToolName
			}),
			because: "update-page should point callers back to the canonical sync-pages workflow");
		pageSyncContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "pages" &&
				field.Description.Contains("get-page.raw.body") &&
				field.Description.Contains("localizable string"),
			because: "sync-pages should advertise raw.body as the source of page write payloads and clarify resources as localizable strings");
		pageUpdateContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "body" &&
				field.Description.Contains("get-page.raw.body"),
			because: "update-page should advertise raw.body as the source of fallback single-page saves");
		pageUpdateContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "resources" &&
				field.Description.Contains("JSON object string"),
			because: "update-page should clarify the concrete resources payload shape");
		ToolContractDefinition modifyColumnContract = contracts.Single(contract => contract.Name == ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName);
		modifyColumnContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName
				},
				because: "single-column schema edits should inspect current metadata first and read it back after saving");
		modifyColumnContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				SchemaSyncTool.ToolName
			}),
			because: "modify-entity-schema-column should still advertise sync-schemas when the work expands into a multi-step ordered schema plan");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical existing-app section-create contract with selector rules, scalar validation, defaults, and preferred flow guidance.")]
	public void ToolContractGet_Should_Return_ApplicationSectionCreate_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationSectionCreateTool.ApplicationSectionCreateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-tool-contract should expose the create-app-section contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
			because: "the requested tool contract should be returned verbatim");
		contract.InputSchema.Required.Should().Contain(["application-code", "caption"],
			because: "section-create requires application-code and caption as the minimal payload");
		contract.InputSchema.Required.Should().NotContain("environment-name",
			because: "environment-name is schema-optional (FR-05a, ENG-93347): passthrough supplies the tenant via the X-Integration-Credentials header, while non-passthrough requiredness is enforced by the resolver at runtime");
		contract.InputSchema.AnyOf.Should().BeNullOrEmpty(
			because: "section-create now uses a single required application-code selector");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "forbid-fields" &&
				validator.Fields!.Contains("title-localizations"),
			because: "the contract should reject localization maps on the scalar section-create tool");
		contract.Defaults.Should().Contain(defaultValue =>
				defaultValue.Name == "with-mobile-pages" &&
				defaultValue.Value == "true",
			because: "section-create should document its mobile-enabled default explicitly");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "application-code" &&
				alias.Alias == "app-code" &&
				alias.Status == "rejected",
			because: "the contract should reject legacy app-code naming for the section-create tool");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "application-code" &&
				alias.Alias == "application-id" &&
				alias.Status == "rejected",
			because: "the contract should reject the removed application-id selector explicitly");
		contract.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				},
				because: "section-create should advertise the canonical discover-inspect-mutate-verify flow for existing apps");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "section",
			because: "the output contract should advertise the created section payload explicitly");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "entity",
			because: "the output contract should advertise the created or targeted entity payload explicitly");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical existing-app section-update contract with selector rules, scalar partial-update validation, and preferred flow guidance.")]
	public void ToolContractGet_Should_Return_ApplicationSectionUpdate_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-tool-contract should expose the update-app-section contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName,
			because: "the requested tool contract should be returned verbatim");
		contract.InputSchema.Required.Should().Contain(["application-code", "section-code"],
			because: "section-update requires application-code and section-code as the selector payload");
		contract.InputSchema.Required.Should().NotContain("environment-name",
			because: "environment-name is schema-optional (FR-05a, ENG-93347): passthrough supplies the tenant via the X-Integration-Credentials header, while non-passthrough requiredness is enforced by the resolver at runtime");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "caption",
			because: "section-update should advertise caption as an optional mutable field");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "description",
			because: "section-update should advertise description as an optional mutable field");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "icon-id",
			because: "section-update should advertise icon-id as an optional mutable field");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "icon-background",
			because: "section-update should advertise icon-background as an optional mutable field");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "forbid-fields" &&
				validator.Fields!.Contains("title-localizations"),
			because: "the contract should reject localization maps on the scalar section-update tool");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "section-code" &&
				alias.Alias == "sectionCode" &&
				alias.Status == "rejected",
			because: "the contract should reject camelCase section selectors in favor of kebab-case");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "previous-section",
			because: "section-update should return the section metadata before the update for auditability");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "section",
			because: "section-update should return the section metadata after the update");
		contract.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName
				},
				because: "section-update should advertise the canonical discover-inspect-mutate flow for existing section metadata edits");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical existing-app section-delete contract with environment-name schema-optional (FR-05a, ENG-93347).")]
	public void ToolContractGet_Should_Return_ApplicationSectionDelete_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-tool-contract should expose the delete-app-section contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName,
			because: "the requested tool contract should be returned verbatim");
		contract.InputSchema.Required.Should().Contain(["application-code", "section-code"],
			because: "section-delete requires application-code and section-code as the selector payload");
		contract.InputSchema.Required.Should().NotContain("environment-name",
			because: "environment-name is schema-optional (FR-05a, ENG-93347): passthrough supplies the tenant via the X-Integration-Credentials header, while non-passthrough requiredness is enforced by the resolver at runtime");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the full canonical entity-schema contract surface with authoritative flows and metadata from clio.")]
	public void ToolContractGet_Should_Return_Canonical_EntitySchema_Surface() {
		// Arrange
		ToolContractGetTool tool = new();
		string[] requestedTools = [
			SchemaSyncTool.ToolName,
			CreateLookupTool.CreateLookupToolName,
			CreateEntitySchemaTool.CreateEntitySchemaToolName,
			UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
			GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
			GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
		];

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(requestedTools));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-tool-contract should expose the full canonical entity/schema MCP surface from clio");
		result.Tools.Should().NotBeNull(
			because: "successful canonical surface lookup should include contract definitions");
		result.Tools!.Select(contract => contract.Name).Should().BeEquivalentTo(requestedTools,
			because: "the canonical entity/schema tool surface should be retrievable as one consistent contract set");
		result.Tools.Should().OnlyContain(contract =>
				contract.OutputContract != null
				&& contract.ErrorContract != null
				&& contract.PreferredFlow != null
				&& contract.FallbackFlow != null,
			because: "each canonical schema tool contract should publish output, error, and flow metadata");
		result.Tools.Should().Contain(contract =>
				contract.Name == SchemaSyncTool.ToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] {
					ApplicationCreateTool.ApplicationCreateToolName,
					SchemaSyncTool.ToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				}),
			because: "sync-schemas should advertise the canonical batched entity workflow");
		result.Tools.Should().Contain(contract =>
				contract.Name == CreateLookupTool.CreateLookupToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] { SchemaSyncTool.ToolName }),
			because: "create-lookup should advertise sync-schemas as the preferred canonical path");
		result.Tools.Should().Contain(contract =>
				contract.Name == CreateEntitySchemaTool.CreateEntitySchemaToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] { SchemaSyncTool.ToolName }),
			because: "create-entity-schema should advertise sync-schemas as the preferred canonical path");
		result.Tools.Should().Contain(contract =>
				contract.Name == UpdateEntitySchemaTool.UpdateEntitySchemaToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] {
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
					UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName
				}),
			because: "update-entity-schema should advertise the canonical inspect-mutate-verify flow");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical local binding contract surface with workflow routing and workspace-path validation guidance.")]
	public void ToolContractGet_Should_Return_Canonical_Local_Binding_Surface() {
		// Arrange
		ToolContractGetTool tool = new();
		string[] requestedTools = [
			CreateDataBindingTool.CreateDataBindingToolName,
			AddDataBindingRowTool.AddDataBindingRowToolName,
			RemoveDataBindingRowTool.RemoveDataBindingRowToolName
		];

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(requestedTools));

		// Assert
		result.Success.Should().BeTrue(
			because: "the authoritative local binding surface should be served by clio");
		result.Tools.Should().NotBeNull(
			because: "a successful lookup should return the requested local binding contracts");
		result.Tools!.Select(contract => contract.Name).Should().Equal(requestedTools,
			because: "the response should preserve the requested local binding tool order");

		ToolContractDefinition createContract = result.Tools.Single(contract =>
			contract.Name == CreateDataBindingTool.CreateDataBindingToolName);
		createContract.Description.Should().Contain("data-bindings",
			because: "create-data-binding should route callers to the canonical binding guide");
		createContract.InputSchema.Required.Should().Contain(["package-name", "schema-name", "workspace-path"],
			because: "create-data-binding should require package-name, schema-name, and workspace-path as the minimal local payload");
		createContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "environment-name" &&
				field.Description.Contains("Required when schema-name is not SysSettings", StringComparison.Ordinal),
			because: "create-data-binding should advertise that runtime schemas require environment-name on the MCP surface");
		createContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "workspace-path" &&
				field.Description.Contains("Absolute local workspace path", StringComparison.Ordinal),
			because: "create-data-binding should canonically describe the local workspace requirement");
		createContract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "require-environment-name-for-runtime-schema"
				&& validator.Code == "missing-required-parameter"
				&& validator.Required == true
				&& validator.Fields != null
				&& validator.Fields.SequenceEqual(new[] { "schema-name", "environment-name" })
				&& validator.Context != null
				&& validator.Context.Contains("SysSettings", StringComparison.Ordinal)
				&& !validator.Context.Contains("SysModule", StringComparison.Ordinal),
			because: "create-data-binding should expose a machine-readable conditional requirement for runtime schemas");
		createContract.Defaults.Should().Contain(defaultValue =>
				defaultValue.Name == "install-type" &&
				defaultValue.Value == "0",
			because: "create-data-binding should advertise the default install-type");
		createContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					CreateDataBindingTool.CreateDataBindingToolName,
					AddDataBindingRowTool.AddDataBindingRowToolName
				},
				because: "create-data-binding should advertise the local create-then-edit artifact flow");
		createContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				SchemaSyncTool.ToolName
			}),
			because: "create-data-binding should point callers back to sync-schemas when lookup work can stay batched");

		ToolContractDefinition addContract = result.Tools.Single(contract =>
			contract.Name == AddDataBindingRowTool.AddDataBindingRowToolName);
		addContract.Description.Should().Contain("data-bindings",
			because: "add-data-binding-row should route callers to the canonical binding guide");
		addContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					CreateDataBindingTool.CreateDataBindingToolName,
					AddDataBindingRowTool.AddDataBindingRowToolName
				},
				because: "add-data-binding-row should advertise the create-then-edit local artifact flow");

		ToolContractDefinition removeContract = result.Tools.Single(contract =>
			contract.Name == RemoveDataBindingRowTool.RemoveDataBindingRowToolName);
		removeContract.Description.Should().Contain("data-bindings",
			because: "remove-data-binding-row should route callers to the canonical binding guide");
		removeContract.InputSchema.Properties.Should().Contain(field => field.Name == "key-value",
			because: "remove-data-binding-row should continue advertising the canonical key-value parameter name");
		removeContract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "workspace-path" &&
				alias.Alias == "workspacePath",
			because: "remove-data-binding-row should reject the camelCase workspace path alias explicitly");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical DB-first binding contract surface with explicit fallback, lifecycle, and failure guidance.")]
	public void ToolContractGet_Should_Return_Canonical_DbFirst_Binding_Surface() {
		// Arrange
		ToolContractGetTool tool = new();
		string[] requestedTools = [
			CreateDataBindingDbTool.CreateDataBindingDbToolName,
			UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName,
			RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName
		];

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(requestedTools));

		// Assert
		result.Success.Should().BeTrue(
			because: "the authoritative DB-first binding surface should be served by clio");
		result.Tools.Should().NotBeNull(
			because: "a successful lookup should return the requested DB-first binding contracts");
		result.Tools!.Select(contract => contract.Name).Should().Equal(requestedTools,
			because: "the response should preserve the requested DB-first binding tool order");

		ToolContractDefinition createContract = result.Tools.Single(contract =>
			contract.Name == CreateDataBindingDbTool.CreateDataBindingDbToolName);
		createContract.Description.Should().Contain("data-bindings",
			because: "create-data-binding-db should route callers to the canonical binding guide");
		createContract.Description.Should().Contain("primary key plus columns referenced",
			because: "create-data-binding-db should explain the subset-column projection rule for DB-first binding metadata");
		createContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					SchemaSyncTool.ToolName
				},
				because: "create-data-binding-db should advertise sync-schemas as the canonical batched path");
		createContract.Deprecations.Should().ContainSingle(
			because: "create-data-binding-db should advertise that it is a fallback or standalone path");
		createContract.Deprecations[0].Message.Should().Contain("fallback",
			because: "the deprecation guidance should explicitly frame create-data-binding-db as a fallback");
		createContract.Deprecations[0].Message.Should().Contain("seed-rows",
			because: "the deprecation guidance should point callers at inline seed-rows inside sync-schemas");
		createContract.Deprecations[0].Message.Should().Contain("direct SQL",
			because: "the deprecation guidance should keep standalone lookup seeding on the MCP surface");
		createContract.Deprecations[0].Message.Should().Contain("get-guidance",
			because: "the deprecation guidance should route callers to the canonical binding guide");
		createContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "rows" &&
				field.Description.Contains("values object") &&
				field.Description.Contains("projected", StringComparison.Ordinal),
			because: "create-data-binding-db should canonically describe the required rows[].values shape");
		createContract.Examples.Should().Contain(example =>
				example.Arguments["rows"] != null &&
				example.Arguments["rows"].ToString()!.Contains("In Progress", StringComparison.Ordinal),
			because: "create-data-binding-db should advertise a realistic multi-row lookup seeding example");

		ToolContractDefinition upsertContract = result.Tools.Single(contract =>
			contract.Name == UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName);
		upsertContract.Description.Should().Contain("data-bindings",
			because: "upsert-data-binding-row-db should route callers to the canonical binding guide");
		upsertContract.Description.Should().Contain("bound rows and the requested upsert payload",
			because: "upsert-data-binding-row-db should explain how projected binding metadata is rebuilt");
		upsertContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					CreateDataBindingDbTool.CreateDataBindingDbToolName,
					UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName
				},
				because: "upsert-data-binding-row-db should advertise the required create-then-upsert sequence");
		upsertContract.ErrorContract.Codes.Should().Contain(code => code.Code == "binding-not-found",
			because: "upsert-data-binding-row-db should document the missing-binding failure mode");

		ToolContractDefinition removeContract = result.Tools.Single(contract =>
			contract.Name == RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName);
		removeContract.Description.Should().Contain("data-bindings",
			because: "remove-data-binding-row-db should route callers to the canonical binding guide");
		removeContract.Description.Should().Contain("remaining bound rows",
			because: "remove-data-binding-row-db should explain how projected binding metadata is rebuilt after deletion");
		removeContract.Description.Should().Contain("package schema data record",
			because: "remove-data-binding-row-db should document the last-row lifecycle cleanup");
		removeContract.InputSchema.Properties.Should().Contain(field => field.Name == "key-value",
			because: "remove-data-binding-row-db should continue advertising the canonical key-value parameter name");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises enriched get-app-info output fields for installed application identity.")]
	public void ToolContractGet_Should_Advertise_Application_Info_Identity_Fields() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationGetInfoTool.ApplicationGetInfoToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the get-app-info contract should be available through get-tool-contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-id",
			because: "the contract should advertise the installed application identifier");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-name",
			because: "the contract should advertise the installed application display name");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-code",
			because: "the contract should advertise the installed application code");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-version",
			because: "the contract should advertise the installed application version");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "schema-name-prefix",
			because: "the contract should advertise the active SchemaNamePrefix so agents know the correct prefix for subsequent schema names");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the canonical create-app validators aliases and preferred flow through get-tool-contract.")]
	public void ToolContractGet_Should_Advertise_Application_Create_Canonical_Rules() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationCreateTool.ApplicationCreateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the create-app contract should be available through get-tool-contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "canonical-main-entity-name",
			because: "create-app should advertise the canonical main entity field in its response shape");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "schema-name-prefix",
			because: "create-app should advertise the active SchemaNamePrefix so agents know the correct prefix for all subsequent schema codes");
		contract.OutputContract.Fields.Should().Contain(field =>
				field.Name == "dataforge" &&
				field.Description.Contains("context-summary", StringComparison.Ordinal),
			because: "create-app should advertise the built-in Data Forge diagnostics block in its response contract");
		contract.InputSchema.Validators.Should().ContainSingle(validator =>
				validator.Name == "forbid-fields"
				&& validator.Fields!.Contains("title-localizations")
				&& validator.Fields.Contains("descriptionLocalizations"),
			because: "create-app should advertise forbidden localization maps through the canonical contract");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "code"
				&& alias.Alias == "app-code"
				&& alias.Status == "rejected",
			because: "create-app should reject legacy alias parameters through the canonical contract");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "name"
				&& alias.Alias == "app-name"
				&& alias.Status == "rejected",
			because: "create-app should reject legacy alias parameters through the canonical contract");
		contract.PreferredFlow.Tools.Should().Equal(
			new[] {
				ApplicationCreateTool.ApplicationCreateToolName,
				SchemaSyncTool.ToolName,
				ApplicationGetInfoTool.ApplicationGetInfoToolName
			},
			because: "create-app should advertise the canonical create -> sync-schemas -> refresh flow");
		contract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				ApplicationGetListTool.ApplicationGetListToolName,
				ApplicationGetInfoTool.ApplicationGetInfoToolName
			}),
			because: "create-app should advertise the canonical existing-app fallback flow");
		contract.Examples.Should().ContainSingle(example =>
				example.Summary.Contains("top-level payload", StringComparison.Ordinal),
			because: "create-app should advertise the minimal top-level request shape explicitly");
		contract.AntiPatterns.Should().NotBeNullOrEmpty(
			because: "create-app should advertise known anti-patterns so agents avoid wasted round-trips");
		contract.AntiPatterns.Should().Contain(ap =>
				ap.Pattern.Contains("create-app-section", StringComparison.Ordinal) &&
				ap.Why.Contains("canonical-main-entity-name", StringComparison.Ordinal),
			because: "create-app should explicitly warn against the create-app → create-app-section → delete-app-section waste pattern");
		contract.OutputContract.Fields.Should().Contain(field =>
				field.Name == "canonical-main-entity-name" &&
				field.Description.Contains("sync-schemas", StringComparison.Ordinal) &&
				field.Description.Contains("create-app-section", StringComparison.Ordinal),
			because: "canonical-main-entity-name description should guide the agent to use sync-schemas and warn against create-app-section");
		contract.PreferredFlow.Notes.Should().Contain("create-app-section",
			because: "the preferred-flow notes should explicitly warn against calling create-app-section for a single-section app");
		contract.Defaults.Should().Contain(defaultValue =>
				defaultValue.Name == "with-mobile-pages" &&
				defaultValue.Value == "true",
			because: "create-app should document its mobile-enabled default explicitly so the MCP surface stays consistent with create-app-section");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that create-app-section preferred-flow notes contain the guard against calling it directly after create-app.")]
	public void ToolContractGet_Should_Advertise_ApplicationSectionCreate_PostCreateApp_Guard() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationSectionCreateTool.ApplicationSectionCreateToolName
		]));

		// Assert
		ToolContractDefinition contract = result.Tools!.Single();
		contract.PreferredFlow.Notes.Should().Contain("create-app",
			because: "create-app-section preferred-flow should warn that it must not be called directly after create-app for the primary section");
		contract.PreferredFlow.Notes.Should().Contain("canonical-main-entity-name",
			because: "the guard should redirect the agent to canonical-main-entity-name as the correct alternative");
		contract.PreferredFlow.Notes.Should().Contain("second or subsequent",
			because: "the notes should make clear that create-app-section is only for adding additional sections");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the full Data Forge contract surface through explicit lookup while keeping maintenance tools out of the default bootstrap set.")]
	public void ToolContractGet_Should_Return_Full_DataForge_Surface_On_Explicit_Lookup() {
		// Arrange
		ToolContractGetTool tool = new();
		string[] requestedTools = [
			DataForgeTool.DataForgeStatusToolName,
			DataForgeTool.DataForgeFindTablesToolName,
			DataForgeTool.DataForgeFindLookupsToolName,
			DataForgeTool.DataForgeGetRelationsToolName,
			DataForgeTool.DataForgeGetTableColumnsToolName,
			DataForgeTool.DataForgeContextToolName,
			DataForgeTool.DataForgeInitializeToolName,
			DataForgeTool.DataForgeUpdateToolName
		];

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(requestedTools));

		// Assert
		result.Success.Should().BeTrue(
			because: "the full Data Forge surface should remain available through explicit get-tool-contract lookup");
		result.Tools.Should().NotBeNull(
			because: "explicit Data Forge lookup should return the requested contracts");
		result.Tools!.Select(contract => contract.Name).Should().Equal(requestedTools,
			because: "the response should preserve the requested Data Forge tool order");
		result.Tools.Should().Contain(contract =>
				contract.Name == DataForgeTool.DataForgeInitializeToolName &&
				contract.OutputContract.Fields.Any(field => field.Name == "status"),
			because: "the maintenance initialize contract should remain available through explicit lookup");
		result.Tools.Should().Contain(contract =>
				contract.Name == DataForgeTool.DataForgeUpdateToolName &&
				contract.OutputContract.Fields.Any(field => field.Name == "status"),
			because: "the maintenance update contract should remain available through explicit lookup");
		result.Tools.Should().OnlyContain(contract =>
				contract.Description.Contains("Creatio platform version 10.0.0 or later"),
			because: "DataForge contracts should advertise the Creatio platform requirement");
		result.Tools.Should().Contain(contract =>
				contract.Name == DataForgeTool.DataForgeStatusToolName &&
				contract.PreferredFlow != null &&
				contract.PreferredFlow.Notes.Contains("whether Data Forge discovery is available"),
			because: "the dataforge-status preferred flow should explain when to call it");
		ToolContractDefinition columnsContract = result.Tools.Single(contract =>
			contract.Name == DataForgeTool.DataForgeGetTableColumnsToolName);
		columnsContract.Description.Should().Contain("logical columns of a Creatio table",
			because: "dataforge-get-table-columns should advertise the caller-facing result");
		columnsContract.Description.Should().Contain("lookup targets",
			because: "column metadata includes reference-schema hints that callers use during modeling");
		ToolContractDefinition contextContract = result.Tools.Single(contract =>
			contract.Name == DataForgeTool.DataForgeContextToolName);
		contextContract.Description.Should().Contain("compact Data Forge context package",
			because: "dataforge-context should be framed as an aggregated planning result");
		contextContract.Description.Should().Contain("similar tables, lookup matches, relation paths, table columns, and readiness status",
			because: "the contract should list the planning artifacts the caller receives");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns structured unknown tool suggestions when the requested tool name is misspelled.")]
	public void ToolContractGet_Should_Return_Structured_Error_For_Unknown_Tool() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			"page-updte"
		]));

		// Assert
		result.Success.Should().BeFalse(
			because: "a misspelled tool name should fail contract lookup");
		result.Tools.Should().BeNull(
			because: "no contract payload should be returned when the lookup fails");
		result.Error.Should().NotBeNull(
			because: "the tool should return a structured error envelope for unknown names");
		result.Error!.Code.Should().Be("tool-not-found",
			because: "unknown tool names should map to the tool-not-found error code");
		result.Error.Suggestions.Should().Contain(PageUpdateTool.ToolName,
			because: "the error should suggest the closest matching registered tool name");
		result.Error.Message.Should().Contain("get-tool-contract",
			because: "the tool-not-found message must point the agent at the discovery tool when its guess (and suggestions) miss");
		result.Error.Message.Should().Contain("compact index of every tool",
			because: "the discovery hint must name the cheap compact-index path for in-band recovery");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the canonical required field name in the modify-entity-schema-column contract.")]
	public void ToolContractGet_Should_Use_Canonical_Required_Key_For_Modify_Entity_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the modify-entity-schema-column contract lookup should succeed");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "required",
			because: "the contract should advertise the canonical required field name");
		contract.InputSchema.Properties.Should().NotContain(field => field.Name == "is-required",
			because: "legacy aliases should not be exposed as canonical request fields");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().Contain("required",
			because: "the examples should use the canonical required field name");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().NotContain("is-required",
			because: "the examples should not teach callers to use the removed legacy alias");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns field-level validation errors when the request contains blank tool names.")]
	public void ToolContractGet_Should_Return_Field_Level_Error_For_Blank_Tool_Name() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			" "
		]));

		// Assert
		result.Success.Should().BeFalse(
			because: "blank tool names are invalid input");
		result.Error.Should().NotBeNull(
			because: "invalid input should return a structured validation error");
		result.Error!.Code.Should().Be("missing-required-parameter",
			because: "blank tool names should be treated as missing required values");
		result.Error.FieldErrors.Should().ContainSingle(
			because: "the validation error should identify the exact offending entry");
		result.Error.FieldErrors![0].Field.Should().Be("tool-names[0]",
			because: "the field path should point to the blank element inside tool-names");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the get-schema-name-prefix contract so contract-driven clients can discover its input and response shape before invoking it.")]
	public void ToolContractGet_Should_Return_GetSchemaNamePrefix_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			SchemaNamePrefixTool.GetSchemaNamePrefixToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-schema-name-prefix is a registered clio MCP tool and its contract should be discoverable through get-tool-contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.InputSchema.Required.Should().Contain("environment-name",
			because: "get-schema-name-prefix requires the target environment to resolve the active SchemaNamePrefix setting");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "schema-name-prefix",
			because: "the contract should advertise the schema-name-prefix field so callers know how to read the returned prefix");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "success",
			because: "the contract should advertise the success field so callers can detect failures structurally");
		contract.Examples.Should().NotBeEmpty(
			because: "the contract should include at least one example showing the minimal required input shape");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns structured invalid-parameter-alias error when caller sends camelCase legacy aliases instead of tool-names")]
	public void ToolContractGet_Should_Return_Alias_Error_For_Legacy_CamelCase_Args() {
		ToolContractGetTool tool = new();
		var element = System.Text.Json.JsonDocument.Parse("\"list-pages\"").RootElement;
		ToolContractGetArgs args = new() {
			ExtensionData = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> {
				["toolName"] = element
			}
		};

		ToolContractGetResponse result = tool.GetToolContracts(args);

		result.Success.Should().BeFalse(
			because: "unknown args should not silently fall back to listing all tools");
		result.Error.Should().NotBeNull(
			because: "legacy alias failures should return structured error details");
		result.Error!.Code.Should().Be("invalid-parameter-alias",
			because: "legacy camelCase args should be reported as alias errors");
		result.Error.Message.Should().Contain("tool-names",
			because: "the error should teach the caller the canonical argument name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns structured error listing valid args when caller sends an entirely unknown key")]
	public void ToolContractGet_Should_Report_Unknown_Args_With_Valid_List() {
		ToolContractGetTool tool = new();
		var element = System.Text.Json.JsonDocument.Parse("\"list-pages\"").RootElement;
		ToolContractGetArgs args = new() {
			ExtensionData = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> {
				["foo"] = element
			}
		};

		ToolContractGetResponse result = tool.GetToolContracts(args);

		result.Success.Should().BeFalse(
			because: "unknown args should not silently fall back to listing all tools");
		result.Error!.Message.Should().Contain("'foo'",
			because: "unknown args should be quoted back so the caller sees what was rejected");
		result.Error.Message.Should().Contain("tool-names",
			because: "the error should point to the canonical valid args");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves contracts from a flat top-level 'tool-names' array sent without the nested 'args' wrapper (field-test defect #4).")]
	public void ToolContractGet_Should_Resolve_Contracts_From_Flat_ToolNames() {
		// Arrange
		ToolContractGetTool tool = new();
		JsonElement flat = JsonDocument.Parse("[\"get-tool-contract\"]").RootElement;
		ToolContractGetArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["tool-names"] = flat
			}
		};

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(args);

		// Assert
		result.Success.Should().BeTrue(
			because: "a flat top-level 'tool-names' (no nested 'args' wrapper) must resolve contracts instead of failing");
		result.Tools.Should().ContainSingle(contract => contract.Name == ToolContractGetTool.ToolName,
			because: "the flat-shape tool name must resolve to its contract");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves named contracts from a flat 'tool-names' sent alongside a co-key 'detail' (no nested 'args' wrapper) instead of mis-reporting 'tool-names' as an unknown arg.")]
	public void ToolContractGet_Should_Resolve_Contracts_From_Flat_ToolNames_When_Detail_CoKey_Present() {
		// Arrange
		ToolContractGetTool tool = new();
		JsonElement flatNames = JsonDocument.Parse("[\"get-tool-contract\"]").RootElement;
		JsonElement flatDetail = JsonDocument.Parse("\"full\"").RootElement;
		ToolContractGetArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["tool-names"] = flatNames,
				["detail"] = flatDetail
			}
		};

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(args);

		// Assert
		result.Success.Should().BeTrue(
			because: "a co-present flat 'detail' key must not block recovery of flat 'tool-names'");
		result.Error.Should().BeNull(
			because: "the canonical 'tool-names' key must never be reported as an unknown arg when a 'detail' co-key is present");
		result.Tools.Should().ContainSingle(contract => contract.Name == ToolContractGetTool.ToolName,
			because: "named tool-names must resolve their full contract even when 'detail' is also supplied");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a contract from a flat scalar 'name' sent without the nested 'args' wrapper (field-test defect #4).")]
	public void ToolContractGet_Should_Resolve_Contract_From_Flat_Name_Scalar() {
		// Arrange
		ToolContractGetTool tool = new();
		JsonElement flat = JsonDocument.Parse("\"get-tool-contract\"").RootElement;
		ToolContractGetArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["name"] = flat
			}
		};

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(args);

		// Assert
		result.Success.Should().BeTrue(
			because: "a flat scalar 'name' must resolve a single contract instead of failing");
		result.Tools.Should().ContainSingle(contract => contract.Name == ToolContractGetTool.ToolName,
			because: "the flat 'name' value must resolve to its contract");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the helpful 'Expected args shape' hint for a genuinely-unknown wrong-shape key instead of an opaque error (field-test defect #4).")]
	public void ToolContractGet_Should_Return_Expected_Shape_Hint_For_Wrong_Shape() {
		// Arrange
		ToolContractGetTool tool = new();
		JsonElement element = JsonDocument.Parse("\"list-pages\"").RootElement;
		ToolContractGetArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["wrong-shape"] = element
			}
		};

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(args);

		// Assert
		result.Success.Should().BeFalse(
			because: "an unknown key is not a recoverable flat shape");
		result.Error!.Message.Should().Contain("Expected args shape",
			because: "the wrong-shape path must surface the helpful expected-shape hint rather than an opaque error");
		result.Error.Message.Should().Contain("tool-names",
			because: "the hint must teach the canonical tool-names array shape");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical create-sys-setting contract with reference-schema-name as an input field and warning in the output envelope.")]
	public void ToolContractGet_Should_Return_CreateSysSetting_Contract_With_ReferenceSchemaName_And_Warning() {
		ToolContractGetTool tool = new();

		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			SysSettingCreateTool.CreateSysSettingToolName
		]));

		result.Success.Should().BeTrue(
			because: "create-sys-setting is part of the executable clio MCP contract surface");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "reference-schema-name",
			because: "the contract must advertise reference-schema-name so contract-driven clients can discover the Lookup creation path");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "warning",
			because: "the contract must advertise the optional partial-success warning that the tool actually returns when the row is created but the initial value cannot be applied");
		contract.InputSchema.Properties.Select(field => field.Name).Should().NotContain("Binary",
			because: "Binary is intentionally excluded from the advertised value-type-name surface");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical list-sys-settings contract documenting Binary exclusion in its description.")]
	public void ToolContractGet_Should_Return_ListSysSettings_Contract_With_Binary_Exclusion_Note() {
		ToolContractGetTool tool = new();

		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			SysSettingsListTool.ListSysSettingsToolName
		]));

		result.Success.Should().BeTrue(
			because: "list-sys-settings is part of the executable clio MCP contract surface");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Description.Should().Contain("Binary",
			because: "the description must call out Binary exclusion so contract-driven clients understand why those entries are absent");
		contract.Description.Should().Contain("excluded",
			because: "the description must clarify Binary is not just hidden, but unsupported through this tool set");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical install-gate contract so an MCP-only agent can discover the cliogate remediation tool.")]
	public void ToolContractGet_Should_Return_InstallGate_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			InstallGateTool.InstallGateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "install-gate must be discoverable through get-tool-contract so gate-dependent flows are completable from MCP");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(InstallGateTool.InstallGateToolName,
			because: "the requested tool contract should be returned verbatim");
		contract.InputSchema.Required.Should().ContainSingle(required => required == "environment-name",
			because: "install-gate targets one registered environment");
		contract.OutputContract.Kind.Should().Be("command-execution-result",
			because: "install-gate returns the standard command execution result payload");
		contract.PreferredFlow.Tools.Should().Equal(
			new[] {
				InstallGateTool.InstallGateToolName,
				RestoreWorkspaceTool.RestoreWorkspaceToolName
			},
			because: "the contract should advertise installing the gate before retrying the gate-dependent flow");
	}
	[Test]
	[Category("Unit")]
	[Description("Exposes curated contracts for the deploy lifecycle tools so the most consequential tools are discoverable.")]
	public void ToolContractGet_Should_Return_Curated_Lifecycle_Contracts() {
		// Arrange
		ToolContractGetTool tool = new();
		string[] requestedTools = [
			AssertInfrastructureTool.AssertInfrastructureToolName,
			ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName,
			FindEmptyIisPortTool.FindEmptyIisPortToolName,
			InstallerCommandTool.DeployCreatioToolName,
			RestoreWorkspaceTool.RestoreWorkspaceToolName,
			PushWorkspaceTool.PushWorkspaceToolName
		];

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(requestedTools));

		// Assert
		result.Success.Should().BeTrue(
			because: "all six deploy lifecycle tools must be discoverable through get-tool-contract");
		result.Tools!.Select(contract => contract.Name).Should().BeEquivalentTo(requestedTools,
			because: "the curated lifecycle contract set should be retrievable as one consistent set");

		ToolContractDefinition deploy = result.Tools!.Single(contract =>
			contract.Name == InstallerCommandTool.DeployCreatioToolName);
		deploy.InputSchema.Required.Should().Contain(["siteName", "zipFile", "sitePort"],
			because: "deploy-creatio requires the site name, build archive, and port");
		deploy.OutputContract.Kind.Should().Be("command-execution-result",
			because: "deploy-creatio returns the standard command execution result payload");
		deploy.PreferredFlow.Tools.Should().Equal(
			new[] {
				AssertInfrastructureTool.AssertInfrastructureToolName,
				ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName,
				FindEmptyIisPortTool.FindEmptyIisPortToolName,
				InstallerCommandTool.DeployCreatioToolName
			},
			because: "deploy-creatio should advertise the canonical deploy preflight order");
		deploy.Preconditions.Should().NotBeNullOrEmpty(
			because: "the most consequential tool must spell out its preconditions");

		ToolContractDefinition restore = result.Tools!.Single(contract =>
			contract.Name == RestoreWorkspaceTool.RestoreWorkspaceToolName);
		restore.InputSchema.Required.Should().Contain(["environment-name", "workspace-path"],
			because: "restore-workspace needs the environment and the local workspace path");
		restore.Preconditions.Should().Contain(precondition =>
			precondition.Contains("cliogate", StringComparison.Ordinal) &&
			precondition.Contains("install-gate", StringComparison.Ordinal),
			because: "restore-workspace should tell callers how to satisfy the cliogate prerequisite");

		ToolContractDefinition assert = result.Tools!.Single(contract =>
			contract.Name == AssertInfrastructureTool.AssertInfrastructureToolName);
		assert.InputSchema.Properties.Should().BeEmpty(
			because: "assert-infrastructure takes no parameters");
		assert.OutputContract.Fields.Should().Contain(field => field.Name == "database-candidates",
			because: "assert-infrastructure should advertise the normalized database candidates it returns");
	}
	[Test]
	[Category("Unit")]
	[Description("Falls back to a schema-derived contract for a registered tool that has no curated contract instead of returning tool-not-found.")]
	public void ToolContractGet_Should_Fall_Back_To_Schema_For_Uncurated_Registered_Tool() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			DownloadConfigurationTool.DownloadConfigurationByEnvironmentToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "a registered-but-uncurated tool should resolve through the input-schema fallback");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(DownloadConfigurationTool.DownloadConfigurationByEnvironmentToolName,
			because: "the fallback should return the requested tool verbatim");
		contract.Description.Should().Contain("Auto-generated from the tool input schema",
			because: "the fallback contract should mark itself as schema-derived so callers know it is uncurated");
		contract.InputSchema.Properties.Select(field => field.Name).Should().Contain(["environment-name", "workspace-path"],
			because: "the fallback should surface the tool's real input fields from its args record");
		contract.InputSchema.Required.Should().Contain(["environment-name", "workspace-path"],
			because: "the fallback should detect [Required] declared on the args constructor parameters");
	}
	[Test]
	[Category("Unit")]
	[Description("Still returns tool-not-found with suggestions for a name that matches no registered tool.")]
	public void ToolContractGet_Should_Return_NotFound_For_Unknown_Tool() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			"definitely-not-a-real-tool"
		]));

		// Assert
		result.Success.Should().BeFalse(
			because: "a name that matches no curated and no registered tool must still fail");
		result.Error!.Code.Should().Be("tool-not-found",
			because: "the fallback must not mask genuinely unknown tool names");
		result.Error.Suggestions.Should().NotBeNullOrEmpty(
			because: "the error should still offer nearest-name suggestions");
		result.Error.Message.Should().EndWith(ToolContractGetTool.DiscoveryHint,
			because: "the discovery hint must trail the tool-not-found message so a missed guess has the full-catalog path");
		result.Error.Message.Should().Contain("compact index of every tool",
			because: "the hint must name the cheap compact-index discovery route");
	}
	[Test]
	[Category("Unit")]
	[Description("Returns the canonical list-creatio-builds contract so an agent can discover the build-discovery tool.")]
	public void ToolContractGet_Should_Return_ListCreatioBuilds_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ListCreatioBuildsTool.ListCreatioBuildsToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "list-creatio-builds must be discoverable through get-tool-contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(ListCreatioBuildsTool.ListCreatioBuildsToolName,
			because: "the requested tool contract should be returned verbatim");
		contract.InputSchema.Properties.Should().BeEmpty(
			because: "list-creatio-builds takes no parameters and reads the configured products folder");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "builds",
			because: "the contract should advertise the discovered build archives");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "products-folder",
			because: "the contract should advertise the resolved products folder so a stale configuration is visible");
		contract.PreferredFlow.Tools.Should().Equal(
			new[] {
				ListCreatioBuildsTool.ListCreatioBuildsToolName,
				InstallerCommandTool.DeployCreatioToolName
			},
			because: "build discovery should flow into deploy-creatio with the chosen archive");
	}

	// Codex review #2 (PR #743): the no-args compact index was built ONLY from the curated canonical set,
	// so a lazy-mode client following the "compact index of every tool" guidance could not discover the
	// hidden long tail (stop-creatio, add-package-dependency, ...) it must reach through clio-run. The
	// index now spans EVERY invokable tool (curated core + registry-invokable hidden tools). This guard
	// pins the union behavior and refutes the "hidden tools omitted from the index" regression.
	[Test]
	[Category("Unit")]
	[Description("The no-args compact index includes hidden long-tail tools (stop-creatio, add-package-dependency) alongside the curated core, so a lazy-mode client can discover every invokable tool from the index.")]
	public void ToolContractGet_Should_Include_HiddenInvokableTools_In_Compact_Index() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Success.Should().BeTrue(
			because: "the no-args request is the default compact-discovery entry point");
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		IReadOnlyList<ToolContractIndexEntry> index = result.Index!;
		index.Select(entry => entry.Name).Should().Contain([
				"stop-creatio",
				AddPackageDependencyTool.AddPackageDependencyToolName
			],
			because: "the compact index must list hidden, clio-run-invokable tools so a lazy-mode client can discover them without already knowing the exact name");
		index.Select(entry => entry.Name).Should().Contain([
				GuidanceGetTool.ToolName,
				PageUpdateTool.ToolName
			],
			because: "extending the index to the long tail must not drop the curated core tools");
	}

	[Test]
	[Category("Unit")]
	[Description("The hidden long-tail entries in the no-args compact index carry a non-empty purpose and the correct destructive flag derived from the tool annotation.")]
	public void ToolContractGet_Should_Populate_Purpose_And_Destructive_For_HiddenIndexEntries() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		IReadOnlyList<ToolContractIndexEntry> index = result.Index!;
		ToolContractIndexEntry stopCreatio = index.Single(entry => entry.Name == "stop-creatio");
		stopCreatio.Purpose.Should().NotBeNullOrWhiteSpace(
			because: "a hidden tool must carry a non-empty one-line purpose so the agent can choose it without the full schema");
		stopCreatio.Destructive.Should().BeTrue(
			because: "stop-creatio is annotated Destructive = true on its MCP tool method");
		stopCreatio.ContractAvailable.Should().BeTrue(
			because: "stop-creatio resolves a registry-derived contract, so its index entry is expandable");
		ToolContractIndexEntry addDependency = index.Single(entry =>
			entry.Name == AddPackageDependencyTool.AddPackageDependencyToolName);
		addDependency.Purpose.Should().NotBeNullOrWhiteSpace(
			because: "add-package-dependency must carry a non-empty one-line purpose distilled from its description");
		addDependency.Destructive.Should().BeFalse(
			because: "add-package-dependency is annotated Destructive = false on its MCP tool method");
	}

	[Test]
	[Category("Unit")]
	[Description("Every no-args compact index entry built over the real registry keeps a one-line purpose under the length cap, even after extending the index to the hidden long tail.")]
	public void ToolContractGet_Should_Keep_Index_Compact_When_Including_HiddenTools() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		result.Index!.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.Purpose),
			because: "every index entry — curated or hidden — must carry a non-empty one-line purpose");
		result.Index!.Should().OnlyContain(entry => entry.Purpose.Length <= 120,
			because: "the purpose must stay a single short line so the long-tail index remains cheap");
	}

	// PR #743 follow-up: the uncurated registry/reflection contract appends an "Auto-generated … no curated
	// contract yet" note to its Description. Because a hidden tool's raw description often has no terminating
	// period (e.g. stop-creatio's "Stops Creatio instance by environment name"), BuildPurpose could not cut
	// at a sentence boundary and the index one-liner leaked the meta-note (e.g. "Stops Creatio instance by
	// environment name Auto-ge…"). The note describes the ABSENCE of curation, not what the tool does, so it
	// is noise in a compact index. These guards pin: (a) the index one-liner is noteless functional text,
	// and (b) the FULL named contract still carries the note.
	[Test]
	[Category("Unit")]
	[Description("A hidden long-tail tool's compact-index purpose carries only the raw functional description, never the uncurated 'Auto-generated … no curated contract' note.")]
	public void ToolContractGet_Should_Strip_UncuratedNote_From_HiddenIndexPurpose() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		ToolContractIndexEntry stopCreatio = result.Index!.Single(entry => entry.Name == "stop-creatio");
		stopCreatio.Purpose.Should().NotContain("Auto-generated",
			because: "the compact index one-liner must describe what the tool does, not announce that no curated contract exists");
		stopCreatio.Purpose.Should().NotContain("no curated contract",
			because: "the absence-of-curation meta-note is noise in a compact discovery index");
		stopCreatio.Purpose.Should().Be("Stops Creatio instance by environment name",
			because: "the purpose must be the tool's raw functional description, not the description merged with and truncated against the meta-note");
	}

	[Test]
	[Category("Unit")]
	[Description("A hidden tool with a no-period description (add-package-dependency) yields a noteless functional compact-index purpose, proving the strip is independent of sentence-boundary detection.")]
	public void ToolContractGet_Should_Strip_UncuratedNote_From_HiddenIndexPurpose_When_Description_Has_No_SentenceBreak() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Index.Should().NotBeNullOrEmpty(
			because: "the no-args default must populate the compact index");
		ToolContractIndexEntry addDependency = result.Index!.Single(entry =>
			entry.Name == AddPackageDependencyTool.AddPackageDependencyToolName);
		addDependency.Purpose.Should().NotContain("Auto-generated",
			because: "stripping the note must not depend on the description ending in a sentence-terminating period");
		addDependency.Purpose.Should().NotContain("no curated contract",
			because: "the meta-note must be removed before the purpose is distilled, for every uncurated tool");
		addDependency.Purpose.Should().StartWith("Adds one or more package dependencies",
			because: "the purpose must be distilled from the tool's own functional description");
	}

	[Test]
	[Category("Unit")]
	[Description("The FULL named contract for a hidden long-tail tool still carries the 'Auto-generated … no curated contract' note, proving the note was stripped only from the compact index, not from the full contract.")]
	public void ToolContractGet_Should_Keep_UncuratedNote_In_FullNamedContract_ForHiddenTool() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(["stop-creatio"]));

		// Assert
		result.Success.Should().BeTrue(
			because: "stop-creatio is invokable via clio-run-destructive and must expose a discoverable full contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Description.Should().Contain("Auto-generated",
			because: "the full named contract must keep the note so a caller knows the contract is auto-derived, not curated");
		contract.Description.Should().Contain("no curated contract",
			because: "the note correctly signals the uncurated nature only in the full contract; the index one-liner stays noteless");
		contract.Description.Should().StartWith("Stops Creatio instance by environment name",
			because: "the full contract still leads with the tool's functional description before the appended note");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical get-component-info contract with composite= in the input schema, "
		+ "composite-mode output fields, a composite example, and a synthesize anti-pattern. "
		+ "Agents that call get-tool-contract to introspect get-component-info must see composite= "
		+ "so they know to call composite=\"Expanded list\" instead of component-type=\"Expanded list\".")]
	public void ToolContractGet_Should_Return_ComponentInfo_Contract_With_Composite_Parameter() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ComponentInfoTool.ToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-component-info is a canonical clio MCP tool and must be discoverable through get-tool-contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(ComponentInfoTool.ToolName,
			because: "the contract name must stay stable for clients that bootstrap from the contract tool");

		contract.InputSchema.Properties.Should().Contain(field => field.Name == "composite",
			because: "agents calling get-tool-contract must see composite= so they know the parameter exists "
				+ "and call composite=\"Expanded list\" instead of component-type=\"Expanded list\"");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "component-type",
			because: "the component-type parameter must remain documented alongside composite");
		contract.InputSchema.Validators.Should().NotBeNull(
			because: "composite and component-type are mutually exclusive and the constraint must be machine-readable");
		contract.InputSchema.Validators!.Should().Contain(v =>
				v.Fields != null && v.Fields.Contains("composite") && v.Fields.Contains("component-type"),
			because: "the mutually-exclusive validator must name both fields so agents know they cannot be combined");

		contract.OutputContract.Fields.Should().Contain(field => field.Name == "documentation",
			because: "composite detail mode returns the assembly recipe in the documentation field");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "composites",
			because: "list mode returns a composites array so agents can discover composite captions");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "caption",
			because: "composite detail mode echoes the matched composite caption");

		contract.Examples.Should().Contain(example => example.Arguments.ContainsKey("composite"),
			because: "the contract must include a composite= example so agents have a concrete call to follow");

		contract.AntiPatterns.Should().NotBeNull(
			because: "an anti-pattern is required to stop agents from synthesizing the composite structure from memory");
		contract.AntiPatterns!.Should().Contain(p =>
				p.Why.Contains("synthesize", StringComparison.OrdinalIgnoreCase) ||
				p.Pattern.Contains("synthesize", StringComparison.OrdinalIgnoreCase),
			because: "the anti-pattern must explicitly warn against synthesizing instead of following the documentation field");
	}

	[Test]
	[Category("Unit")]
	[Description("The get-component-info contract advertises 'component-name' (plus its camelCase/snake_case spellings) as a rejected alias of 'component-type', so a contract-driven validator steers an agent that reaches for the wrong-WORD selector to the canonical parameter instead of a generic 'unknown parameter' guess.")]
	[TestCase("component-name")]
	[TestCase("componentName")]
	[TestCase("component_name")]
	public void ToolContractGet_Should_Advertise_ComponentName_As_Rejected_Alias_Of_ComponentType(string spelling) {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ComponentInfoTool.ToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-component-info must be discoverable through get-tool-contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Aliases.Should().Contain(alias =>
				alias.Alias == spelling
				&& alias.CanonicalName == "component-type"
				&& alias.Status == "rejected"
				&& alias.Message.Contains("component-type"),
			because: "an agent that passes the wrong-WORD selector must be redirected to 'component-type' rather than left to guess");
	}
}
