using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;

namespace Clio.Command.EntityConnections;

/// <summary>Input of <see cref="IEntityConnectionsService.Get"/>.</summary>
/// <param name="PackageName">The package whose <c>EntityConnections</c> add-on layer is read.</param>
/// <param name="EntitySchemaName">The object (entity schema) name.</param>
public sealed record EntityConnectionsReadRequest(string PackageName, string EntitySchemaName);

/// <summary>Input of <see cref="IEntityConnectionsService.Update"/>.</summary>
/// <param name="PackageName">The package whose <c>EntityConnections</c> add-on layer is written.</param>
/// <param name="EntitySchemaName">The object (entity schema) name.</param>
/// <param name="Connections">The full desired ordered list of connection column names (codes).</param>
public sealed record EntityConnectionsUpdateRequest(
	string PackageName,
	string EntitySchemaName,
	IReadOnlyList<string> Connections);

/// <summary>One effective connection of an object.</summary>
/// <param name="ColumnName">Column code; <c>null</c> when the object has no column with that UId.</param>
/// <param name="ColumnUId">Column UId.</param>
/// <param name="Caption">Column caption in the current culture (en-US fallback).</param>
/// <param name="ReferenceSchemaName">The object the lookup column references.</param>
/// <param name="Position">Zero-based position in the effective list.</param>
public sealed record EntityConnectionEntry(
	string ColumnName,
	string ColumnUId,
	string Caption,
	string ReferenceSchemaName,
	int Position);

/// <summary>The <c>EntityConnections</c> add-on content read for the package (parent layers folded with it).</summary>
/// <param name="Items">Columns the add-on places first, in position order (column code, or UId when unknown).</param>
/// <param name="Removed">Columns the add-on removes (column code, or UId when unknown).</param>
public sealed record EntityConnectionsPackageLayer(IReadOnlyList<string> Items, IReadOnlyList<string> Removed);

/// <summary>Result of <see cref="IEntityConnectionsService.Get"/>, and the state after <see cref="IEntityConnectionsService.Update"/>.</summary>
/// <param name="EntitySchemaName">The object (entity schema) name as requested.</param>
/// <param name="EntitySchemaUId">The object's persisted schema UId (the add-on target).</param>
/// <param name="PackageName">The package whose add-on layer was read.</param>
/// <param name="Connections">The effective ordered connections.</param>
/// <param name="PackageLayer">The add-on content read for the package.</param>
/// <param name="Warnings">Non-fatal issues; empty when there are none.</param>
public sealed record EntityConnectionsReadResult(
	string EntitySchemaName,
	string EntitySchemaUId,
	string PackageName,
	IReadOnlyList<EntityConnectionEntry> Connections,
	EntityConnectionsPackageLayer PackageLayer,
	IReadOnlyList<string> Warnings);

/// <summary>Result of <see cref="IEntityConnectionsService.Update"/>.</summary>
/// <param name="State">The state read back after the write (or the unchanged state when nothing was written).</param>
/// <param name="Changed"><c>true</c> when the add-on was saved.</param>
public sealed record EntityConnectionsUpdateResult(EntityConnectionsReadResult State, bool Changed);

/// <summary>
/// Reads and writes an object's "Connections" — the Object designer "Connections" node, shown as fields by the
/// Freedom UI <c>crt.Connections</c> component. Connections are stored as <c>EntityConnection</c> rows plus the
/// per-package <c>EntityConnections</c> add-on; clio reads the merged list and writes only the add-on.
/// </summary>
public interface IEntityConnectionsService {

	/// <summary>
	/// Reads the effective ordered connections (<c>EntityConnectionsService.GetConnections</c>, with the
	/// <c>EntityConnection</c> table as a fallback reported in the warnings) and the package's add-on content.
	/// Read-only.
	/// </summary>
	/// <exception cref="ArgumentException">A required argument is missing.</exception>
	/// <exception cref="InvalidOperationException">The package, the object or the connections cannot be read.</exception>
	EntityConnectionsReadResult Get(EntityConnectionsReadRequest request);

	/// <summary>
	/// Makes the effective connections equal the requested ordered list by writing the package's
	/// <c>EntityConnections</c> add-on layer the way the Object designer does (only the difference from the parent
	/// layers), then resets the client script cache and reads the state back. Only lookup columns of the object are
	/// accepted; an unknown, non-lookup or repeated column fails before any write. Writes nothing when the list
	/// already matches.
	/// </summary>
	/// <exception cref="ArgumentException">The request is invalid (missing argument, unknown, non-lookup or repeated column).</exception>
	/// <exception cref="InvalidOperationException">The state cannot be read reliably enough to write, or the save failed.</exception>
	EntityConnectionsUpdateResult Update(EntityConnectionsUpdateRequest request);
}

