using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Discriminates how a <c>sync-schemas</c> create operation must converge against the current
/// server state of the target schema.
/// </summary>
public enum SchemaConvergenceOutcome {
	/// <summary>The schema is absent and must be created.</summary>
	Create,

	/// <summary>The schema already exists in the target package and only the missing columns must be added.</summary>
	Reconcile,

	/// <summary>The schema already exists in the target package and matches the requested shape; no mutation is required.</summary>
	AlreadySatisfied,

	/// <summary>The requested schema durably collides with an existing schema (different package, or an incompatible parent/kind in the target package).</summary>
	Collision
}

/// <summary>
/// The target schema a <c>sync-schemas</c> create operation intends to converge to. Data-only carrier.
/// </summary>
/// <param name="EnvironmentName">The environment the operation resolves under.</param>
/// <param name="PackageName">The target package the caller intends to own the schema.</param>
/// <param name="SchemaName">The requested schema name.</param>
/// <param name="RequestedParentSchemaName">The requested parent schema (e.g. <c>BaseLookup</c> for a lookup); may be <see langword="null"/>.</param>
/// <param name="IsLookup">Whether the operation is a <c>create-lookup</c>.</param>
/// <param name="ExtendParent">Whether the operation creates a REPLACEMENT schema (a same-name schema in the target package that shadows a same-name base schema in a lower package).</param>
/// <param name="RequestedColumns">The columns requested for the schema.</param>
public sealed record SchemaConvergenceTarget(
	string EnvironmentName,
	string PackageName,
	string SchemaName,
	string? RequestedParentSchemaName,
	bool IsLookup,
	bool ExtendParent,
	IReadOnlyList<CreateEntitySchemaColumnArgs> RequestedColumns);

/// <summary>
/// The convergence plan computed from the current server state of a target schema. Data-only carrier.
/// </summary>
/// <param name="Outcome">How the operation must converge.</param>
/// <param name="ColumnsToAdd">The requested columns that are absent on the server and must be added (empty unless <see cref="SchemaConvergenceOutcome.Reconcile"/>).</param>
/// <param name="ColumnsToModify">Requested columns present on the server with a differing shape, surfaced for the Story-2 modify write path (Story 1 does not apply them).</param>
/// <param name="CollisionPackageName">The owning package of the colliding schema; set only when <see cref="Outcome"/> is <see cref="SchemaConvergenceOutcome.Collision"/>.</param>
/// <param name="Error">A user-friendly <c>Error: {message}</c> string; set only when <see cref="Outcome"/> is <see cref="SchemaConvergenceOutcome.Collision"/>.</param>
public sealed record SchemaConvergencePlan(
	SchemaConvergenceOutcome Outcome,
	IReadOnlyList<CreateEntitySchemaColumnArgs> ColumnsToAdd,
	IReadOnlyList<UpdateEntitySchemaOperationArgs> ColumnsToModify,
	string? CollisionPackageName,
	string? Error);

/// <summary>
/// Reads the current server state of a target schema and computes the convergence delta for a
/// <c>sync-schemas</c> create operation (create-if-absent, add-missing-columns, or fail on a durable
/// collision). All reads go through the existing environment-scoped commands server-side, so no new
/// backend endpoint and no extra MCP round-trip are introduced.
/// </summary>
public interface ISchemaConvergenceService {
	/// <summary>
	/// Classifies the target schema against its current server state and returns the convergence plan.
	/// </summary>
	/// <param name="target">The schema the caller intends to converge to.</param>
	/// <returns>A plan describing whether to create, reconcile, no-op, or fail on a durable collision.</returns>
	SchemaConvergencePlan Classify(SchemaConvergenceTarget target);

	/// <summary>
	/// Reads the current column set of a schema keyed by column name (case-insensitive) for the per-column
	/// reconcile of an <c>update-entity</c> operation. This is the single server-side column read (1 read/op)
	/// the update path uses; it never reads existence/package and never issues a mutation. Returns an empty
	/// map when the schema has no columns.
	/// </summary>
	/// <param name="environmentName">The environment the read resolves under.</param>
	/// <param name="schemaName">The schema whose columns are read.</param>
	/// <returns>The existing columns keyed by name (case-insensitive).</returns>
	IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> ReadColumns(string environmentName, string schemaName);
}

