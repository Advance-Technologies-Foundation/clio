using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleConstants {
	internal const string BusinessRuleAddonName = "BusinessRule";
	internal const string EntitySchemaManagerName = "EntitySchemaManager";
	internal const string EntitySchemaDesignerBasePath = "ServiceModel/EntitySchemaDesignerService.svc";
	internal const string BusinessRulesMetadataTypeName = "Terrasoft.Core.BusinessRules.BusinessRules";
	internal const string BusinessRuleTypeName = "Terrasoft.Core.BusinessRules.BusinessRule";
	internal const string BusinessRuleCaseTypeName = "Terrasoft.Core.BusinessRules.Models.BusinessRuleCase";
	internal const string BusinessRuleTriggerTypeName = "Terrasoft.Core.BusinessRules.Models.Trigger";
	internal const string BusinessRuleGroupConditionTypeName = "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleGroupCondition";
	internal const string BusinessRuleConditionTypeName = "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleCondition";
	internal const string BusinessRuleAttributeExpressionTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleAttributeExpression";
	internal const string BusinessRuleValueExpressionTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleValueExpression";
	internal const string BusinessRuleFormulaExpressionTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleFormulaExpression";
	internal const string BusinessRuleContextExpressionTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleContextExpression";
	internal const string BusinessRuleParameterMappingTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.ParameterMapping";
	internal const string ExpressionSchemaVariableTypeName = "Terrasoft.Core.ExpressionEngine.Schema.Variables.ExpressionSchemaVariable";
	internal const string ExpressionSchemaRecordVariableConfigTypeName = "Terrasoft.Core.ExpressionEngine.Schema.Variables.ExpressionSchemaRecordVariableConfig";
	internal const string ExpressionSchemaParameterTypeName = "Terrasoft.Core.ExpressionEngine.Schema.Parameters.ExpressionSchemaParameter";
	internal const string BusinessRuleEditableElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionEditableElement";
	internal const string BusinessRuleReadonlyElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionReadonlyElement";
	internal const string BusinessRuleRequiredElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionRequiredElement";
	internal const string BusinessRuleOptionalElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionOptionalElement";
	internal const string BusinessRuleSetValuesElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionSetValues";
	internal const string BusinessRuleFilterLookupElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionFilterLookup";
	internal const string BusinessRuleSetFilterElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionSetFilter";
	internal const string BusinessRuleSetValueItemTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleSetValueItem";
	internal const string BusinessRuleFilterLookupExpressionTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleFilterLookupExpression";
	internal const string BusinessRuleEmptyValueExpressionTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleEmptyValueExpression";
	internal const string BusinessRuleHideElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionHideElement";
	internal const string BusinessRuleShowElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionShowElement";
	internal const string ClientUnitSchemaManagerName = "ClientUnitSchemaManager";
	internal const string SetValuesActionTypeName = "set-values";
	internal const string ApplyFilterActionTypeName = "apply-filter";
	internal const string ApplyStaticFilterActionTypeName = "apply-static-filter";
	internal const string AttributeValueExpressionType = "AttributeValue";
	internal const string ConstExpressionType = "Const";
	internal const string FormulaExpressionType = "Formula";
	internal const int ChangeAttributeValueTriggerType = 0;
	internal const int DataLoadedTriggerType = 2;
	internal const int LogicalAnd = 1;
	internal const int LogicalOr = 2;
	internal const int ComparisonIsNotFilledIn = 0;
	internal const int ComparisonIsFilledIn = 1;
	internal const int ComparisonEqual = 2;
	internal const int ComparisonNotEqual = 3;
	internal const int ComparisonLessThan = 5;
	internal const int ComparisonLessThanOrEqual = 6;
	internal const int ComparisonGreaterThan = 7;
	internal const int ComparisonGreaterThanOrEqual = 8;
	internal const string SupportedComparisonTypesDescription =
		"equal, not-equal, is-filled-in, is-not-filled-in, greater-than, greater-than-or-equal, less-than, less-than-or-equal";
	internal const string SupportedActionTypesDescription =
		"make-editable, make-read-only, make-required, make-optional, set-values, apply-filter, apply-static-filter";
	internal const string SupportedPageActionTypesDescription =
		"hide-element, show-element, make-editable, make-read-only, make-required, make-optional";

	internal static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	internal static readonly IReadOnlyDictionary<string, string> SupportedActionTypeNames =
		new Dictionary<string, string> {
			["make-editable"] = BusinessRuleEditableElementTypeName,
			["make-read-only"] = BusinessRuleReadonlyElementTypeName,
			["make-required"] = BusinessRuleRequiredElementTypeName,
			["make-optional"] = BusinessRuleOptionalElementTypeName,
			[ApplyFilterActionTypeName] = BusinessRuleFilterLookupElementTypeName,
			[ApplyStaticFilterActionTypeName] = BusinessRuleSetFilterElementTypeName
		};

	internal static readonly IReadOnlyDictionary<string, string> SupportedPageActionTypeNames =
		new Dictionary<string, string> {
			["hide-element"] = BusinessRuleHideElementTypeName,
			["show-element"] = BusinessRuleShowElementTypeName,
			["make-editable"] = BusinessRuleEditableElementTypeName,
			["make-read-only"] = BusinessRuleReadonlyElementTypeName,
			["make-required"] = BusinessRuleRequiredElementTypeName,
			["make-optional"] = BusinessRuleOptionalElementTypeName
		};

	internal static readonly IReadOnlyDictionary<string, int> SupportedComparisonTypeValues =
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
			["is-not-filled-in"] = ComparisonIsNotFilledIn,
			["is-filled-in"] = ComparisonIsFilledIn,
			["equal"] = ComparisonEqual,
			["not-equal"] = ComparisonNotEqual,
			["less-than"] = ComparisonLessThan,
			["less-than-or-equal"] = ComparisonLessThanOrEqual,
			["greater-than"] = ComparisonGreaterThan,
			["greater-than-or-equal"] = ComparisonGreaterThanOrEqual
		};

	internal static readonly IReadOnlySet<string> UnaryComparisonTypeNames =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"is-filled-in",
			"is-not-filled-in"
		};

	internal static readonly IReadOnlySet<string> RelationalComparisonTypeNames =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"greater-than",
			"greater-than-or-equal",
			"less-than",
			"less-than-or-equal"
		};

	internal static readonly IReadOnlySet<string> EqualityComparisonTypeNames =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"equal",
			"not-equal"
		};

}
