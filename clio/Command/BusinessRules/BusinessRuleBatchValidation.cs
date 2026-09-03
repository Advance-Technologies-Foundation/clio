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
		// Every missing field is reported in ONE message: reporting them one at a time costs the caller a
		// failed round trip per field (issue #1305, point 3).
		List<string> missing = [];
		if (string.IsNullOrWhiteSpace(packageName)) {
			missing.Add("package-name");
		}

		if (string.IsNullOrWhiteSpace(schemaName)) {
			missing.Add(schemaFieldName);
		}

		if (missing.Count == 1) {
			throw new ArgumentException($"{missing[0]} is required.");
		}

		if (missing.Count > 1) {
			throw new ArgumentException($"{string.Join(", ", missing)} are required.");
		}

		if (rules is null || rules.Count == 0) {
			throw new ArgumentException("rules is required and must contain at least one rule.");
		}
	}
}
