using System;
using System.Collections.Generic;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Shared request-level guards for the entity/page batch business-rule services. Keeps the
/// package/schema/rules validation (and its error messages) identical across both services instead
/// of duplicating it per service.
/// </summary>
internal static class BusinessRuleBatchValidation {
	/// <summary>
	/// Validates that the batch carries a package name, a schema name, and at least one rule.
	/// </summary>
	/// <param name="packageName">The target package name.</param>
	/// <param name="schemaName">The target entity/page schema name.</param>
	/// <param name="schemaFieldName">The caller-facing field label used in the schema-name error message
	/// (for example <c>entity-schema-name</c> or <c>page-schema-name</c>).</param>
	/// <param name="rules">The rules to create in the batch.</param>
	/// <exception cref="ArgumentException">Thrown when any required field is missing or the batch is empty.</exception>
	internal static void RequireBatchFields(
		string packageName,
		string schemaName,
		string schemaFieldName,
		IReadOnlyList<BusinessRule>? rules) {
		string? missingFieldsError = MissingSchemaFieldsError(packageName, schemaName, schemaFieldName);
		if (missingFieldsError is not null) {
			throw new ArgumentException(missingFieldsError);
		}

		if (rules is null || rules.Count == 0) {
			throw new ArgumentException("rules is required and must contain at least one rule.");
		}
	}

	/// <summary>
	/// The request-shape message for a PAGE business-rule tool: every missing field named in ONE message,
	/// environment included, and evaluated BEFORE the environment is resolved.
	/// </summary>
	/// <remarks>
	/// PR #1352 review: the pre-environment check had landed on <c>read</c> only. The other three page tools
	/// pre-checked their collection field alone and then went through the executor, which resolves the
	/// environment before the service reaches <see cref="RequireBatchFields" /> — so a caller who omitted the
	/// identity fields got one error per call (the two-failed-calls pattern issue #1305 exists to remove), and
	/// on an unresolvable environment never learned which identity fields were missing at all. One shared
	/// aggregate, used by all four, removes the asymmetry instead of adding a fourth variant of it.
	/// </remarks>
	/// <param name="environmentName">The registered environment name the request targets.</param>
	/// <param name="packageName">The target package name.</param>
	/// <param name="schemaName">The effective page schema name (canonical field or its alias).</param>
	/// <param name="schemaFieldName">The caller-facing label for the schema field.</param>
	/// <param name="collectionFieldName">The caller-facing label for the tool's collection field
	/// (<c>rules</c> or <c>rule-names</c>), or <c>null</c> for a tool that has none.</param>
	/// <param name="collectionCount">How many entries that collection carries.</param>
	/// <returns>The aggregated error message, or <c>null</c> when the request shape is complete.</returns>
	internal static string? MissingRequestFieldsError(
		string? environmentName,
		string? packageName,
		string? schemaName,
		string schemaFieldName,
		string? collectionFieldName,
		int collectionCount) {
		List<string> missing = [];
		if (string.IsNullOrWhiteSpace(environmentName)) {
			missing.Add("environment-name");
		}

		if (string.IsNullOrWhiteSpace(packageName)) {
			missing.Add("package-name");
		}

		if (string.IsNullOrWhiteSpace(schemaName)) {
			missing.Add(schemaFieldName);
		}

		string collectionItemNoun = collectionFieldName == "rules" ? "rule." : "rule name.";
		string? emptyCollectionError = collectionFieldName is not null && collectionCount <= 0
			? $"{collectionFieldName} is required and must contain at least one {collectionItemNoun}"
			: null;

		return missing.Count switch {
			0 => emptyCollectionError,
			1 when emptyCollectionError is null => $"{missing[0]} is required.",
			_ when emptyCollectionError is null => $"{string.Join(", ", missing)} are required.",
			1 => $"{missing[0]} is required. {emptyCollectionError}",
			_ => $"{string.Join(", ", missing)} are required. {emptyCollectionError}"
		};
	}

	/// <summary>
	/// The message naming EVERY missing target field, or <c>null</c> when package and schema are both
	/// supplied. Reporting the missing fields one at a time costs the caller a failed round trip per field,
	/// which for an agent-facing tool is the difference between one call and three (issue #1305, point 3).
	/// This is the single source of that message: the services throw it, and a tool can also use it to
	/// reject a malformed request BEFORE an environment is resolved, so a bad environment name does not
	/// mask which fields the caller actually forgot.
	/// </summary>
	/// <param name="packageName">The target package name.</param>
	/// <param name="schemaName">The target entity/page schema name.</param>
	/// <param name="schemaFieldName">The caller-facing field label for the schema name
	/// (for example <c>entity-schema-name</c> or <c>page-schema-name</c>).</param>
	/// <returns>The aggregated error message, or <c>null</c> when nothing is missing.</returns>
	internal static string? MissingSchemaFieldsError(string? packageName, string? schemaName, string schemaFieldName) {
		List<string> missing = [];
		if (string.IsNullOrWhiteSpace(packageName)) {
			missing.Add("package-name");
		}

		if (string.IsNullOrWhiteSpace(schemaName)) {
			missing.Add(schemaFieldName);
		}

		return missing.Count switch {
			0 => null,
			1 => $"{missing[0]} is required.",
			_ => $"{string.Join(", ", missing)} are required."
		};
	}
}
