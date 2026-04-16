using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules;

#region Wire DTOs

internal sealed class AddonGetRequestDto {
	[JsonPropertyName("addonName")]
	public string AddonName { get; set; } = string.Empty;

	[JsonPropertyName("targetSchemaUId")]
	public Guid TargetSchemaUId { get; set; }

	[JsonPropertyName("targetParentSchemaUId")]
	public Guid TargetParentSchemaUId { get; set; }

	[JsonPropertyName("targetPackageUId")]
	public Guid TargetPackageUId { get; set; }

	[JsonPropertyName("targetSchemaManagerName")]
	public string TargetSchemaManagerName { get; set; } = string.Empty;

	[JsonPropertyName("useFullHierarchy")]
	public bool UseFullHierarchy { get; set; }
}

internal sealed class AddonSchemaResponseDto {
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("schema")]
	public AddonSchemaDto? Schema { get; set; }

	[JsonPropertyName("errorInfo")]
	public ErrorInfoDto? ErrorInfo { get; set; }
}

internal sealed class AddonSaveResponseDto {
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("value")]
	public bool? Value { get; set; }

	[JsonPropertyName("errorInfo")]
	public ErrorInfoDto? ErrorInfo { get; set; }
}

internal sealed class AddonSchemaDto {
	[JsonPropertyName("metaData")]
	public string MetaData { get; set; } = string.Empty;

	[JsonPropertyName("resources")]
	public List<AddonResourceDto> Resources { get; set; } = [];

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

internal sealed class AddonResourceDto {
	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("value")]
	public List<AddonResourceValueDto> Value { get; set; } = [];
}

internal sealed class AddonResourceValueDto {
	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("value")]
	public string Value { get; set; } = string.Empty;
}

internal sealed class ErrorInfoDto {
	[JsonPropertyName("message")]
	public string? Message { get; set; }
}

internal sealed class BusinessRulesAddonMetadata {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = BusinessRuleConstants.BusinessRulesMetadataTypeName;

	[JsonPropertyName("rules")]
	public List<BusinessRuleMetadataDto> Rules { get; set; } = [];
}

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
	public bool Enabled { get; set; }

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
	public BusinessRuleExpressionMetadataDto RightExpression { get; set; } = default!;

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

internal sealed class FieldSelectionBusinessRuleActionMetadataDto : BaseBusinessRuleActionMetadataDto {
	[JsonPropertyName("items")]
	public string Items { get; set; } = string.Empty;
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

internal abstract class BusinessRuleExpressionMetadataDto {
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
}

internal sealed class BusinessRuleAttributeExpressionMetadataDto : BusinessRuleExpressionMetadataDto {
	[JsonPropertyOrder(5)]
	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;
}

internal sealed class BusinessRuleValueExpressionMetadataDto : BusinessRuleExpressionMetadataDto {
	[JsonPropertyOrder(5)]
	[JsonPropertyName("value")]
	public object? Value { get; set; }
}

internal sealed class BusinessRuleExpressionMetadataDtoConverter : JsonConverter<BusinessRuleExpressionMetadataDto> {
	public override BusinessRuleExpressionMetadataDto? Read(
		ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		using JsonDocument doc = JsonDocument.ParseValue(ref reader);
		string rawJson = doc.RootElement.GetRawText();
		if (doc.RootElement.TryGetProperty("typeName", out JsonElement typeNameElement)) {
			string? typeName = typeNameElement.GetString();
			if (string.Equals(typeName, BusinessRuleConstants.BusinessRuleAttributeExpressionTypeName, StringComparison.Ordinal)) {
				return JsonSerializer.Deserialize<BusinessRuleAttributeExpressionMetadataDto>(rawJson, options);
			}
			if (string.Equals(typeName, BusinessRuleConstants.BusinessRuleValueExpressionTypeName, StringComparison.Ordinal)) {
				return JsonSerializer.Deserialize<BusinessRuleValueExpressionMetadataDto>(rawJson, options);
			}
		}
		return null;
	}

	public override void Write(
		Utf8JsonWriter writer, BusinessRuleExpressionMetadataDto value, JsonSerializerOptions options) {
		JsonSerializer.Serialize(writer, value, value.GetType(), options);
	}
}

#endregion
