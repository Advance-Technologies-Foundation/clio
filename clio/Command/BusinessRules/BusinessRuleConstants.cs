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
	internal const string BusinessRuleSysValueExpressionTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleSysValueExpression";
	internal const string BusinessRuleSysSettingExpressionTypeName = "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleSysSettingExpression";
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
	internal const string SysValueExpressionType = "SysValue";
	internal const string SysSettingExpressionType = "SysSetting";
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
	internal const int ComparisonContain = 11;
	internal const int ComparisonNotContain = 12;
	internal const string SupportedComparisonTypesDescription =
		"equal, not-equal, is-filled-in, is-not-filled-in, greater-than, greater-than-or-equal, less-than, less-than-or-equal, contain, not-contain";
	internal const string SupportedActionTypesDescription =
		"make-editable, make-read-only, make-required, make-optional, set-values, apply-filter, apply-static-filter";
	internal const string SupportedPageActionTypesDescription =
		"hide-element, show-element, make-editable, make-read-only, make-required, make-optional";

	/// <summary>
	/// Shared empty system-setting operand map. Threaded through the pure converter/validator when a rule
	/// references no <c>SysSetting</c> operand, so those code paths never need a null check or allocation.
	/// </summary>
	internal static readonly IReadOnlyDictionary<string, SysSettingOperandDescriptor> EmptySysSettingOperandMap =
		new Dictionary<string, SysSettingOperandDescriptor>(0);

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
			["greater-than-or-equal"] = ComparisonGreaterThanOrEqual,
			["contain"] = ComparisonContain,
			["not-contain"] = ComparisonNotContain
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

	/// <summary>
	/// Collection-membership comparisons. Used for an <c>ObjectList</c> left operand
	/// (for example the <c>CurrentUserRoles</c> system variable) against a single lookup value,
	/// and for text "contains" comparisons.
	/// </summary>
	internal static readonly IReadOnlySet<string> ContainComparisonTypeNames =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"contain",
			"not-contain"
		};

	/// <summary>
	/// Ordered catalog of system variables supported as the right-hand expression of a
	/// business-rule condition. The order is the documented order surfaced through the MCP
	/// tool contract and guidance text; both <see cref="SupportedSystemVariables"/> and
	/// <see cref="SupportedSystemVariablesDescription"/> are derived from this single source
	/// so they can never drift apart.
	/// </summary>
	private static readonly SystemVariableDescriptor[] SystemVariableCatalog = [
		new("CurrentDate", "Date", null),
		new("CurrentTime", "Time", null),
		new("CurrentDateTime", "DateTime", null),
		new("CurrentUser", "Lookup", "SysAdminUnit"),
		new("CurrentUserContact", "Lookup", "Contact"),
		new("CurrentUserAccount", "Lookup", "Account"),
		new("CurrentUserRoles", "ObjectList", "SysAdminUnit")
	];

	/// <summary>
	/// System variables supported as the right-hand expression of a business-rule condition.
	/// Keyed case-insensitively by the platform <c>sysValueName</c>. The value carries the
	/// canonical <c>sysValueName</c>, the system variable's data value type and, for lookup
	/// variables, the schema it references. Use <see cref="SystemVariableDescriptor.SysValueName"/>
	/// (not the caller-supplied key) when persisting metadata, because the platform resolves
	/// system variables by exact name at runtime.
	/// </summary>
	internal static readonly IReadOnlyDictionary<string, SystemVariableDescriptor> SupportedSystemVariables =
		BuildSystemVariableLookup(SystemVariableCatalog);

	internal static readonly string SupportedSystemVariablesDescription =
		string.Join(", ", Array.ConvertAll(SystemVariableCatalog, descriptor => descriptor.SysValueName));

	private static IReadOnlyDictionary<string, SystemVariableDescriptor> BuildSystemVariableLookup(
		IEnumerable<SystemVariableDescriptor> catalog) {
		Dictionary<string, SystemVariableDescriptor> lookup =
			new(StringComparer.OrdinalIgnoreCase);
		foreach (SystemVariableDescriptor descriptor in catalog) {
			lookup[descriptor.SysValueName] = descriptor;
		}

		return lookup;
	}

}

/// <summary>
/// Metadata describing a system variable usable in a business-rule condition.
/// </summary>
/// <param name="SysValueName">Canonical platform <c>sysValueName</c> the variable resolves to at runtime.</param>
/// <param name="DataValueTypeName">Data value type the system variable resolves to.</param>
/// <param name="ReferenceSchemaName">Schema referenced by a lookup system variable, or <c>null</c> for non-lookup variables.</param>
internal sealed record SystemVariableDescriptor(string SysValueName, string DataValueTypeName, string? ReferenceSchemaName);