/// <inheritdoc />
internal sealed class EntityConnectionsService(
	IEntityAddonTargetResolver targetResolver,
	IAddonSchemaDesignerClient addonSchemaDesignerClient,
	IEntityConnectionsReader connectionsReader,
	IEntityConnectionsAddonMetadata addonMetadata)
	: IEntityConnectionsService {

	internal const string AddonName = "EntityConnections";
	internal const string EntitySchemaNameRequired = "entity-schema-name is required.";
	internal const string PackageNameRequired = "package-name is required.";
	internal const string ConnectionsRequired =
		"connections is required: pass the full desired ordered list of column names (an empty list removes every connection).";

	private const string TargetSchemaUIdInvalid =
		"The EntityConnections add-on response is missing a valid targetSchemaUId.";

	private const string LayerMismatchWarning =
		"The effective list does not match the EntityConnections add-on content (a column the add-on removes is still "
		+ "listed). The server probably does not apply the add-on — the UseConnectionsComponent feature may be off on "
		+ "this environment.";

	private const string ItemOrderMismatchWarning =
		"The effective list does not start with the EntityConnections add-on items of this package. An add-on layer "
		+ "of another package (outside this package's parents) may place or remove connections, or the server does "
		+ "not apply the add-on because the UseConnectionsComponent feature is off on this environment.";

	internal const string ReadBackFailedWarningPrefix = "Connections were saved, but reading them back failed: ";

	internal const string OrderNotVerifiedWarning =
		"Connections were saved, but the written order could not be verified: the list was read back from the "
		+ "EntityConnection table, which does not reflect the EntityConnections add-on.";

	/// <inheritdoc />
	public EntityConnectionsReadResult Get(EntityConnectionsReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateTarget(request.EntitySchemaName, request.PackageName);
		State state = ReadState(request.PackageName, request.EntitySchemaName);
		return ToResult(request.EntitySchemaName, request.PackageName, state, []);
	}

	/// <inheritdoc />
	public EntityConnectionsUpdateResult Update(EntityConnectionsUpdateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateTarget(request.EntitySchemaName, request.PackageName);
		if (request.Connections is null) {
			throw new ArgumentException(ConnectionsRequired);
		}
		ValidateNames(request.Connections);

		State state = ReadState(request.PackageName, request.EntitySchemaName);
		List<Guid> desired = ResolveDesiredColumns(request.Connections, state.Target.EntitySchema);
		if (state.Snapshot.IsTableFallback) {
			// The Object designer makes the node read-only in this state: the list misses the add-on items and
			// removals, so a diff computed from it would rewrite the add-on from an incomplete list.
			throw new InvalidOperationException(
				"Connections were not changed: " + state.Snapshot.Warning
				+ " Writing the add-on from that list could drop changes made by other packages.");
		}

		// A column the server still lists but package-name cannot resolve exists on the object (another package
		// outside this package's hierarchy adds it), so it is not deleted: it stays out of the diff, is never added
		// to the removals, and is kept as it is.
		HashSet<Guid> known = KnownColumns(state.Target.EntitySchema);
		HashSet<Guid> unresolvable = [.. state.Snapshot.ColumnUIds.Where(columnUId => !known.Contains(columnUId))];
		// A column the object no longer has leaves the stored content: the server rejects an add-on that still
		// references it (the same clean-up both designers do before a save).
		HashSet<Guid> deleted = [.. state.Content.Items.Concat(state.Content.Removed)
			.Where(columnUId => !known.Contains(columnUId) && !unresolvable.Contains(columnUId))];
		EntityConnectionsAddonContent baseContent = addonMetadata.WithoutColumns(state.Content, deleted);
		List<Guid> effective = state.Snapshot.ColumnUIds.Where(columnUId => !unresolvable.Contains(columnUId)).ToList();
		// Only a removed column that is still listed proves the add-on is not applied: removals of all layers are
		// united, so no other package can bring it back. Items out of place can come from another package's layer.
		if (!AreRemovalsReflected(baseContent, effective)) {
			throw new InvalidOperationException("Connections were not changed: " + LayerMismatchWarning);
		}

		EntityConnectionsAddonContent metadata = addonMetadata.Build(baseContent, effective, desired)
			?? (ReferenceEquals(baseContent, state.Content) ? null : baseContent);
		if (metadata is null) {
			return new EntityConnectionsUpdateResult(
				ToResult(request.EntitySchemaName, request.PackageName, state, []), false);
		}

		state.Schema.MetaData = addonMetadata.Serialize(metadata);
		addonSchemaDesignerClient.SaveSchema(state.Schema);
		// Best-effort, like the related-page add-on: the save is committed, a failed reset is only reported.
		// No BuildConfiguration: connections are read at runtime from EntityConnectionsService, not from static
		// client content, and the Object designer only resets the script cache after saving the add-on.
		List<string> warnings = [];
		AddWarning(warnings, addonSchemaDesignerClient.ResetClientScriptCache());

		State after;
		try {
			after = ReadState(request.PackageName, request.EntitySchemaName);
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			// The save is committed: report it as done and describe the state from what was written.
			warnings.Add(ReadBackFailedWarningPrefix + exception.Message);
			State written = state with {
				Content = metadata,
				Snapshot = new EntityConnectionsSnapshot(desired, false, null)
			};
			return new EntityConnectionsUpdateResult(
				ToResult(request.EntitySchemaName, request.PackageName, written, warnings), true);
		}
		if (after.Snapshot.IsTableFallback) {
			warnings.Add(OrderNotVerifiedWarning);
		} else {
			HashSet<Guid> knownAfter = KnownColumns(after.Target.EntitySchema);
			// Columns package-name cannot resolve were left as they are, so they do not take part in the comparison.
			if (!after.Snapshot.ColumnUIds.Where(knownAfter.Contains).SequenceEqual(desired)) {
				warnings.Add(
					"The add-on was saved, but the effective list differs from the requested one. A column removed by "
					+ "another package layer cannot be brought back from this package (removals of all layers are united), "
					+ "and the server may not apply the add-on when the UseConnectionsComponent feature is off.");
			}
		}
		return new EntityConnectionsUpdateResult(
			ToResult(request.EntitySchemaName, request.PackageName, after, warnings), true);
	}

	private static HashSet<Guid> KnownColumns(EntityDesignSchemaDto entitySchema) =>
		[.. SchemaColumns(entitySchema).Select(column => column.UId)];

	private static void ValidateTarget(string entitySchemaName, string packageName) {
		if (string.IsNullOrWhiteSpace(entitySchemaName)) {
			throw new ArgumentException(EntitySchemaNameRequired);
		}
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new ArgumentException(PackageNameRequired);
		}
	}

	private static void ValidateNames(IReadOnlyList<string> connections) {
		if (connections.Any(string.IsNullOrWhiteSpace)) {
			throw new ArgumentException("connections must not contain an empty column name.");
		}
		string[] repeated = connections
			.GroupBy(name => name.Trim(), StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToArray();
		if (repeated.Length > 0) {
			throw new ArgumentException(
				$"connections lists a column more than once: {string.Join(", ", repeated)}. Each column can be one connection.");
		}
	}

	private static List<Guid> ResolveDesiredColumns(IReadOnlyList<string> connections, EntityDesignSchemaDto entitySchema) {
		Dictionary<string, EntitySchemaColumnDto> byName = new(StringComparer.OrdinalIgnoreCase);
		// Own columns first, so an own column wins over an inherited one with the same name.
		foreach (EntitySchemaColumnDto column in SchemaColumns(entitySchema)
			.Where(column => !string.IsNullOrWhiteSpace(column.Name))) {
			byName.TryAdd(column.Name, column);
		}
		List<string> unknown = [];
		List<string> notLookup = [];
		List<Guid> result = [];
		foreach (string name in connections.Select(name => name.Trim())) {
			if (!byName.TryGetValue(name, out EntitySchemaColumnDto column)) {
				unknown.Add($"'{name}'");
				continue;
			}
			if (!column.IsLookupType()) {
				notLookup.Add($"'{column.Name}' ({DescribeDataValueType(column.DataValueType)})");
				continue;
			}
			result.Add(column.UId);
		}
		List<string> errors = [];
		if (unknown.Count > 0) {
			errors.Add($"Object '{entitySchema.Name}' has no column {string.Join(", ", unknown)}.");
		}
		if (notLookup.Count > 0) {
			errors.Add("Only lookup columns can be connections; not a lookup: " + string.Join(", ", notLookup) + ".");
		}
		if (errors.Count > 0) {
			throw new ArgumentException("Connections were not changed. " + string.Join(" ", errors));
		}
		return result;
	}

	// Mirrors the server merge (add-on items by position, then the EntityConnection rows in table order, removals
	// of all layers united and always excluded): the effective list must not contain a removed column. When it does,
	// the server is not applying the add-on and a diff against it is meaningless.
	private static bool AreRemovalsReflected(EntityConnectionsAddonContent content, IReadOnlyList<Guid> effective) {
		HashSet<Guid> removed = [.. content.Removed];
		return !effective.Any(removed.Contains);
	}

	// The effective list starts with the package's items that are not removed. Not a proof that the add-on is
	// ignored: the effective list folds every installed package, so another package's items or removals can
	// shift or drop these items.
	private static bool AreItemsLeading(EntityConnectionsAddonContent content, IReadOnlyList<Guid> effective) {
		HashSet<Guid> removed = [.. content.Removed];
		List<Guid> items = content.Items.Where(columnUId => !removed.Contains(columnUId)).ToList();
		return effective.Count >= items.Count && effective.Take(items.Count).SequenceEqual(items);
	}

	private State ReadState(string packageName, string entitySchemaName) {
		EntityAddonTarget target = targetResolver.Resolve(packageName, entitySchemaName);
		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(
			EntityAddonTargetResolver.BuildGetRequest(target.EntitySchema, target.PackageId, AddonName));
		Guid entitySchemaUId = EntityAddonTargetResolver.TryReadTargetSchemaUId(schema)
			?? throw new InvalidOperationException(TargetSchemaUIdInvalid);
		EntityConnectionsAddonContent content = addonMetadata.Parse(schema.MetaData);
		EntityConnectionsSnapshot snapshot = connectionsReader.Read(entitySchemaUId);
		return new State(target, schema, entitySchemaUId, content, snapshot);
	}

	private static EntityConnectionsReadResult ToResult(
		string entitySchemaName, string packageName, State state, IReadOnlyList<string> extraWarnings) {
		Dictionary<Guid, EntitySchemaColumnDto> byUId = [];
		foreach (EntitySchemaColumnDto column in SchemaColumns(state.Target.EntitySchema)) {
			byUId.TryAdd(column.UId, column);
		}
		List<EntityConnectionEntry> connections = state.Snapshot.ColumnUIds
			.Select((columnUId, position) => {
				byUId.TryGetValue(columnUId, out EntitySchemaColumnDto column);
				return new EntityConnectionEntry(
					column?.Name,
					columnUId.ToString("D"),
					column is null ? null : EntitySchemaDesignerSupport.GetLocalizableValue(column.Caption),
					column?.ReferenceSchema?.Name,
					position);
			})
			.ToList();
		string NameOf(Guid columnUId) =>
			byUId.TryGetValue(columnUId, out EntitySchemaColumnDto column) && !string.IsNullOrWhiteSpace(column.Name)
				? column.Name
				: columnUId.ToString("D");

		List<string> warnings = [];
		AddWarning(warnings, state.Snapshot.Warning);
		List<string> unresolvable = state.Snapshot.ColumnUIds
			.Where(columnUId => !byUId.ContainsKey(columnUId))
			.Select(columnUId => columnUId.ToString("D"))
			.ToList();
		if (unresolvable.Count > 0) {
			warnings.Add(
				$"{unresolvable.Count} connection(s) point to columns not visible from package '{packageName}'; they "
				+ $"are kept as they are and must not be sent back: {string.Join(", ", unresolvable)}.");
		}
		if (!state.Snapshot.IsTableFallback) {
			if (!AreRemovalsReflected(state.Content, state.Snapshot.ColumnUIds)) {
				warnings.Add(LayerMismatchWarning);
			} else if (!AreItemsLeading(state.Content, state.Snapshot.ColumnUIds)) {
				warnings.Add(ItemOrderMismatchWarning);
			}
		}
		foreach (string warning in extraWarnings) {
			AddWarning(warnings, warning);
		}
		return new EntityConnectionsReadResult(
			entitySchemaName,
			state.EntitySchemaUId.ToString("D"),
			packageName,
			connections,
			new EntityConnectionsPackageLayer(
				state.Content.Items.Select(NameOf).ToList(),
				state.Content.Removed.Select(NameOf).ToList()),
			warnings);
	}

	private static void AddWarning(List<string> warnings, string warning) {
		if (!string.IsNullOrWhiteSpace(warning) && !warnings.Contains(warning)) {
			warnings.Add(warning);
		}
	}

	// The object's own plus inherited columns (a connection is commonly an inherited lookup, e.g. Account.Owner).
	private static IEnumerable<EntitySchemaColumnDto> SchemaColumns(EntityDesignSchemaDto entitySchema) =>
		(entitySchema.Columns ?? []).Concat(entitySchema.InheritedColumns ?? []).Where(column => column is not null);

	private static string DescribeDataValueType(int? dataValueType) {
		if (dataValueType is null) {
			return "unknown type";
		}
		string name = EntitySchemaDesignerSupport.SupportedDataValueTypes
			.FirstOrDefault(pair => pair.Value == dataValueType.Value).Key;
		return name ?? $"data value type {dataValueType.Value}";
	}

	private sealed record State(
		EntityAddonTarget Target,
		AddonSchemaDto Schema,
		Guid EntitySchemaUId,
		EntityConnectionsAddonContent Content,
		EntityConnectionsSnapshot Snapshot);
}
