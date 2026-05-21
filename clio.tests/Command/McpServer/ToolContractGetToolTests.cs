using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ToolContractGetToolTests {
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

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical clio MCP contract set when the request omits tool names.")]
	public void ToolContractGet_Should_Return_Canonical_Contracts_When_Request_Is_Empty() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Success.Should().BeTrue(
			because: "an empty request should be a valid bootstrap entry point for canonical contract discovery");
		result.Error.Should().BeNull(
			because: "a successful bootstrap lookup should not return a structured error");
		result.Tools.Should().NotBeNull(
			because: "the bootstrap response should include the canonical contract set");
		result.Tools!.Select(contract => contract.Name).Should().Contain([
				GuidanceGetTool.ToolName,
				SettingsHealthTool.ToolName,
				ApplicationGetListTool.ApplicationGetListToolName,
				ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
				ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName,
				CreateEntityBusinessRuleTool.BusinessRuleCreateToolName,
				CreatePageBusinessRuleTool.BusinessRuleCreateToolName,
				DataForgeTool.DataForgeContextToolName,
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
		contract.InputSchema.Required.Should().Contain(["environment-name", "package-name", "entity-schema-name", "rule"],
			because: "entity-business-rule creation requires environment package entity and rule payload");
		contract.InputSchema.Validators.Should().Contain(validator =>

				validator.Name == "enum" &&
				validator.Field == "rule.condition.logicalOperation",
			because: "the contract should validate the target architecture logicalOperation field");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rule.condition.conditions[*].comparisonType",
			because: "the contract should validate target-architecture comparisonType fields");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "conditional-field" &&
				validator.Field == "rule.condition.conditions[*].rightExpression" &&
				validator.Context!.Contains("Omit or null for is-filled-in and is-not-filled-in", StringComparison.Ordinal),
			because: "the contract should advertise the unary-versus-binary rightExpression rule");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "comparison-family" &&
				validator.Field == "rule.condition.conditions[*]" &&
				validator.Context!.Contains("date/time left attributes", StringComparison.Ordinal),
			because: "the contract should advertise the numeric and date/time scope of relational comparisons");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "comparison-family" &&
				validator.Code == "unsupported-equality-operands" &&
				validator.Field == "rule.condition.conditions[*]" &&
				validator.Context!.Contains("RichText or Image", StringComparison.Ordinal),
			because: "the contract should advertise Creatio's equality limitation for rich text and image columns");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "date-time-constant" &&
				validator.Field == "rule.condition.conditions[*].rightExpression.value" &&
				validator.Context!.Contains("timezone suffix", StringComparison.Ordinal),
			because: "the contract should explicitly require timezone-aware DateTime and Time constants");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rule.actions[*].type",
			because: "the contract should validate target-architecture action type fields");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rule.actions[*].type" &&
				validator.Context!.Contains("set-values", StringComparison.Ordinal),
			because: "the contract should advertise the Set values action type");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "set-values-shape" &&
				validator.Field == "rule.actions[*].items[*]" &&
				validator.Context!.Contains("forward reference paths like LookupColumn.SourceColumn", StringComparison.Ordinal),
			because: "the contract should advertise AttributeValue source assignments in the current set-values scope");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "set-values-constant" &&
				validator.Field == "rule.actions[*].items[*].value.value" &&
				validator.Context!.Contains("JSON number", StringComparison.Ordinal),
			because: "the contract should document typed constant payloads for set-values");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "set-values-formula" &&
				validator.Field == "rule.actions[*].items[*].value.expression" &&
				validator.Context!.Contains("ExpressionService.svc/Validate", StringComparison.Ordinal),
			because: "the contract should document remote formula validation after expression-schema translation");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "set-values-shape" &&
				validator.Context!.Contains("direct-field arithmetic expression", StringComparison.Ordinal),
			because: "the contract should document the current simple direct-field formula scope");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rule.condition.conditions[*].comparisonType" &&
				validator.Context!.Contains("greater-than-or-equal", StringComparison.Ordinal),
			because: "the contract should advertise the full supported comparison set");
		contract.Defaults.Should().BeEmpty(
			because: "the contract should not have defaults after enabled was removed");
		contract.Aliases.Should().BeEmpty(
			because: "the contract should avoid duplicating rejected aliases already represented by the runtime tool schema");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "exit-code",
			because: "the output contract should advertise the command exit code that the tool actually returns");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "execution-log-messages",
			because: "the output contract should advertise the execution log messages");
		contract.OutputContract.Kind.Should().Be("command-execution-result",
			because: "create-entity-business-rule returns the standard command execution result payload");
		contract.OutputContract.SuccessField.Should().BeNull(
			because: "command execution result payloads do not include a success field");
		contract.OutputContract.FailureSignals.Should().Contain("exit-code != 0",
			because: "contract-driven clients should detect command failures from the exit code");
		contract.OutputContract.FailureSignals.Should().NotContain("success == false",
			because: "create-entity-business-rule does not emit a success field");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "rule",
			because: "tool-contract-get should not advertise a structured rule payload that create-entity-business-rule does not return");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "package-u-id",
			because: "tool-contract-get should not advertise package identifiers that the create-entity-business-rule tool does not return");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "entity-schema-u-id",
			because: "tool-contract-get should not advertise entity identifiers that the create-entity-business-rule tool does not return");
		contract.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
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
			example.Arguments["rule"] is Dictionary<string, object?> rule
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
			example.Arguments["rule"] is Dictionary<string, object?> rule
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
			example.Arguments["rule"] is Dictionary<string, object?> rule
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
			example.Arguments["rule"] is Dictionary<string, object?> rule
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
			example.Arguments["rule"] is Dictionary<string, object?> rule
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
			example.Arguments["rule"] is Dictionary<string, object?> rule
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
			because: "the contract should include a set-values example with text number boolean Date DateTime and Time constants");
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
		contract.InputSchema.Required.Should().Contain(["environment-name", "package-name", "page-schema-name", "rule"],
			because: "page-business-rule creation requires environment package page and rule payload");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rule.actions[*].type" &&
				validator.Context!.Contains("hide-element", StringComparison.Ordinal) &&
				validator.Context.Contains("show-element", StringComparison.Ordinal) &&
				validator.Context.Contains("make-editable", StringComparison.Ordinal) &&
				validator.Context.Contains("make-read-only", StringComparison.Ordinal) &&
				validator.Context.Contains("make-required", StringComparison.Ordinal) &&
				validator.Context.Contains("make-optional", StringComparison.Ordinal),
			because: "the page rule contract should advertise all supported page actions");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "page-element" &&
				validator.Field == "rule.actions[*].items" &&
				validator.Context!.Contains("recursive get-page bundle.viewConfig", StringComparison.Ordinal),
			because: "the contract should direct callers to recursive page element discovery");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "page-attribute" &&
				validator.Field == "rule.condition.conditions[*].leftExpression.path",
			because: "page rule conditions must use declared page view-model attributes");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "conditional-field" &&
				validator.Field == "rule.condition.conditions[*].rightExpression" &&
				validator.Context!.Contains("Omit or null for is-filled-in and is-not-filled-in", StringComparison.Ordinal),
			because: "page rules share the business-rule unary-versus-binary rightExpression contract");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "comparison-family" &&
				validator.Code == "unsupported-relational-operands" &&
				validator.Field == "rule.condition.conditions[*]" &&
				validator.Context!.Contains("date/time left attributes", StringComparison.Ordinal),
			because: "page rules share the numeric and date/time scope for relational comparisons");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "date-time-constant" &&
				validator.Field == "rule.condition.conditions[*].rightExpression.value" &&
				validator.Context!.Contains("timezone suffix", StringComparison.Ordinal),
			because: "page rules share the DateTime and Time constant timezone requirement");
		contract.OutputContract.Kind.Should().Be("command-execution-result",
			because: "create-page-business-rule returns the standard command execution result payload");
		contract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
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

	private static Func<ToolContractExample, bool> HasPageActionExample(string actionType) =>
		example =>
			example.Arguments["rule"] is Dictionary<string, object?> rule
			&& rule.TryGetValue("actions", out object? actionsValue)
			&& actionsValue is object[] actions
			&& actions.OfType<Dictionary<string, object?>>().Any(action =>
				string.Equals(action["type"]?.ToString(), actionType, StringComparison.Ordinal));

	private static bool HasSetValuesConstantExample(ToolContractExample example) {
		if (example.Arguments["rule"] is not Dictionary<string, object?> rule
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
			&& values.Contains("2025-01-01T00:00:00Z");
	}

	private static bool HasSetValuesFormulaExample(ToolContractExample example) {
		if (example.Arguments["rule"] is not Dictionary<string, object?> rule
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
		if (example.Arguments["rule"] is not Dictionary<string, object?> rule
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
		contract.InputSchema.Required.Should().Contain(["environment-name", "application-code", "caption"],
			because: "section-create requires environment-name, application-code, and caption as the minimal payload");
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
		contract.InputSchema.Required.Should().Contain(["environment-name", "application-code", "section-code"],
			because: "section-update requires environment-name, application-code, and section-code as the selector payload");
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
}
