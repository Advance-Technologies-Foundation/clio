using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.BusinessRules.Filters.Schema;

namespace Clio.Command.Filters;

public sealed record PageWidgetFilterReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) {
	internal static readonly PageWidgetFilterReport Empty = new([], []);
}

/// <summary>
/// Validates the stored ESQ filters of a page's widgets against the environment's entity schemas
/// (ENG-95270). Before this, a chart/indicator filter leaf was checked by nothing: the registry validator
/// stops at the opaque filter types, and the only signal was an optional <c>execute-esq</c> dry run.
/// </summary>
public interface IWidgetFilterValidator {
	/// <param name="body">Freedom UI page body (web) about to be saved.</param>
	/// <param name="packageUId">Package the page is saved to; entity schemas are read in its context.</param>
	/// <param name="persistedBody">The body the page holds now, when known. A filter that is byte-for-byte what
	/// the page already stores at the same place only warns: a stale filter left by someone else must not block
	/// an unrelated edit.</param>
	PageWidgetFilterReport Validate(string body, Guid packageUId, string? persistedBody = null);
}

internal sealed class WidgetFilterValidator(IEntityBusinessRuleSchemaProvider entitySchemaProvider) : IWidgetFilterValidator {

	private const string UnchangedPrefix = "Already on the page, not changed by this save: ";

	public PageWidgetFilterReport Validate(string body, Guid packageUId, string? persistedBody = null) {
		IReadOnlyList<PageWidgetFilter>? filters = WidgetFilterLocator.Locate(body);
		if (filters is null) {
			return new PageWidgetFilterReport([],
				["Widget filters were not checked: the view-config diff is not readable JSON."]);
		}
		if (filters.Count == 0) {
			return PageWidgetFilterReport.Empty;
		}
		IReadOnlySet<(string Path, string Json)> unchanged = persistedBody is null
			? new HashSet<(string, string)>()
			: (WidgetFilterLocator.Locate(persistedBody) ?? [])
				.Select(filter => (filter.Path, Canonical(filter.Group)))
				.ToHashSet();
		FilterSchemaProvider schemaProvider = new(entitySchemaProvider);
		schemaProvider.Initialize(packageUId);
		EsqFilterSemanticValidator validator = new(schemaProvider);
		List<string> errors = [];
		var warnings = new List<string>();
		var seenWarnings = new HashSet<string>(StringComparer.Ordinal);
		foreach (PageWidgetFilter filter in filters) {
			if (filter.GroupRootSchemaName is { } groupRoot
				&& !string.Equals(groupRoot, filter.RootSchemaName, StringComparison.OrdinalIgnoreCase)) {
				AddWarning($"{filter.Path}: not checked, its rootSchemaName '{groupRoot}' differs from the data source's schemaName '{filter.RootSchemaName}'.");
				continue;
			}
			EsqFilterGroupNode? group = EsqFilterReader.ReadGroup(filter.Group, filter.Path);
			if (group is null) {
				continue;
			}
			EsqFilterFindings findings = validator.Validate(group, filter.RootSchemaName);
			bool isUnchanged = unchanged.Contains((filter.Path, Canonical(filter.Group)));
			foreach (string error in findings.Errors) {
				if (isUnchanged) {
					AddWarning(UnchangedPrefix + error);
				} else {
					errors.Add(error);
				}
			}
			foreach (string warning in findings.Warnings) {
				AddWarning(warning);
			}
		}
		return new PageWidgetFilterReport(errors, warnings);

		// Compact re-serialization: the stored body and an append-merged one can differ in whitespace only.
		static string Canonical(JsonElement group) => JsonSerializer.Serialize(group);

		void AddWarning(string warning) {
			if (seenWarnings.Add(warning)) {
				warnings.Add(warning);
			}
		}
	}
}
