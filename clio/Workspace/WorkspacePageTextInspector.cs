using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Common;

namespace Clio.Workspaces;

/// <summary>
/// Finds Freedom UI page schemas in local workspace packages whose user-visible text is authored as
/// inline literals instead of localizable-string bindings.
/// </summary>
/// <remarks>
/// <c>update-page</c> (MCP) rejects such pages, while <c>push-workspace</c> installs them; this inspector lets
/// <c>push-workspace</c> report the same rule as a warning without changing what it installs (issue #1639).
/// It applies <see cref="SchemaValidationService.FindLocalizableTextViolations"/>, the scan behind the
/// <c>update-page</c> gate, so both paths agree on which nodes break the rule.
/// </remarks>
public interface IWorkspacePageTextInspector {

	/// <summary>
	/// Scans the <c>Schemas/&lt;SchemaName&gt;/*.js</c> files of the given workspace packages and returns one
	/// finding per Freedom UI page schema that carries at least one offending text value. Schemas without a
	/// <c>SCHEMA_VIEW_CONFIG_DIFF</c> section (classic client modules, entity and source-code schemas) are skipped.
	/// </summary>
	/// <param name="packageNames">Workspace package names, resolved against the workspace packages folder.</param>
	/// <returns>The findings ordered by package, then schema name; empty when nothing breaks the rule.</returns>
	IReadOnlyList<PageTextFinding> Inspect(IEnumerable<string> packageNames);
}

/// <summary>
/// A page schema whose user-visible text breaks the localizable-text rule.
/// </summary>
/// <param name="PackageName">The workspace package that owns the schema.</param>
/// <param name="SchemaName">The page schema name (its folder under <c>Schemas</c>).</param>
/// <param name="Elements">Offending values as <c>&lt;node&gt;.&lt;property&gt;</c>, e.g. <c>UsrLabel.caption</c>.</param>
public sealed record PageTextFinding(string PackageName, string SchemaName, IReadOnlyList<string> Elements);

/// <inheritdoc />
public sealed class WorkspacePageTextInspector(IWorkspacePathBuilder workspacePathBuilder, IFileSystem fileSystem)
	: IWorkspacePageTextInspector {

	private const string SchemasFolderName = "Schemas";
	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";

	/// <inheritdoc />
	public IReadOnlyList<PageTextFinding> Inspect(IEnumerable<string> packageNames) {
		List<PageTextFinding> findings = [];
		string packagesFolderPath = workspacePathBuilder.PackagesFolderPath;
		foreach (string packageName in packageNames ?? []) {
			string schemasPath = Path.Combine(packagesFolderPath, packageName, SchemasFolderName);
			if (!fileSystem.ExistsDirectory(schemasPath)) {
				continue;
			}
			foreach (string schemaPath in fileSystem.GetDirectories(schemasPath).OrderBy(p => p, StringComparer.Ordinal)) {
				IReadOnlyList<string> elements = InspectSchema(schemaPath);
				if (elements.Count > 0) {
					findings.Add(new PageTextFinding(packageName, Path.GetFileName(schemaPath), elements));
				}
			}
		}
		return findings;
	}

	private IReadOnlyList<string> InspectSchema(string schemaPath) {
		List<string> elements = [];
		foreach (string filePath in fileSystem.GetFiles(schemaPath, "*.js", SearchOption.TopDirectoryOnly)) {
			string body = fileSystem.ReadAllText(filePath);
			// Cheap pre-filter: only Freedom UI page bodies carry this marker, so every other schema is
			// skipped without parsing.
			if (!body.Contains(ViewConfigDiffMarker, StringComparison.Ordinal)) {
				continue;
			}
			elements.AddRange(SchemaValidationService.FindLocalizableTextViolations(body)
				.Select(FormatElement));
		}
		return elements.Distinct(StringComparer.Ordinal).ToList();
	}

	private static string FormatElement(LocalizableTextViolation violation) =>
		string.IsNullOrWhiteSpace(violation.NodeName)
			? violation.PropertyName
			: $"{violation.NodeName}.{violation.PropertyName}";
}
