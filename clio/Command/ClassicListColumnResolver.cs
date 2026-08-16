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
			throw new ArgumentException("schema-name is required", nameof(sectionSchemaName));
		}
		string normalizedName = sectionSchemaName.Trim();
		if (!PageSchemaMetadataHelper.IsValidSchemaName(normalizedName)) {
			throw new ArgumentException(PageSchemaMetadataHelper.SchemaNameFormatError, nameof(sectionSchemaName));
		}

		var notes = new List<string>();
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy = ResolveHierarchy(normalizedName, notes);
		string[] bodies = hierarchy
			.Reverse()
			.Select(schema => schema.Body)
			.Where(body => !string.IsNullOrWhiteSpace(body))
			.ToArray();
		string entity = parser.ParseEntityName(bodies);
		if (string.IsNullOrWhiteSpace(entity)) {
			throw new InvalidOperationException($"Classic section '{normalizedName}' does not declare entitySchemaName.");
		}

		EntitySchemaPropertiesInfo properties = columnManager.GetSchemaProperties(
			new GetEntitySchemaPropertiesOptions { SchemaName = entity });
		ClassicListColumnParseResult parsed = parser.ParseColumns(bodies);
		if (parsed.UnparsedLayerCount > 0) {
			// Without this the drop is invisible: a most-derived layer that fails to parse would silently hand the
			// answer to an ancestor layer — or to the entity fallback — and the caller would read that as the
			// section's real column set.
			notes.Add($"{parsed.UnparsedLayerCount} of {bodies.Length} section schema layers could not be parsed " +
				"as JavaScript and were skipped; the resolved columns may be incomplete.");
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
