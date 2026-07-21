using System;
using System.Collections.Generic;
using Clio.Common;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Resolves the environment-specific data value type of every system-setting operand referenced by a
/// business rule so the pure converter/validator can type a <c>SysSetting</c> operand without touching
/// the environment. Settings whose value type cannot be used in a condition (Binary, SecureText, or an
/// otherwise non-scalar type) are rejected with an explanatory error.
/// </summary>
internal interface ISysSettingConditionOperandResolver {
	/// <summary>
	/// Resolves each distinct system setting referenced by a <c>SysSetting</c> condition operand in
	/// <paramref name="rule"/> to its operand descriptor, keyed by setting code. Returns an empty map
	/// (and performs no environment round-trip) when the rule references no system-setting operand.
	/// </summary>
	/// <param name="rule">Business rule whose condition operands are inspected.</param>
	IReadOnlyDictionary<string, SysSettingOperandDescriptor> Resolve(BusinessRule rule);
}

internal sealed class SysSettingConditionOperandResolver(ISysSettingsManager sysSettingsManager)
	: ISysSettingConditionOperandResolver {

	private const string SecureTextValueTypeName = "SecureText";
	private const string BinaryValueTypeName = "Binary";
	private const string LookupValueTypeName = "Lookup";

	public IReadOnlyDictionary<string, SysSettingOperandDescriptor> Resolve(BusinessRule rule) {
		ArgumentNullException.ThrowIfNull(rule);
		Dictionary<string, SysSettingOperandDescriptor> result = new(StringComparer.Ordinal);
		foreach (BusinessRuleCondition condition in rule.Condition?.Conditions ?? []) {
			CollectOperand(condition?.LeftExpression, result);
			CollectOperand(condition?.RightExpression, result);
		}

		return result;
	}

	private void CollectOperand(
		BusinessRuleExpression? expression,
		Dictionary<string, SysSettingOperandDescriptor> result) {
		if (expression is null
			|| !string.Equals(expression.Type, SysSettingExpressionType, StringComparison.OrdinalIgnoreCase)) {
			return;
		}

		string code = expression.SysSettingName;
		// A missing code is a structural error reported by the validator with the exact field path; the
		// resolver only types settings that are actually referenced by code.
		if (string.IsNullOrWhiteSpace(code) || result.ContainsKey(code)) {
			return;
		}

		(string ValueTypeName, string? ReferenceSchemaName)? resolved =
			sysSettingsManager.GetSysSettingTypeByCode(code);
		if (resolved is null) {
			throw new ArgumentException(
				$"System setting '{code}' referenced in rule.condition.conditions[*] does not exist on the target environment.");
		}

		string dataValueTypeName = ResolveDataValueTypeName(code, resolved.Value.ValueTypeName);
		string? referenceSchemaName = null;
		if (string.Equals(dataValueTypeName, LookupValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			if (string.IsNullOrWhiteSpace(resolved.Value.ReferenceSchemaName)) {
				throw new ArgumentException(
					$"System setting '{code}' is a Lookup setting whose reference schema could not be resolved on the target environment.");
			}

			referenceSchemaName = resolved.Value.ReferenceSchemaName;
		}

		result[code] = new SysSettingOperandDescriptor(code, dataValueTypeName, referenceSchemaName);
	}

	/// <summary>
	/// Maps a sys-setting value-type name to the canonical business-rule data value type, normalizing the
	/// two sys-setting-only aliases and rejecting types that cannot participate in a condition comparison.
	/// </summary>
	private static string ResolveDataValueTypeName(string sysSettingName, string sysSettingValueTypeName) {
		string normalized = sysSettingValueTypeName switch {
			"Decimal" => "Float",
			"Currency" => "Money",
			_ => sysSettingValueTypeName
		};

		// SecureText is classified as a (filterable) Text kind by CreatioDataValueType, so this explicit
		// guard is the ONLY barrier that keeps a secret-bearing setting out of a client-side rule
		// condition. Do not drop it in favour of the IsFilterable check below.
		if (string.Equals(normalized, SecureTextValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"System setting '{sysSettingName}' is a SecureText setting and cannot be used as a business-rule condition operand.");
		}

		if (string.Equals(normalized, BinaryValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"System setting '{sysSettingName}' is a Binary setting and cannot be used as a business-rule condition operand.");
		}

		// A null/empty type name (malformed setting row) would throw inside IsFilterable, so reject it
		// with the same friendly error rather than an opaque ArgumentNullException.
		if (string.IsNullOrEmpty(normalized) || !CreatioDataValueType.IsFilterable(normalized)) {
			throw new ArgumentException(
				$"System setting '{sysSettingName}' has value type '{sysSettingValueTypeName}', which is not supported as a business-rule condition operand.");
		}

		return normalized;
	}
}