/// <summary>
/// Default <see cref="ISchemaConvergenceService"/> implementation. Existence, owning package, and parent
/// are read globally via <see cref="FindEntitySchemaCommand"/> (the only surface that can see a
/// cross-package collision); column detail is read via <see cref="GetEntitySchemaPropertiesCommand"/>
/// in the merged/effective view so a column present in any package layer is not treated as absent.
/// </summary>
public sealed class SchemaConvergenceService(IToolCommandResolver commandResolver) : ISchemaConvergenceService {

	/// <inheritdoc/>
	public SchemaConvergencePlan Classify(SchemaConvergenceTarget target) {
		ArgumentNullException.ThrowIfNull(target);
		IReadOnlyList<CreateEntitySchemaColumnArgs> requestedColumns = target.RequestedColumns ?? [];

		EntitySchemaSearchResult? existing = FindExistingSchema(target);
		if (existing is null) {
			// Absent → create. The create command applies the requested columns inline, so the plan
			// carries no add/modify delta.
			return new SchemaConvergencePlan(SchemaConvergenceOutcome.Create, [], [], null, null);
		}

		// Compare the CALLER-SUPPLIED package trimmed so a stray leading/trailing space in the requested
		// package does not spuriously mismatch the (untrimmed) server value and manufacture a cross-package
		// collision. Only the comparison is normalized; the real server value is preserved for CollisionPackageName/error text.
		bool inTargetPackage = string.Equals(existing.PackageName, target.PackageName?.Trim(), StringComparison.OrdinalIgnoreCase);
		if (!inTargetPackage) {
			// A REPLACEMENT schema (extend-parent) is by definition a same-name schema created in the
			// target package that shadows a same-name base schema in a LOWER package. On the first run the
			// replacement does not yet exist in the target package, so the only match is the base row in a
			// different package — that is the schema to replace (create), NOT a durable collision. When
			// extend-parent is false, a same-name schema in another package IS the masked-collision hole.
			if (target.ExtendParent) {
				return new SchemaConvergencePlan(SchemaConvergenceOutcome.Create, [], [], null, null);
			}
			string message =
				$"Error: schema '{target.SchemaName}' already exists in package '{existing.PackageName}'. "
				+ "Reuse the existing schema by referencing it without creation, or delete the stale version before recreating.";
			return new SchemaConvergencePlan(SchemaConvergenceOutcome.Collision, [], [], existing.PackageName, message);
		}

		// Same package: a mismatched immediate parent means the caller asked for a different kind of
		// schema than what already exists (e.g. a BaseEntity-derived entity vs. the requested BaseLookup).
		// Fail explicitly instead of reconciling lookup columns onto the wrong-kind schema. A replacement
		// (extend-parent) legitimately derives from the same-name base schema, so the parent gate does not
		// apply to it — the existing target-package row is the caller's replacement to reconcile.
		if (!target.ExtendParent && HasIncompatibleParent(target.RequestedParentSchemaName, existing.ParentSchemaName)) {
			string message =
				$"Error: schema '{target.SchemaName}' already exists in package '{existing.PackageName}' with parent "
				+ $"'{existing.ParentSchemaName}', which is incompatible with the requested parent '{target.RequestedParentSchemaName}'. "
				+ "Delete the stale schema before recreating, or target a different schema name.";
			return new SchemaConvergencePlan(SchemaConvergenceOutcome.Collision, [], [], existing.PackageName, message);
		}

		(IReadOnlyList<CreateEntitySchemaColumnArgs> columnsToAdd,
			IReadOnlyList<UpdateEntitySchemaOperationArgs> columnsToModify) = ComputeColumnDelta(target, requestedColumns);

		if (columnsToAdd.Count == 0 && columnsToModify.Count == 0) {
			return new SchemaConvergencePlan(SchemaConvergenceOutcome.AlreadySatisfied, [], [], null, null);
		}
		return new SchemaConvergencePlan(SchemaConvergenceOutcome.Reconcile, columnsToAdd, columnsToModify, null, null);
	}

