using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.BusinessRules;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for entity-level Freedom UI business-rule creation.
/// </summary>
[McpServerToolType]
public sealed class CreateEntityBusinessRuleTool(IBusinessRuleService businessRuleService) {

	/// <summary>
	/// Stable MCP tool name for entity business-rule creation.
	/// </summary>
	internal const string BusinessRuleCreateToolName = "create-entity-business-rule";

	/// <summary>
	/// Creates an entity-level Freedom UI business rule in the requested package and entity schema.
	/// </summary>
	[McpServerTool(Name = BusinessRuleCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates an entity-level Freedom UI business rule by editing the entity BusinessRule add-on through backend MCP.")]
	public BusinessRuleCreateResponse BusinessRuleCreate(
		[Description("Parameters: environment-name, package-name, entity-schema-name, rule (all required)")]
		[Required]
		BusinessRuleCreateArgs args) {
		try {
			ValidateArgs(args);
			BusinessRuleCreateResult result = businessRuleService.Create(
				args.EnvironmentName,
				new BusinessRuleCreateRequest(
					args.PackageName,
					args.EntitySchemaName,
					MapRule(args.Rule)));
			return BusinessRuleToolSupport.CreateResponse(BusinessRuleToolResultMapper.Map(args, result));
		} catch (Exception ex) {
			return BusinessRuleToolSupport.CreateErrorResponse(ex.Message);
		}
	}

	private static void ValidateArgs(BusinessRuleCreateArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			throw new ArgumentException("environment-name is required.");
		}

		if (string.IsNullOrWhiteSpace(args.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(args.EntitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}

		if (args.Rule is null) {
			throw new ArgumentException("rule is required.");
		}
	}

	private static BusinessRule MapRule(BusinessRuleArgs rule) {
		return new BusinessRule(
			rule.Caption,
			rule.Enabled,
			new BusinessRuleConditionGroup(
				rule.Condition.LogicalOperation,
				rule.Condition.Conditions.ConvertAll(condition =>
					new BusinessRuleCondition(
						MapOperand(condition.LeftExpression),
						condition.ComparisonType,
						MapOperand(condition.RightExpression)))),
			rule.Actions.ConvertAll(action =>
				new BusinessRuleAction(
					action.Type,
					action.Items)));
	}

	private static BusinessRuleOperand MapOperand(BusinessRuleExpressionArgs operand) {
		return new BusinessRuleOperand(
			MapExpressionKind(operand.Type),
			operand.Path,
			operand.Value,
			operand.DataValueTypeName);
	}

	private static string MapExpressionKind(string expressionType) {
		if (string.Equals(expressionType, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			return "attribute";
		}

		if (string.Equals(expressionType, "ConstValue", StringComparison.OrdinalIgnoreCase)) {
			return "constant";
		}

		return expressionType;
	}
}

/// <summary>
/// Arguments for the <c>create-entity-business-rule</c> MCP tool.
/// </summary>
public sealed record BusinessRuleCreateArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the Creatio environment.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Target entity schema name.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("rule")]
	[property: Description("Structured entity business-rule definition.")]
	[property: Required]
	BusinessRuleArgs Rule
);

/// <summary>
/// Structured business-rule definition accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleArgs(
	[property: JsonPropertyName("caption")]
	[property: Description("Business-rule caption shown to authors.")]
	[property: Required]
	string Caption,

	[property: JsonPropertyName("condition")]
	[property: Description("Top-level condition group in the target architecture contract. Supports one group with logicalOperation AND or OR.")]
	[property: Required]
	BusinessRuleConditionGroupArgs Condition,

	[property: JsonPropertyName("actions")]
	[property: Description("One or more actions to execute when the condition group matches.")]
	[property: Required]
	List<BusinessRuleActionArgs> Actions
) {
	[property: JsonPropertyName("enabled")]
	[property: Description("Whether the rule is enabled. Defaults to true when omitted.")]
	public bool? Enabled { get; init; }

}

/// <summary>
/// Structured top-level condition group accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleConditionGroupArgs(
	[property: JsonPropertyName("logicalOperation")]
	[property: Description("Logical operator for the top-level condition group. Supported values: AND, OR.")]
	[property: Required]
	string LogicalOperation,

	[property: JsonPropertyName("conditions")]
	[property: Description("Leaf conditions evaluated by the top-level condition group.")]
	[property: Required]
	List<BusinessRuleConditionArgs> Conditions
);

/// <summary>
/// Structured leaf condition accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleConditionArgs(
	[property: JsonPropertyName("leftExpression")]
	[property: Description("Left expression. Must be an attribute reference with type AttributeValue.")]
	[property: Required]
	BusinessRuleExpressionArgs LeftExpression,

	[property: JsonPropertyName("comparisonType")]
	[property: Description("Condition comparison. Supported values: equal, not-equal.")]
	[property: Required]
	string ComparisonType,

	[property: JsonPropertyName("rightExpression")]
	[property: Description("Right expression. Supports AttributeValue or ConstValue.")]
	[property: Required]
	BusinessRuleExpressionArgs RightExpression
);

/// <summary>
/// Structured expression accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleExpressionArgs(
	[property: JsonPropertyName("type")]
	[property: Description("Expression type. Supported values: AttributeValue, ConstValue.")]
	[property: Required]
	string Type
) {
	[property: JsonPropertyName("path")]
	[property: Description("Attribute path when type is AttributeValue.")]
	public string? Path { get; init; }

	[property: JsonPropertyName("value")]
	[property: Description("Constant value when type is ConstValue.")]
	public System.Text.Json.JsonElement? Value { get; init; }

	[property: JsonPropertyName("dataValueTypeName")]
	[property: Description("Optional explicit data type. Must match the referenced attribute type when provided.")]
	public string? DataValueTypeName { get; init; }
}

/// <summary>
/// Structured action accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleActionArgs(
	[property: JsonPropertyName("type")]
	[property: Description("Business-rule action. Supported values: make-editable, make-read-only, make-required, make-optional.")]
	[property: Required]
	string Type,

	[property: JsonPropertyName("items")]
	[property: Description("One or more target attributes affected by the action.")]
	[property: Required]
	List<string> Items
);

/// <summary>
/// Structured entity business-rule creation envelope.
/// </summary>
public sealed record BusinessRuleCreateResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("package-name")] string? PackageName = null,
	[property: JsonPropertyName("entity-schema-name")] string? EntitySchemaName = null,
	[property: JsonPropertyName("rule-name")] string? RuleName = null,
	[property: JsonPropertyName("error")] string? Error = null
);

internal static class BusinessRuleToolResultMapper {
	public static BusinessRuleCreateResponse Map(BusinessRuleCreateArgs args, BusinessRuleCreateResult result) {
		return new BusinessRuleCreateResponse(
			true,
			PackageName: args.PackageName,
			EntitySchemaName: args.EntitySchemaName,
			RuleName: result.RuleName);
	}
}

internal static class BusinessRuleToolSupport {
	public static BusinessRuleCreateResponse CreateResponse(BusinessRuleCreateResponse response) {
		return response;
	}

	public static BusinessRuleCreateResponse CreateErrorResponse(string message) {
		return new BusinessRuleCreateResponse(false, Error: message);
	}
}
