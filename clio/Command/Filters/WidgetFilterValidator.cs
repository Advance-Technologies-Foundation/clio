using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.BusinessRules.Filters.Schema;

namespace Clio.Command.Filters;

/// <summary>Errors block the save; warnings are reported beside it.</summary>
public sealed record PageWidgetFilterReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) {
	internal static readonly PageWidgetFilterReport Empty = new([], []);
}

/// <summary>Validates a page's stored widget ESQ filters against the environment's entity schemas (ENG-95270).</summary>
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
		ReportBuilder report = new();
		foreach (PageWidgetFilter filter in filters) {
			ValidateFilter(filter, validator, unchanged, report);
		}
		return report.Build();
	}

	private static void ValidateFilter(PageWidgetFilter filter, EsqFilterSemanticValidator validator,
		IReadOnlySet<(string Path, string Json)> unchanged, ReportBuilder report) {
		if (filter.GroupRootSchemaName is { } groupRoot
			&& !string.Equals(groupRoot, filter.RootSchemaName, StringComparison.OrdinalIgnoreCase)) {
			report.AddWarning($"{filter.Path}: not checked, its rootSchemaName '{groupRoot}' differs from the data source's schemaName '{filter.RootSchemaName}'.");
			return;
		}
		EsqFilterGroupNode? group = EsqFilterReader.ReadGroup(filter.Group, filter.Path);
		if (group is null) {
			return;
		}
		EsqFilterFindings findings = validator.Validate(group, filter.RootSchemaName);
		bool isUnchanged = unchanged.Contains((filter.Path, Canonical(filter.Group)));
		foreach (string error in findings.Errors) {
			if (isUnchanged) {
				report.AddWarning(UnchangedPrefix + error);
			} else {
				report.AddError(error);
			}
		}
		foreach (string warning in findings.Warnings) {
			report.AddWarning(warning);
		}
	}

	// Compact re-serialization: the stored body and an append-merged one can differ in whitespace only.
	private static string Canonical(JsonElement group) => JsonSerializer.Serialize(group);

	private sealed class ReportBuilder {
		private readonly List<string> _errors = [];
		private readonly List<string> _warnings = [];
		private readonly HashSet<string> _seenWarnings = new(StringComparer.Ordinal);

		internal void AddError(string error) => _errors.Add(error);

		internal void AddWarning(string warning) {
			if (_seenWarnings.Add(warning)) {
				_warnings.Add(warning);
			}
		}

		internal PageWidgetFilterReport Build() => new(_errors, _warnings);
	}
}
