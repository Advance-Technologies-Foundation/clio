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
	internal const string BusinessRuleEditableElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionEditableElement";
	internal const string BusinessRuleReadonlyElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionReadonlyElement";
	internal const string BusinessRuleRequiredElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionRequiredElement";
	internal const string BusinessRuleOptionalElementTypeName = "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionOptionalElement";
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
		"make-editable, make-read-only, make-required, make-optional";

	internal static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	internal static readonly IReadOnlyDictionary<int, string> DataValueTypeNames = new Dictionary<int, string> {
		[0] = "Guid",
		[1] = "Text",
		[4] = "Integer",
		[5] = "Float",
		[6] = "Money",
		[7] = "DateTime",
		[8] = "Date",
		[9] = "Time",
		[10] = "Lookup",
		[11] = "Enum",
		[12] = "Boolean",
		[13] = "Blob",
		[14] = "Image",
		[24] = "SecureText",
		[25] = "File",
		[27] = "ShortText",
		[28] = "MediumText",
		[29] = "MaxSizeText",
		[30] = "LongText",
		[31] = "Float1",
		[32] = "Float2",
		[33] = "Float3",
		[34] = "Float4",
		[40] = "Float8",
		[42] = "PhoneText",
		[43] = "RichText",
		[44] = "WebText",
		[45] = "EmailText",
		[47] = "Float0"
	};

	internal static readonly IReadOnlyDictionary<string, string> SupportedActionTypeNames =
		new Dictionary<string, string> {
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

	internal static readonly IReadOnlySet<string> UnsupportedEqualityDataValueTypeNames =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"RichText",
			"Image"
		};

	internal static readonly IReadOnlySet<string> SupportedTextDataValueTypeNames =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"Text",
			"SecureText",
			"ShortText",
			"MediumText",
			"MaxSizeText",
			"LongText",
			"PhoneText",
			"RichText",
			"WebText",
			"EmailText"
		};

	internal static readonly IReadOnlySet<string> SupportedNumericDataValueTypeNames =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"Integer",
			"Float",
			"Money",
			"Float0",
			"Float1",
			"Float2",
			"Float3",
			"Float4",
			"Float8"
		};

	internal static readonly IReadOnlySet<string> SupportedDateTimeDataValueTypeNames =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"Date",
			"DateTime",
			"Time"
		};

}
