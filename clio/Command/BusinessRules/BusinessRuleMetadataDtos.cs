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

	[JsonPropertyName("parentUId")]
	public string? ParentUId { get; set; }

	[JsonPropertyName("parentActionUId")]
	public string? ParentActionUId { get; set; }
}

internal sealed class BusinessRuleCaseMetadataDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = BusinessRuleConstants.BusinessRuleCaseTypeName;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = string.Empty;

	[JsonPropertyName("condition")]
	public BaseBusinessRuleConditionMetadataDto? Condition { get; set; }

	[JsonPropertyName("actions")]
	public List<BaseBusinessRuleActionMetadataDto> Actions { get; set; } = [];
}

[JsonPolymorphic]
[JsonDerivedType(typeof(BusinessRuleGroupConditionMetadataDto))]
[JsonDerivedType(typeof(BusinessRuleConditionMetadataDto))]
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

[JsonPolymorphic]
[JsonDerivedType(typeof(FieldSelectionBusinessRuleActionMetadataDto))]
[JsonDerivedType(typeof(BusinessRuleFilterLookupActionMetadataDto))]
[JsonDerivedType(typeof(BusinessRuleSetFilterActionMetadataDto))]
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
	public object? Items { get; set; }
}

internal sealed class BusinessRuleFilterLookupActionMetadataDto : BaseBusinessRuleActionMetadataDto {
	[JsonPropertyName("leftExpression")]
	public BusinessRuleFilterLookupExpressionMetadataDto LeftExpression { get; set; } = default!;

	[JsonPropertyName("rightExpression")]
	public BusinessRuleFilterLookupExpressionMetadataDto RightExpression { get; set; } = default!;

	[JsonPropertyName("clearValue")]
	public bool ClearValue { get; set; }

	[JsonPropertyName("populateValue")]
	public bool PopulateValue { get; set; }
}

internal sealed class BusinessRuleSetFilterActionMetadataDto : BaseBusinessRuleActionMetadataDto {
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

	// Optional scope for a change-trigger (Terrasoft.Core.BusinessRules.Models.Trigger.ScopeId, short code BRT3).
	// Shipped datasource-scoped rules express their dependency via a DataLoaded trigger named after the datasource
	// rather than this field, so it is emitted only when explicitly set and defaults to empty for the root scope.
	[JsonPropertyName("scopeId")]
	public string ScopeId { get; set; } = string.Empty;

	[JsonPropertyName("type")]
	public int Type { get; set; }
}

internal class BusinessRuleExpressionMetadataDto {
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

	// scopeId is the platform scope discriminator for an attribute expression
	// (Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleAttributeExpression.ScopeId, short code BRX2).
	// It is ordered right after `path` and before `value` so the base expression fields still precede the typed
	// value payload the AddonSchemaDesignerService parses against the already-read dataValueTypeName.
	[JsonPropertyOrder(6)]
	[JsonPropertyName("scopeId")]
	public string? ScopeId { get; set; }

	[JsonPropertyOrder(7)]
	[JsonPropertyName("value")]
	public object? Value { get; set; }

	[JsonPropertyOrder(8)]
	[JsonPropertyName("sysValueName")]
	public string? SysValueName { get; set; }

	// sysSettingName carries a Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleSysSettingExpression
	// operand (short code BSS1). Mirrors sysValueName.
	[JsonPropertyOrder(9)]
	[JsonPropertyName("sysSettingName")]
	public string? SysSettingName { get; set; }

	[JsonPropertyOrder(10)]
	[JsonPropertyName("parameterMappings")]
	public List<BusinessRuleFormulaParameterMappingDto>? ParameterMappings { get; set; }

	[JsonPropertyOrder(11)]
	[JsonPropertyName("expressionSchema")]
	public BusinessRuleExpressionSchemaDto? ExpressionSchema { get; set; }
}

internal sealed class BusinessRuleFilterLookupExpressionMetadataDto : BusinessRuleExpressionMetadataDto {
	[JsonPropertyOrder(12)]
	[JsonPropertyName("filterExpression")]
	public string? FilterExpression { get; set; }
}

internal sealed class BusinessRuleFormulaParameterMappingDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = BusinessRuleConstants.BusinessRuleParameterMappingTypeName;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = Guid.NewGuid().ToString();

	[JsonPropertyName("parameterName")]
	public string ParameterName { get; set; } = string.Empty;

	[JsonPropertyName("expression")]
	public BusinessRuleExpressionMetadataDto? Expression { get; set; }
}

internal sealed class BusinessRuleExpressionSchemaDto {
	[JsonPropertyName("uId")]
	public string UId { get; set; } = Guid.NewGuid().ToString();

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("engineType")]
	public string EngineType { get; set; } = "PowerFx";

	[JsonPropertyName("expression")]
	public string Expression { get; set; } = string.Empty;

	[JsonPropertyName("resultDataValueType")]
	public string ResultDataValueType { get; set; } = string.Empty;

	[JsonPropertyName("expressionVariables")]
	public List<BusinessRuleExpressionSchemaVariableDto> ExpressionVariables { get; set; } = [];

	[JsonPropertyName("parameters")]
	public List<BusinessRuleExpressionSchemaParameterDto> Parameters { get; set; } = [];
}

internal sealed class BusinessRuleExpressionSchemaVariableDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = BusinessRuleConstants.ExpressionSchemaVariableTypeName;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = Guid.NewGuid().ToString();

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("variableType")]
	public string VariableType { get; set; } = "Record";

	[JsonPropertyName("dataValueType")]
	public string DataValueType { get; set; } = "Lookup";

	[JsonPropertyName("config")]
	public BusinessRuleExpressionSchemaRecordVariableConfigDto? Config { get; set; }
}

internal sealed class BusinessRuleExpressionSchemaRecordVariableConfigDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = BusinessRuleConstants.ExpressionSchemaRecordVariableConfigTypeName;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = Guid.NewGuid().ToString();

	[JsonPropertyName("name")]
	public string Name { get; set; } = "VariableConfig";

	[JsonPropertyName("value")]
	public string Value { get; set; } = string.Empty;

	[JsonPropertyName("recordType")]
	public string RecordType { get; set; } = "Entity";

	[JsonPropertyName("primaryValue")]
	public BusinessRuleExpressionSchemaSourceValueConfigDto? PrimaryValue { get; set; }

	[JsonPropertyName("fieldValues")]
	public BusinessRuleExpressionSchemaSourceValueConfigDto? FieldValues { get; set; }

	[JsonPropertyName("columns")]
	public List<string> Columns { get; set; } = [];
}

internal sealed class BusinessRuleExpressionSchemaSourceValueConfigDto {
	[JsonPropertyName("type")]
	public string Type { get; set; } = "Parameter";

	[JsonPropertyName("value")]
	public string Value { get; set; } = string.Empty;
}

internal sealed class BusinessRuleExpressionSchemaParameterDto {
	[JsonPropertyName("typeName")]
	public string TypeName { get; set; } = BusinessRuleConstants.ExpressionSchemaParameterTypeName;

	[JsonPropertyName("uId")]
	public string UId { get; set; } = Guid.NewGuid().ToString();

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("dataValueType")]
	public string DataValueType { get; set; } = string.Empty;
}
