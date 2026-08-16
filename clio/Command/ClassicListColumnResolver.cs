namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Newtonsoft.Json.Linq;

/// <summary>Resolves the effective default Classic list columns through read-only Creatio APIs.</summary>
public interface IClassicListColumnResolver {

	/// <summary>Resolves the requested Classic section schema.</summary>
	/// <param name="sectionSchemaName">Classic section client-unit schema name.</param>
	/// <returns>A successful result including its resolution source.</returns>
	GetClassicListColumnsResponse Resolve(string sectionSchemaName);
}

/// <summary>Reads the Classic section hierarchy and resolves its default list-column source.</summary>
internal sealed class ClassicListColumnResolver(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPageDesignerHierarchyClient hierarchyClient,
	IRemoteEntitySchemaColumnManager columnManager,
	IClassicListColumnParser parser) : IClassicListColumnResolver {

	internal const string SchemaDefaultSource = "schema-default";
	internal const string EntityDefaultSource = "entity-default";
	internal const string NoneSource = "none";

	/// <inheritdoc />
	public GetClassicListColumnsResponse Resolve(string sectionSchemaName) {
		if (string.IsNullOrWhiteSpace(sectionSchemaName)) {
			// No `nameof` argument: ArgumentException appends "(Parameter 'sectionSchemaName')" to Message, and
			// this message travels into the machine-consumed `error` field of the response.
			throw new ArgumentException("schema-name is required");
		}
		string normalizedName = sectionSchemaName.Trim();
		if (!PageSchemaMetadataHelper.IsValidSchemaName(normalizedName)) {
			throw new ArgumentException(PageSchemaMetadataHelper.SchemaNameFormatError);
		}

		var notes = new List<string>();
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy = ResolveHierarchy(normalizedName, notes);
		string[] bodies = hierarchy
			.Reverse()
			.Select(schema => schema.Body)
			.Where(body => !string.IsNullOrWhiteSpace(body))
			.ToArray();
		ClassicListColumnParseResult parsed = parser.ParseColumns(bodies);
		string entity = parser.ParseEntityName(bodies);
		if (string.IsNullOrWhiteSpace(entity)) {
			// Naming the skipped layers here matters: ParseEntityName drops exactly the same unanchorable bodies,
			// so a section that plainly declares entitySchemaName can reach this line purely because every layer
			// was skipped — and a bare "does not declare entitySchemaName" then points the caller at a
			// schema-binding problem that does not exist.
			string skipped = parsed.UnparsedLayerCount > 0
				? $" {parsed.UnparsedLayerCount} of {bodies.Length} schema layers were skipped, so the declaration may be in a layer that could not be read."
				: string.Empty;
			throw new InvalidOperationException(
				$"Classic section '{normalizedName}' does not declare entitySchemaName.{skipped}");
		}

		EntitySchemaPropertiesInfo properties = columnManager.GetSchemaProperties(
			new GetEntitySchemaPropertiesOptions { SchemaName = entity });
		if (parsed.UnparsedLayerCount > 0) {
			// Without this the drop is invisible: a most-derived layer that is skipped would silently hand the
			// answer to an ancestor layer — or to the entity fallback — and the caller would read that as the
			// section's real column set. The two reasons are worded apart: claiming a body "could not be parsed
			// as JavaScript" when it parsed fine sends the reader looking for a syntax error that is not there.
			int invalid = parsed.UnparsedLayerCount - parsed.UnanchoredLayerCount;
			string reason = (invalid, parsed.UnanchoredLayerCount) switch {
				(0, _) => "did not expose a Classic schema object",
				(_, 0) => "could not be parsed as JavaScript",
				_ => $"were skipped ({invalid} could not be parsed as JavaScript, "
					+ $"{parsed.UnanchoredLayerCount} exposed no Classic schema object)"
			};
			notes.Add($"{parsed.UnparsedLayerCount} of {bodies.Length} section schema layers {reason} " +
				"and were skipped; the resolved columns may be incomplete.");
		}
		IReadOnlyList<string> schemaColumns = parsed.Columns;
		if (schemaColumns.Count > 0) {
			return Success(normalizedName, entity, SchemaDefaultSource,
				BuildColumnInfo(schemaColumns, properties.Columns), notes);
		}
		if (!string.IsNullOrWhiteSpace(properties.PrimaryDisplayColumnName)) {
			notes.Add("The section schema does not define static list columns; using the entity primary display column.");
			return Success(normalizedName, entity, EntityDefaultSource,
				BuildColumnInfo([properties.PrimaryDisplayColumnName], properties.Columns), notes);
		}
		notes.Add(
			"The section schema does not define static list columns and the entity has no primary display column.");
		return Success(normalizedName, entity, NoneSource, [], notes);
	}

	// The fifth near-verbatim copy of the name -> UId -> design-package -> re-anchor walk, alongside
	// PageSchemaResolver, GetClassicPageSourcesCommand and GetPageHierarchyCommand. Unifying them is tracked as
	// ENG-93249 — named here so this copy is visible to whoever picks that up, like the other three.
	private IReadOnlyList<PageDesignerHierarchySchema> ResolveHierarchy(string schemaName, List<string> notes) {
		(JToken metadata, string metadataError) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			applicationClient, serviceUrlBuilder, schemaName,
			("UId", "UId"), ("PackageUId", "SysPackage.UId"));
		if (metadata is null) {
			throw new InvalidOperationException(metadataError ?? $"Classic section schema '{schemaName}' was not found.");
		}
		string schemaUId = metadata["UId"]?.ToString();
		string packageUId = metadata["PackageUId"]?.ToString();
		if (string.IsNullOrWhiteSpace(schemaUId) || string.IsNullOrWhiteSpace(packageUId)) {
			throw new InvalidOperationException($"Classic section schema '{schemaName}' metadata is incomplete.");
		}
		string designPackageUId;
		try {
			designPackageUId = hierarchyClient.GetDesignPackageUId(schemaUId);
		}
		catch (Exception exception) {
			// Best-effort, mirroring GetClassicPageSourcesCommand.ResolveHierarchyBaseToTop: the designer call
			// parses its response as JSON, so an expired session (HTML error page) surfaces as a parser/transport
			// exception rather than InvalidOperationException. The schema's own package is a valid anchor. The
			// sibling logs the degradation at debug; this resolver has no logger, so it surfaces through notes —
			// both the MCP tool and the CLI Execute path run these notes through SensitiveErrorTextRedactor,
			// so an inner message carrying a host or URI stays safe on both paths.
			notes.Add($"GetDesignPackageUId failed for '{schemaName}' ({exception.Message}); " +
				"anchoring on the schema's own package.");
			designPackageUId = packageUId;
		}
		// GetParentSchemas returns the hierarchy MOST-DERIVED FIRST; Resolve reverses it to feed the parser
		// base-to-top. Reordering either side without the other silently inverts inheritance precedence.
		IReadOnlyList<PageDesignerHierarchySchema> initial =
			hierarchyClient.GetParentSchemas(schemaUId, designPackageUId);
		if (initial.Count == 0) {
			throw new InvalidOperationException($"Classic section schema '{schemaName}' hierarchy is empty.");
		}
		string rootSchemaUId = initial
			.LastOrDefault(schema => string.Equals(schema.Name, schemaName, StringComparison.OrdinalIgnoreCase))?.UId;
		if (string.IsNullOrWhiteSpace(rootSchemaUId) ||
			string.Equals(rootSchemaUId, schemaUId, StringComparison.OrdinalIgnoreCase)) {
			return initial;
		}
		IReadOnlyList<PageDesignerHierarchySchema> full =
			hierarchyClient.GetParentSchemas(rootSchemaUId, designPackageUId);
		return full.Count > 0 ? full : initial;
	}

	private static IReadOnlyList<ClassicListColumnInfo> BuildColumnInfo(
		IEnumerable<string> paths,
		IReadOnlyList<EntitySchemaPropertyColumnInfo> metadata) {
		var captions = (metadata ?? [])
			.Where(column => !string.IsNullOrWhiteSpace(column.Name))
			.GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.First().Title, StringComparer.OrdinalIgnoreCase);
		// KNOWN LIMITATION — a dotted lookup-traversal path (`Account.PrimaryContact.Name`, which the parser
		// deliberately harvests) has no entry in this map: the metadata describes the section's OWN entity, keyed
		// by direct column name. Such a column comes back with `caption` omitted, which the doc states so a
		// consumer reads it as "traversal path" rather than "unknown column". Do NOT fall back to the last
		// segment's local title — that would attach a caption from the wrong entity, which is worse than none.
		return paths.Select(path => new ClassicListColumnInfo(path,
			captions.TryGetValue(path, out string caption) ? caption : null)).ToArray();
	}

	private static GetClassicListColumnsResponse Success(
		string sectionSchema,
		string entity,
		string source,
		IReadOnlyList<ClassicListColumnInfo> columns,
		IReadOnlyList<string> notes) => new() {
			Success = true,
			SectionSchema = sectionSchema,
			Entity = entity,
			Source = source,
			Columns = columns,
			Notes = notes
		};
}
