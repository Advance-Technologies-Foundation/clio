using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Common;
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
		foreach (BusinessRuleCondition condition in rule.Condition?.Conditions ?? []) {
			if (condition?.RightExpression is null
				|| !IsConstExpression(condition.RightExpression)
				|| condition.RightExpression.Value is null
				|| string.IsNullOrWhiteSpace(condition.LeftExpression?.Path)) {
				continue;
			}

			string leftPath = condition.LeftExpression.Path;
			BusinessRuleAttributeDescriptor leftDescriptor = attributeMap[leftPath];
			if (TryBuildLookupReference(
				    leftDescriptor,
				    condition.RightExpression.Value.Value,
				    "rule.condition.conditions[*].rightExpression.value",
				    out BusinessRuleLookupReference reference)) {
				yield return reference;
			}
		}

		foreach (BusinessRuleAction action in rule.Actions ?? []) {
			if (action is null
				|| !string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			foreach (BusinessRuleSetValueItem item in action.SetValueItems) {
				if (item?.Value is null
					|| !IsConstExpression(item.Value)
					|| item.Value.Value is null
					|| string.IsNullOrWhiteSpace(item.Expression?.Path)) {
					continue;
				}

				string targetPath = item.Expression.Path;
				BusinessRuleAttributeDescriptor targetDescriptor = attributeMap[targetPath];
				if (TryBuildLookupReference(
					    targetDescriptor,
					    item.Value.Value.Value,
					    "rule.actions[*].items[*].value.value",
					    out BusinessRuleLookupReference reference)) {
					yield return reference;
				}
			}
		}
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
		string query = BuildLookupExistsPath(reference.ReferenceSchemaName, reference.RecordId);
		string url = serviceUrlBuilder.Build(query);
		string responseJson = applicationClient.ExecuteGetRequest(url, RequestTimeoutMs);
		if (!ResponseContainsRecord(responseJson)) {
			throw new ArgumentException(
				$"{reference.SourcePath} references lookup attribute '{reference.AttributePath}', but record '{reference.RecordId:D}' was not found in lookup schema '{reference.ReferenceSchemaName}'. Use odata-read to find the lookup record Id before creating the business rule.");
		}
	}

	internal static string BuildLookupExistsPath(string referenceSchemaName, Guid recordId) {
		string filter = Uri.EscapeDataString($"Id eq {recordId:D}");
		string select = Uri.EscapeDataString("Id");
		return $"odata/{referenceSchemaName.Trim()}?$filter={filter}&$select={select}&$top=1";
	}

	private static bool ResponseContainsRecord(string responseJson) {
		using JsonDocument document = JsonDocument.Parse(responseJson);
		if (document.RootElement.TryGetProperty("value", out JsonElement value)
			&& value.ValueKind == JsonValueKind.Array) {
			return value.GetArrayLength() > 0;
		}

		return document.RootElement.ValueKind == JsonValueKind.Object
			&& document.RootElement.TryGetProperty("Id", out _);
	}

	private static bool IsConstExpression(BusinessRuleExpression expression) =>
		string.Equals(expression.Type, ConstExpressionType, StringComparison.OrdinalIgnoreCase);

	private sealed record BusinessRuleLookupReference(
		string AttributePath,
		string ReferenceSchemaName,
		Guid RecordId,
		string SourcePath);
}