	private EntitySchemaSearchResult? FindExistingSchema(SchemaConvergenceTarget target) {
		FindEntitySchemaOptions findOptions = new() {
			Environment = target.EnvironmentName,
			SchemaName = target.SchemaName
		};
		FindEntitySchemaCommand findCommand = commandResolver.Resolve<FindEntitySchemaCommand>(findOptions);
		IReadOnlyList<EntitySchemaSearchResult> results = findCommand.FindSchemas(findOptions);
		// Prefer the row in the target package: if the caller's schema already exists in the target
		// package, that IS the caller's schema (reconcile), even if a same-named schema also exists
		// elsewhere. Only when no row lives in the target package is a match a cross-package collision.
		// Trim the CALLER-SUPPLIED package for the comparison only (server rows keep their real value) so a
		// requested package with stray whitespace still matches its target-package row instead of falling
		// through to a different-package row.
		string? targetPackage = target.PackageName?.Trim();
		return results.FirstOrDefault(result =>
				string.Equals(result.PackageName, targetPackage, StringComparison.OrdinalIgnoreCase))
			?? results.FirstOrDefault();
	}

	private static bool HasIncompatibleParent(string? requestedParent, string? existingParent) {
		if (string.IsNullOrWhiteSpace(requestedParent) || string.IsNullOrWhiteSpace(existingParent)) {
			return false;
		}
		// Trim the CALLER-SUPPLIED parent for the comparison only so a requested parent with stray whitespace
		// is not misclassified as an incompatible-parent collision against the (untrimmed) server value.
		return !string.Equals(requestedParent.Trim(), existingParent, StringComparison.OrdinalIgnoreCase);
	}

	private (IReadOnlyList<CreateEntitySchemaColumnArgs> ColumnsToAdd,
		IReadOnlyList<UpdateEntitySchemaOperationArgs> ColumnsToModify) ComputeColumnDelta(
			SchemaConvergenceTarget target,
			IReadOnlyList<CreateEntitySchemaColumnArgs> requestedColumns) {
		if (requestedColumns.Count == 0) {
			return ([], []);
		}

		IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> existingColumns =
			ReadColumns(target.EnvironmentName, target.SchemaName);
		List<CreateEntitySchemaColumnArgs> columnsToAdd = [];
		List<UpdateEntitySchemaOperationArgs> columnsToModify = [];
		foreach (CreateEntitySchemaColumnArgs column in requestedColumns) {
			string? columnName = column.ResolveName();
			if (string.IsNullOrWhiteSpace(columnName)) {
				continue;
			}
			if (!existingColumns.TryGetValue(columnName, out EntitySchemaPropertyColumnInfo? existingColumn)) {
				columnsToAdd.Add(column);
				continue;
			}
			// Present with a differing type is surfaced to the modify delta. Compare by resolved DataValueType
			// ordinal (not friendly-name string) so a column whose read-back vocabulary diverges from the
			// request vocabulary (e.g. phoneNumber read back as "42", text50 as ShortText) is not misclassified
			// as changed and needlessly reconciled on replay.
			if (!EntitySchemaDesignerSupport.AreColumnTypesEquivalent(column.ResolveType(), existingColumn.Type)) {
				columnsToModify.Add(ToModifyOperation(column));
			}
		}
		return (columnsToAdd, columnsToModify);
	}

	/// <inheritdoc/>
	public IReadOnlyDictionary<string, EntitySchemaPropertyColumnInfo> ReadColumns(string environmentName, string schemaName) {
		// Merged/effective view (no package supplied): columns from every package layer, so a column
		// present in a lower layer is not misclassified as absent and re-added.
		GetEntitySchemaPropertiesOptions options = new() {
			Environment = environmentName,
			SchemaName = schemaName
		};
		GetEntitySchemaPropertiesCommand command = commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(options);
		EntitySchemaPropertiesInfo properties = command.GetSchemaProperties(options);
		Dictionary<string, EntitySchemaPropertyColumnInfo> columns = new(StringComparer.OrdinalIgnoreCase);
		foreach (EntitySchemaPropertyColumnInfo column in properties.Columns ?? []) {
			// A null/blank column name would throw on the dictionary key; skip it and keep reconciling the
			// remaining columns rather than aborting the whole read.
			if (string.IsNullOrWhiteSpace(column.Name)) {
				continue;
			}
			columns[column.Name] = column;
		}
		return columns;
	}

	private static UpdateEntitySchemaOperationArgs ToModifyOperation(CreateEntitySchemaColumnArgs column) {
		return new UpdateEntitySchemaOperationArgs(
			Action: "modify",
			ColumnName: column.ResolveName() ?? string.Empty,
			Type: column.ResolveType(),
			ReferenceSchemaName: column.ResolveReferenceSchemaName(),
			IsRequired: column.ResolveRequired());
	}
}
