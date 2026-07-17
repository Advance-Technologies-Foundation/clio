using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Validates that lookup constants used by business rules reference existing Creatio records.
/// </summary>
internal interface IBusinessRuleLookupReferenceValidator {
	/// <summary>
	/// Validates all lookup constants referenced by the supplied business rule.
	/// </summary>
	/// <param name="rule">Business rule to inspect.</param>
	/// <param name="attributeMap">Business-rule attribute metadata keyed by payload path.</param>
	void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap);
}

internal sealed class BusinessRuleLookupReferenceValidator(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: IBusinessRuleLookupReferenceValidator {

	private const int RequestTimeoutMs = 30_000;

	public void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		ArgumentNullException.ThrowIfNull(rule);
		ArgumentNullException.ThrowIfNull(attributeMap);

		foreach (BusinessRuleLookupReference reference in EnumerateLookupReferences(rule, attributeMap)) {
			ValidateReferenceExists(reference);
		}
	}

	private static IEnumerable<BusinessRuleLookupReference> EnumerateLookupReferences(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		foreach (BusinessRuleLookupReference reference in EnumerateConditionLookupReferences(rule, attributeMap)) {
			yield return reference;
		}

		foreach (BusinessRuleLookupReference reference in EnumerateActionLookupReferences(rule, attributeMap)) {
			yield return reference;
		}
	}

	private static IEnumerable<BusinessRuleLookupReference> EnumerateConditionLookupReferences(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		foreach (BusinessRuleCondition condition in rule.Condition?.Conditions ?? []) {
			if (TryBuildConditionLookupReference(condition, attributeMap, out BusinessRuleLookupReference reference)) {
				yield return reference;
			}
		}
	}

	private static IEnumerable<BusinessRuleLookupReference> EnumerateActionLookupReferences(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		foreach (BusinessRuleAction action in rule.Actions ?? []) {
			if (action is null
				|| !string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			foreach (BusinessRuleSetValueItem item in action.SetValueItems) {
				if (TryBuildActionLookupReference(item, attributeMap, out BusinessRuleLookupReference reference)) {
					yield return reference;
				}
			}
		}
	}

	private static bool TryBuildConditionLookupReference(
		BusinessRuleCondition condition,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		out BusinessRuleLookupReference reference) {
		reference = default!;
		if (condition?.RightExpression is null
			|| !IsConstExpression(condition.RightExpression)
			|| condition.RightExpression.Value is null
			|| string.IsNullOrWhiteSpace(condition.LeftExpression?.Path)) {
			return false;
		}

		string leftPath = condition.LeftExpression.Path;
		return TryBuildLookupReference(
			// Resolve through the scoped operand key so a scoped lookup (e.g. scopeId "PDS", path "Contact",
			// or a lookup page parameter) uses the same descriptor the validator/converter did, instead of a
			// same-named root attribute or a missing key.
			attributeMap[BusinessRuleHelpers.BuildScopedOperandKey(condition.LeftExpression.ScopeId, leftPath)],
			condition.RightExpression.Value.Value,
			"rule.condition.conditions[*].rightExpression.value",
			out reference);
	}

	private static bool TryBuildActionLookupReference(
		BusinessRuleSetValueItem item,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		out BusinessRuleLookupReference reference) {
		reference = default!;
		if (item?.Value is null
			|| !IsConstExpression(item.Value)
			|| item.Value.Value is null
			|| string.IsNullOrWhiteSpace(item.Expression?.Path)) {
			return false;
		}

		string targetPath = item.Expression.Path;
		return TryBuildLookupReference(
			attributeMap[BusinessRuleHelpers.BuildScopedOperandKey(item.Expression.ScopeId, targetPath)],
			item.Value.Value.Value,
			"rule.actions[*].items[*].value.value",
			out reference);
	}

	private static bool TryBuildLookupReference(
		BusinessRuleAttributeDescriptor descriptor,
		JsonElement value,
		string sourcePath,
		out BusinessRuleLookupReference reference) {
		reference = default!;
		if (!string.Equals(descriptor.DataValueTypeName, "Lookup", StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		if (string.IsNullOrWhiteSpace(descriptor.ReferenceSchemaName)) {
			throw new ArgumentException(
				$"{sourcePath} references lookup attribute '{descriptor.Path}', but its reference schema cannot be resolved.");
		}

		if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out Guid recordId)) {
			return false;
		}

		reference = new BusinessRuleLookupReference(
			descriptor.Path,
			descriptor.ReferenceSchemaName,
			recordId,
			sourcePath);
		return true;
	}

	private void ValidateReferenceExists(BusinessRuleLookupReference reference) {
		// ESQ SelectQuery (DataService) is used here instead of OData because it resolves lookup records
		// more reliably across schemas; mirrors the static-filter LookupValueResolver.
		object query = SelectQueryHelper.BuildSelectQuery(
			reference.ReferenceSchemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[new SelectQueryHelper.SelectQueryFilterDefinition(
				"Id",
				reference.RecordId.ToString("D"),
				SelectQueryHelper.GuidDataValueType,
				ComparisonType: 3)],
			// Existence probe: an Id-equality filter yields at most one row, so cap the fetch at 1.
			rowCount: 1);

		LookupExistsResponseDto response;
		try {
			response = SelectQueryHelper.ExecuteSelectQuery<LookupExistsResponseDto>(
				applicationClient, serviceUrlBuilder, query, RequestTimeoutMs);
		} catch (InvalidOperationException exception) {
			// Preserve the validator's ArgumentException contract: consumers (e.g. PageBusinessRuleValidator)
			// catch only ArgumentException, so a transport/server SelectQuery failure must surface as one.
			throw new ArgumentException(
				$"{reference.SourcePath} references lookup attribute '{reference.AttributePath}', but record existence in lookup schema '{reference.ReferenceSchemaName}' could not be verified: {exception.Message}",
				exception);
		}

		if ((response.Rows?.Count ?? 0) == 0) {
			throw new ArgumentException(
				$"{reference.SourcePath} references lookup attribute '{reference.AttributePath}', but record '{reference.RecordId:D}' was not found in lookup schema '{reference.ReferenceSchemaName}'. Use odata-read or execute-esq to find the lookup record Id before creating the business rule.");
		}
	}

	private sealed class LookupExistsResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<JsonElement>? Rows { get; set; }
	}

	private static bool IsConstExpression(BusinessRuleExpression expression) =>
		string.Equals(expression.Type, ConstExpressionType, StringComparison.OrdinalIgnoreCase);

	private sealed record BusinessRuleLookupReference(
		string AttributePath,
		string ReferenceSchemaName,
		Guid RecordId,
		string SourcePath);
}
