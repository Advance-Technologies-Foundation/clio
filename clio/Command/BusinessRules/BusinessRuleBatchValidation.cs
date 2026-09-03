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
