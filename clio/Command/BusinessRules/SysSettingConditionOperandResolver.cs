using System;
using System.Collections.Generic;
using Clio.Common;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

internal interface ISysSettingConditionOperandResolver {
	IReadOnlyDictionary<string, SysSettingOperandDescriptor> Resolve(BusinessRule rule);
}

internal sealed class SysSettingConditionOperandResolver(
	Func<EnvironmentSettings, ISysSettingsManager> sysSettingsManagerFactory,
	EnvironmentSettings environmentSettings) : ISysSettingConditionOperandResolver {

	private const string SecureTextValueTypeName = "SecureText";
	private const string BinaryValueTypeName = "Binary";
	private const string LookupValueTypeName = "Lookup";

	public IReadOnlyDictionary<string, SysSettingOperandDescriptor> Resolve(BusinessRule rule) {
		ArgumentNullException.ThrowIfNull(rule);
		Dictionary<string, SysSettingOperandDescriptor> result = new(StringComparer.Ordinal);
		ISysSettingsManager? sysSettingsManager = null;
		foreach (BusinessRuleCondition condition in rule.Condition?.Conditions ?? []) {
			CollectOperand(condition?.LeftExpression, result, ref sysSettingsManager);
			CollectOperand(condition?.RightExpression, result, ref sysSettingsManager);
		}

		return result;
	}

	private void CollectOperand(
		BusinessRuleExpression? expression,
		Dictionary<string, SysSettingOperandDescriptor> result,
		ref ISysSettingsManager? sysSettingsManager) {
		if (expression is null
			|| !string.Equals(expression.Type, SysSettingExpressionType, StringComparison.OrdinalIgnoreCase)) {
			return;
		}

		string code = expression.SysSettingName;
		if (string.IsNullOrWhiteSpace(code) || result.ContainsKey(code)) {
			return;
		}

		sysSettingsManager ??= sysSettingsManagerFactory(environmentSettings);
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

	private static string ResolveDataValueTypeName(string sysSettingName, string sysSettingValueTypeName) {
		string normalized = sysSettingValueTypeName switch {
			"Decimal" => "Float",
			"Currency" => "Money",
			_ => sysSettingValueTypeName
		};

		// SecureText is a filterable Text kind, so this explicit guard is the only barrier that keeps a
		// secret-bearing setting out of a client-side rule condition.
		if (string.Equals(normalized, SecureTextValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"System setting '{sysSettingName}' is a SecureText setting and cannot be used as a business-rule condition operand.");
		}

		if (string.Equals(normalized, BinaryValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"System setting '{sysSettingName}' is a Binary setting and cannot be used as a business-rule condition operand.");
		}

		if (string.IsNullOrEmpty(normalized) || !CreatioDataValueType.IsFilterable(normalized)) {
			throw new ArgumentException(
				$"System setting '{sysSettingName}' has value type '{sysSettingValueTypeName}', which is not supported as a business-rule condition operand.");
		}

		return normalized;
	}
}
