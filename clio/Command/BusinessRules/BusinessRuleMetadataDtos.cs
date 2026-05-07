using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules;

internal sealed class BusinessRuleMetadataDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = BusinessRuleConstants.BusinessRuleTypeName;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = string.Empty;

	[JsonPropertyName("cases")]
	public List<BusinessRuleCaseMetadataDto> Cases { get; set; } = [];

	[JsonPropertyName("triggers")]
	public List<BusinessRuleTriggerMetadataDto> Triggers { get; set; } = [];

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("enabled")]
	public bool? Enabled { get; set; }

	[JsonPropertyName("caption")]
	public string? Caption { get; set; }
}

internal sealed class BusinessRuleCaseMetadataDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = BusinessRuleConstants.BusinessRuleCaseTypeName;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = string.Empty;

	[JsonPropertyName("condition")]
	public BusinessRuleGroupConditionMetadataDto? Condition { get; set; }

	[JsonPropertyName("actions")]
	public List<FieldSelectionBusinessRuleActionMetadataDto> Actions { get; set; } = [];
}

internal abstract class BaseBusinessRuleConditionMetadataDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = string.Empty;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = string.Empty;
}

internal sealed class BusinessRuleGroupConditionMetadataDto : BaseBusinessRuleConditionMetadataDto {
	[JsonPropertyName("logicalOperation")]
	public int LogicalOperation { get; set; }

	[JsonPropertyName("conditions")]
	public List<BusinessRuleConditionMetadataDto> Conditions { get; set; } = [];
}

internal sealed class BusinessRuleConditionMetadataDto : BaseBusinessRuleConditionMetadataDto {
	[JsonPropertyName("leftExpression")]
	public BusinessRuleExpressionMetadataDto LeftExpression { get; set; } = default!;

	[JsonPropertyName("rightExpression")]
	public BusinessRuleExpressionMetadataDto? RightExpression { get; set; }

	[JsonPropertyName("comparisonType")]
	public int ComparisonType { get; set; }
}

internal abstract class BaseBusinessRuleActionMetadataDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = string.Empty;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = string.Empty;

	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; }
}

// Hand-written serialization so SetFilterBusinessRuleActionMetadataDto, which inherits
// from FieldSelectionBusinessRuleActionMetadataDto, emits its own Expression / Value
// properties when the case-level Actions list carries the field-selection static type.
// STJ resolves converters by the declared element type, not the runtime type, so writing
// properties manually is the simplest way to keep the case Actions list element type stable
// while still emitting the SetFilter shape.
internal sealed class BusinessRuleActionMetadataDtoJsonConverter
	: JsonConverter<FieldSelectionBusinessRuleActionMetadataDto> {
	public override FieldSelectionBusinessRuleActionMetadataDto Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options) =>
		throw new NotSupportedException(
			"Reading FieldSelectionBusinessRuleActionMetadataDto from JSON is not supported; clio writes add-on metadata only.");

	public override void Write(
		Utf8JsonWriter writer,
		FieldSelectionBusinessRuleActionMetadataDto value,
		JsonSerializerOptions options) {
		writer.WriteStartObject();
		writer.WriteString("typeName", value.TypeName);
		writer.WriteString("uId", value.UId);
		writer.WriteBoolean("enabled", value.Enabled);
		if (value is SetFilterBusinessRuleActionMetadataDto setFilter) {
			writer.WritePropertyName("expression");
			JsonSerializer.Serialize(writer, setFilter.Expression, options);
			writer.WritePropertyName("value");
			JsonSerializer.Serialize(writer, setFilter.Value, options);
		} else if (value.Items is not null) {
			writer.WritePropertyName("items");
			JsonSerializer.Serialize(writer, value.Items, options);
		}
		writer.WriteEndObject();
	}
}

[JsonConverter(typeof(BusinessRuleActionMetadataDtoJsonConverter))]
internal class FieldSelectionBusinessRuleActionMetadataDto : BaseBusinessRuleActionMetadataDto {
	[JsonPropertyName("items")]
	public object? Items { get; set; }
}

internal sealed class SetFilterBusinessRuleActionMetadataDto : FieldSelectionBusinessRuleActionMetadataDto {
	[JsonPropertyName("expression")]
	public BusinessRuleExpressionMetadataDto Expression { get; set; } = default!;

	[JsonPropertyName("value")]
	public BusinessRuleExpressionMetadataDto Value { get; set; } = default!;
}

internal sealed class BusinessRuleSetValueItemMetadataDto : BaseBusinessRuleActionMetadataDto {
	[JsonPropertyName("expression")]
	public BusinessRuleExpressionMetadataDto Expression { get; set; } = default!;

	[JsonPropertyName("value")]
	public BusinessRuleExpressionMetadataDto Value { get; set; } = default!;
}

internal sealed class BusinessRuleTriggerMetadataDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = BusinessRuleConstants.BusinessRuleTriggerTypeName;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = string.Empty;

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("type")]
	public int Type { get; set; }
}

internal sealed class BusinessRuleExpressionMetadataDto {
	// AddonSchemaDesignerService reads business-rule metadata sequentially and parses `value`
	// according to the already-read `dataValueTypeName`. Keep base expression fields ordered
	// ahead of derived payload fields to avoid numeric constants being treated as default Text.
	[JsonPropertyOrder(0)]
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = string.Empty;

	[JsonPropertyOrder(1)]
	[JsonPropertyName("uId")]
	public string UId { get; set; } = string.Empty;

	[JsonPropertyOrder(2)]
	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;

	[JsonPropertyOrder(3)]
	[JsonPropertyName("dataValueTypeName")]
	public string? DataValueTypeName { get; set; }

	[JsonPropertyOrder(4)]
	[JsonPropertyName("referenceSchemaName")]
	public string? ReferenceSchemaName { get; set; }

	[JsonPropertyOrder(5)]
	[JsonPropertyName("path")]
	public string? Path { get; set; }

	[JsonPropertyOrder(6)]
	[JsonPropertyName("value")]
	public object? Value { get; set; }
}
