using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for entity-level Freedom UI business-rule creation.
/// </summary>
[McpServerToolType]
public sealed class CreateEntityBusinessRuleTool(
	CreateEntityBusinessRuleCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateEntityBusinessRuleOptions>(command, logger, commandResolver) {

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
			CreateEntityBusinessRuleOptions options = CreateOptions(args);
			CreateEntityBusinessRuleCommand resolvedCommand = ResolveCommand<CreateEntityBusinessRuleCommand>(options);
			BusinessRuleCreateResult result = resolvedCommand.Create(options);
			return BusinessRuleToolSupport.CreateResponse(BusinessRuleToolResultMapper.Map(args, result));
		} catch (Exception ex) {
			return BusinessRuleToolSupport.CreateErrorResponse(ex.Message);
		}
	}

	internal static CreateEntityBusinessRuleOptions CreateOptions(BusinessRuleCreateArgs args) {
		return new CreateEntityBusinessRuleOptions {
			EnvironmentName = args.EnvironmentName,
			PackageName = args.PackageName,
			EntitySchemaName = args.EntitySchemaName,
			Rule = MapRule(args.Rule)
		};
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
		if (operand is BusinessRuleAttributeExpressionArgs attributeOperand) {
			return new BusinessRuleOperand(
				MapExpressionKind(attributeOperand.Type),
				attributeOperand.Path,
				null,
				null);
		}

		if (operand is BusinessRuleValueExpressionArgs valueOperand) {
			return new BusinessRuleOperand(
				MapExpressionKind(valueOperand.Type),
				null,
				valueOperand.Value?.Clone(),
				null);
		}

		return new BusinessRuleOperand(
			MapExpressionKind(operand.Type),
			null,
			null,
			null);
	}

	private static string MapExpressionKind(string expressionType) {
		if (string.Equals(expressionType, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			return "attribute";
		}

		if (string.Equals(expressionType, "Const", StringComparison.OrdinalIgnoreCase)) {
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
	[property: Description("Right expression. Supports AttributeValue or Const. For lookup constants pass only a GUID string; reference schema is resolved automatically.")]
	[property: Required]
	BusinessRuleExpressionArgs RightExpression
);

/// <summary>
/// Base structured expression accepted by the MCP tool.
/// </summary>
[JsonConverter(typeof(BusinessRuleExpressionArgsJsonConverter))]
public abstract record BusinessRuleExpressionArgs(
	[property: JsonPropertyName("type")]
	[property: Description("Expression type. Supported values: AttributeValue, Const.")]
	[property: Required]
	string Type
) { }

/// <summary>
/// Structured attribute expression accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleAttributeExpressionArgs(
	string Type
) : BusinessRuleExpressionArgs(Type) {
	[JsonPropertyName("path")]
	[Description("Attribute path when type is AttributeValue.")]
	public string? Path { get; init; }
}

/// <summary>
/// Structured constant expression accepted by the MCP tool.
/// </summary>
public sealed record BusinessRuleValueExpressionArgs(
	string Type
) : BusinessRuleExpressionArgs(Type) {
	[JsonPropertyName("value")]
	[Description("Constant value when type is Const. For lookup constants pass a GUID string; do not pass referenceSchemaName.")]
	public System.Text.Json.JsonElement? Value { get; init; }
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

internal sealed class BusinessRuleExpressionArgsJsonConverter : JsonConverter<BusinessRuleExpressionArgs> {
	public override BusinessRuleExpressionArgs? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		string rawJson = document.RootElement.GetRawText();
		if (document.RootElement.TryGetProperty("path", out _)) {
			return JsonSerializer.Deserialize<BusinessRuleAttributeExpressionArgs>(rawJson, options);
		}

		return JsonSerializer.Deserialize<BusinessRuleValueExpressionArgs>(rawJson, options);
	}

	public override void Write(Utf8JsonWriter writer, BusinessRuleExpressionArgs value, JsonSerializerOptions options) {
		JsonSerializer.Serialize(writer, value, value.GetType(), options);
	}
}
