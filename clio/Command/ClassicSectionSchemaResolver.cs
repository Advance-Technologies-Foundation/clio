namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json.Linq;

/// <summary>
/// Outcome of a Classic section lookup for one entity.
/// </summary>
/// <param name="SectionSchemaNames">
/// Section (list) schema names bound to the entity through <c>SysModule</c>, in the order the metadata returned
/// them. Empty when the entity has no Classic section — which is a legitimate result, not a failure.
/// </param>
/// <param name="Error">
/// Reason the lookup could not complete (entity not resolvable, DataService failure); <c>null</c> when the lookup
/// ran to completion. An empty <see cref="SectionSchemaNames"/> with a <c>null</c> error means "no section exists".
/// </param>
public sealed record ClassicSectionLookup(IReadOnlyList<string> SectionSchemaNames, string Error);

/// <summary>
/// Resolves the Classic section (list) schema bound to an entity from <c>SysModule</c> metadata rather than from a
/// naming convention.
/// </summary>
/// <remarks>
/// Name derivation (<c>&lt;Entity&gt;Section</c>, <c>&lt;PagePrefix&gt;Section</c>) cannot reach sections whose schema
/// name carries a UId/app-derived infix (e.g. entity <c>ASPContractData</c> -> section
/// <c>ASPContractDatac145c7efSection</c>) or that were simply renamed. The binding is recorded in metadata, so the
/// metadata is the authoritative source and the name conventions are only a fallback.
/// </remarks>
public interface IClassicSectionSchemaResolver {

	/// <summary>Resolves the Classic section schema names bound to <paramref name="entityName"/>.</summary>
	/// <param name="entityName">Entity schema name, e.g. <c>Contact</c>.</param>
	/// <returns>
	/// The resolved section schema names, or an <see cref="ClassicSectionLookup.Error"/> describing why the lookup
	/// could not complete. Never throws: transport and parse failures are reported through the error field so the
	/// caller can degrade to name-derived candidates.
	/// </returns>
	ClassicSectionLookup ResolveSectionSchemaNames(string entityName);
}

/// <inheritdoc />
public sealed class ClassicSectionSchemaResolver : IClassicSectionSchemaResolver {

	// Single-sourced with ClassicEntitySchemaQuery so the sentinel/cap cannot drift between the shared query
	// builder and its callers.
	private const string EmptyGuid = ClassicEntitySchemaQuery.EmptyGuid;
	private const int SectionRowCount = ClassicEntitySchemaQuery.SectionRowCount;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>Initializes a new instance of the <see cref="ClassicSectionSchemaResolver"/> class.</summary>
	public ClassicSectionSchemaResolver(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <inheritdoc />
	public ClassicSectionLookup ResolveSectionSchemaNames(string entityName) {
		if (string.IsNullOrWhiteSpace(entityName)) {
			return new ClassicSectionLookup(Array.Empty<string>(), "entity name is required");
		}
		try {
			(string entityUId, string entityError) =
				ClassicEntitySchemaQuery.ResolveEntityUId(_applicationClient, _serviceUrlBuilder, entityName);
			if (entityUId == null) {
				return new ClassicSectionLookup(Array.Empty<string>(), entityError);
			}
			JArray moduleRows = ClassicEntitySchemaQuery.Select(
				_applicationClient, _serviceUrlBuilder, BuildSelectSections(entityUId));
			string[] sectionUIds = moduleRows
				.Select(row => row["SectionSchemaUId"]?.ToString())
				.Where(uId => !string.IsNullOrWhiteSpace(uId) && uId != EmptyGuid)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (sectionUIds.Length == 0) {
				return new ClassicSectionLookup(Array.Empty<string>(), null);
			}
			JArray schemaRows = ClassicEntitySchemaQuery.Select(
				_applicationClient, _serviceUrlBuilder, ClassicEntitySchemaQuery.BuildSelectSchemaNamesByUId(sectionUIds));
			// Preserve the SysModule row order: the first module bound to the entity is the one a migration plan
			// treats as "the" section, and a UId the SysSchema lookup did not return is simply dropped.
			var nameByUId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (JToken row in schemaRows) {
				string uId = row["UId"]?.ToString();
				string name = row["Name"]?.ToString();
				if (!string.IsNullOrWhiteSpace(uId) && !string.IsNullOrWhiteSpace(name)) {
					nameByUId[uId] = name;
				}
			}
			List<string> names = sectionUIds
				.Where(nameByUId.ContainsKey)
				.Select(uId => nameByUId[uId])
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			return new ClassicSectionLookup(names, null);
		}
		catch (Exception ex) {
			return new ClassicSectionLookup(Array.Empty<string>(), ex.Message);
		}
	}

	// Column-set-specific selects (kept local); the DSL + entity resolution are single-sourced in
	// ClassicEntitySchemaQuery so this resolver and ListEntityClientSchemasCommand cannot drift.
	private static JObject BuildSelectSections(string entityUId) => ClassicEntitySchemaQuery.Query("SysModule",
		new JObject { ["SectionSchemaUId"] = ClassicEntitySchemaQuery.Column("SectionSchemaUId") },
		ClassicEntitySchemaQuery.Group(
			("byEntity", ClassicEntitySchemaQuery.Eq("SysModuleEntity.SysEntitySchemaUId", entityUId, 0))),
		SectionRowCount);

}
