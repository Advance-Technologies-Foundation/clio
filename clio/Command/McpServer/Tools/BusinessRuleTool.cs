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
/// MCP tool surface for object-level Freedom UI business-rule creation.
/// </summary>
[McpServerToolType]
public sealed class BusinessRuleCreateTool(IBusinessRuleService businessRuleService) {

	/// <summary>
	/// Stable MCP tool name for object business-rule creation.
	/// </summary>
	internal const string BusinessRuleCreateToolName = "business-rule-create";

	/// <summary>
	/// Creates an object-level Freedom UI business rule in the requested package and entity schema.
	/// </summary>
	[McpServerTool(Name = BusinessRuleCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates an object-level Freedom UI business rule by editing the entity BusinessRule add-on through backend MCP.")]
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
				rule.ConditionGroup.Operator,
				rule.ConditionGroup.Conditions.ConvertAll(condition =>
					new BusinessRuleCondition(
						MapOperand(condition.Left),
						condition.Comparison,
						MapOperand(condition.Right)))),
			rule.Actions.ConvertAll(action =>
				new BusinessRuleAction(
					action.Action,
					action.Targets)));
	}

	private static BusinessRuleOperand MapOperand(BusinessRuleOperandArgs operand) {
		return new BusinessRuleOperand(
			operand.Kind,
			operand.Path,
			operand.Value,
			operand.DataValueTypeName);
	}
}

/// <summary>
/// Arguments for the <c>business-rule-create</c> MCP tool.
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
	[property: Description("Structured object business-rule definition.")]
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

	[property: JsonPropertyName("if")]
	[property: Description("Top-level condition group. Supports one group with operator AND or OR.")]
	[property: Required]
	BusinessRuleConditionGroupArgs ConditionGroup,

	[property: JsonPropertyName("then")]
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
	[property: JsonPropertyName("operator")]
	[property: Description("Logical operator for the top-level condition group. Supported values: AND, OR.")]
	[property: Required]
	string Operator,

	[property: JsonPropertyName("conditions")]
	[property: Description("Leaf conditions evaluated by the top-level condition group.")]
	[property: Required]
	List<BusinessRuleConditionArgs> Conditions
);

/// <summary>
/// Structured leaf condition accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleConditionArgs(
	[property: JsonPropertyName("left")]
	[property: Description("Left operand. Must be an attribute reference.")]
	[property: Required]
	BusinessRuleOperandArgs Left,

	[property: JsonPropertyName("comparison")]
	[property: Description("Condition comparison. Supported values: equal, not-equal.")]
	[property: Required]
	string Comparison,

	[property: JsonPropertyName("right")]
	[property: Description("Right operand. Supports attribute or constant.")]
	[property: Required]
	BusinessRuleOperandArgs Right
);

/// <summary>
/// Structured operand accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleOperandArgs(
	[property: JsonPropertyName("kind")]
	[property: Description("Operand kind. Supported values: attribute, constant.")]
	[property: Required]
	string Kind
) {
	[property: JsonPropertyName("path")]
	[property: Description("Attribute path when kind is attribute.")]
	public string? Path { get; init; }

	[property: JsonPropertyName("value")]
	[property: Description("Constant value when kind is constant.")]
	public System.Text.Json.JsonElement? Value { get; init; }

	[property: JsonPropertyName("dataValueTypeName")]
	[property: Description("Optional explicit data type. Must match the referenced attribute type when provided.")]
	public string? DataValueTypeName { get; init; }
}

/// <summary>
/// Structured action accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleActionArgs(
	[property: JsonPropertyName("action")]
	[property: Description("Business-rule action. Supported values: make-editable, make-read-only, make-required, make-optional.")]
	[property: Required]
	string Action,

	[property: JsonPropertyName("targets")]
	[property: Description("One or more target attributes affected by the action.")]
	[property: Required]
	List<string> Targets
);

/// <summary>
/// Structured object business-rule creation envelope.
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
