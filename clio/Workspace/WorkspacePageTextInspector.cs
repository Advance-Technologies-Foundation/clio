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
	/// finding per Freedom UI page schema (web or mobile) that carries at least one offending text value. Web
	/// bodies without a <c>SCHEMA_VIEW_CONFIG_DIFF</c> section (classic client modules, entity and source-code
	/// schemas) and plain-JSON bodies without a <c>viewConfigDiff</c> array are skipped.
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
/// <param name="Elements">
/// Inline literals that must be localizable bindings, as <c>&lt;node&gt;.&lt;property&gt;</c>, e.g. <c>UsrLabel.caption</c>.
/// </param>
/// <param name="LiteralOnlyElements">
/// Localizable bindings on literal-only properties that render empty at runtime, e.g. <c>UsrPhoto.tooltip</c>.
/// </param>
public sealed record PageTextFinding(string PackageName, string SchemaName, IReadOnlyList<string> Elements,
	IReadOnlyList<string> LiteralOnlyElements);

/// <inheritdoc />
public sealed class WorkspacePageTextInspector(IWorkspacePathBuilder workspacePathBuilder, IFileSystem fileSystem)
	: IWorkspacePageTextInspector {

	private const string SchemasFolderName = "Schemas";
	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";
	private const string MobileViewConfigDiffKey = "\"viewConfigDiff\"";

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
				List<LocalizableTextViolation> violations = InspectSchema(schemaPath);
				if (violations.Count > 0) {
					findings.Add(new PageTextFinding(packageName, Path.GetFileName(schemaPath),
						FormatElements(violations, LocalizableTextViolationKind.InlineLiteral),
						FormatElements(violations, LocalizableTextViolationKind.ResourceBindingOnLiteralOnlyProperty)));
				}
			}
		}
		return findings;
	}

	private List<LocalizableTextViolation> InspectSchema(string schemaPath) {
		List<LocalizableTextViolation> violations = [];
		foreach (string filePath in fileSystem.GetFiles(schemaPath, "*.js", SearchOption.TopDirectoryOnly)) {
			violations.AddRange(FindViolations(fileSystem.ReadAllText(filePath)));
		}
		return violations;
	}

	// Mobile Freedom UI pages are plain JSON with a viewConfigDiff key; web pages are AMD modules whose
	// viewConfigDiff sits between SCHEMA_VIEW_CONFIG_DIFF markers. Each goes through the scan update-page
	// applies to that page type.
	private static IReadOnlyList<LocalizableTextViolation> FindViolations(string body) =>
		PageSchemaTypeExtensions.FromBody(body) switch {
			PageSchemaType.Mobile when body.Contains(MobileViewConfigDiffKey, StringComparison.Ordinal)
				=> SchemaValidationService.FindMobileLocalizableTextViolations(body),
			// Cheap pre-filter: only Freedom UI web page bodies carry this marker, so every other web schema
			// is skipped without parsing.
			PageSchemaType.Web when body.Contains(ViewConfigDiffMarker, StringComparison.Ordinal)
				=> SchemaValidationService.FindLocalizableTextViolations(body),
			_ => []
		};

	private static List<string> FormatElements(IEnumerable<LocalizableTextViolation> violations,
		LocalizableTextViolationKind kind) =>
		violations.Where(violation => violation.Kind == kind)
			.Select(FormatElement)
			.Distinct(StringComparer.Ordinal)
			.ToList();

	private static string FormatElement(LocalizableTextViolation violation) =>
		string.IsNullOrWhiteSpace(violation.NodeName)
			? violation.PropertyName
			: $"{violation.NodeName}.{violation.PropertyName}";
}
