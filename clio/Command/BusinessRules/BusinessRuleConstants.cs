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
	internal const int ComparisonEqual = 2;
	internal const int ComparisonNotEqual = 3;

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

}
