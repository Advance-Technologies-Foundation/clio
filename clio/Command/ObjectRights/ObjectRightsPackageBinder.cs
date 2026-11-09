using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.ObjectRights;
using Clio.Package;

namespace Clio.Command.ObjectRights;

/// <summary>A binding of the object's operation permission rows that another package or name already holds.</summary>
/// <param name="BindingName">The binding's name.</param>
/// <param name="PackageName">The package that holds it.</param>
public sealed record ExistingObjectRightsBinding(string BindingName, string PackageName);

/// <summary>
/// Where the object's operation permission rows are bound: the package, the binding name, and the packages that hold
/// the object's schema (and with it the "Use operation permissions" switch). <see cref="Refusal"/> is set when no
/// binding may be written, and the call must then change nothing.
/// </summary>
/// <param name="PackageName">The package that receives the binding.</param>
/// <param name="PackageUId">The UId of that package.</param>
/// <param name="BindingName">The binding name, <c>SysEntitySchemaOperationRight_&lt;object code&gt;</c>.</param>
/// <param name="SchemaPackages">The packages that hold a layer of the object's schema, the base one first.</param>
/// <param name="Existing">A binding of these rows found in another package or under another name; the call then
/// writes no second one.</param>
/// <param name="Refusal">Why nothing may be written; <see langword="null"/> when the target is usable.</param>
public sealed record ObjectRightsBindingTarget(
	string PackageName,
	Guid PackageUId,
	string BindingName,
	IReadOnlyList<string> SchemaPackages,
	ExistingObjectRightsBinding Existing = null,
	string Refusal = null) {

	/// <summary>Whether the binding may be written.</summary>
	public bool Refused => Refusal is not null;

	/// <summary>Whether the package that receives the binding also holds a layer of the object's schema.</summary>
	public bool SwitchInSamePackage =>
		SchemaPackages.Contains(PackageName, StringComparer.OrdinalIgnoreCase);

	/// <summary>A refused target.</summary>
	/// <param name="refusal">Why nothing may be written.</param>
	/// <returns>The refused target.</returns>
	public static ObjectRightsBindingTarget Refuse(string refusal) => new(null, Guid.Empty, null, [], Refusal: refusal);
}

/// <summary>What the binding write did.</summary>
public enum ObjectRightsBindingStatus {
	/// <summary>The binding was created or updated with the object's rows.</summary>
	Written,
	/// <summary>The object has no stored rows, and the binding that carried them was removed.</summary>
	Removed,
	/// <summary>The object has no stored rows and there was no binding: nothing to bind.</summary>
	NothingToBind,
	/// <summary>The rows are bound in another binding, which was left as it is.</summary>
	LeftToExisting,
	/// <summary>The binding could not be written.</summary>
	Failed
}

/// <summary>The outcome of binding the object's operation permission rows.</summary>
/// <param name="Status">What was done.</param>
/// <param name="RowCount">How many rows the binding now holds.</param>
/// <param name="Error">Why the write failed; <see langword="null"/> otherwise.</param>
public sealed record ObjectRightsBindingResult(ObjectRightsBindingStatus Status, int RowCount = 0, string Error = null);

/// <summary>
/// Keeps an object's operation permission rows (<c>SysEntitySchemaOperationRight</c>) bound to a package, so they
/// install with it on another site. One binding per object, <c>SysEntitySchemaOperationRight_&lt;object code&gt;</c>,
/// holding exactly the rows stored for the object, re-saved after every change.
/// </summary>
/// <remarks>
/// Package install inserts and updates bound rows; it never deletes a row that is not in the binding, so a row removed
/// here leaves the binding but stays on a site that already installed it
/// (docs/knowledge/platform/package-install-never-deletes-rows-missing-from-a-binding.md). A row references its role
/// by <c>SysAdminUnit</c> id, so it lands only where that role exists under the same id.
/// </remarks>
public interface IObjectRightsPackageBinder {

	/// <summary>
	/// Decides where the object's rows are bound, before anything is written: the named package, or the package of the
	/// object's base schema. A package that does not exist or is locked is refused, naming a package that can be used.
	/// The object's own <c>SysEntitySchemaOperationRight_&lt;code&gt;</c> binding in an editable package other than the
	/// target refuses the call too, so a change never leaves that binding stale. Also finds a binding of these rows made
	/// by hand under another name, which is then reported instead of writing a second one. Reads only.
	/// </summary>
	/// <param name="schemaName">The object's code.</param>
	/// <param name="schemaUId">The UId of the object's base schema, which the rows reference.</param>
	/// <param name="packageName">The package the caller named; blank for the object's own package, or for the package
	/// that already holds the object's <c>SysEntitySchemaOperationRight_&lt;code&gt;</c> binding when another one does.</param>
	/// <param name="requestOptions">Timeout and deadline of the reads, so they share the call's budget; the package
	/// lookups that go through <see cref="IPackageTargetResolver"/> are not bounded by it.</param>
	/// <returns>The target, or a refused one.</returns>
	ObjectRightsBindingTarget ResolveTarget(string schemaName, Guid schemaUId, string packageName,
		CreatioRequestOptions requestOptions);

	/// <summary>
	/// Writes the binding with the rows stored for the object now, in priority order: creates it, updates it in place,
	/// or removes it when the object has no stored rows. Never throws: a failure is returned.
	/// </summary>
	/// <param name="target">A target <see cref="ResolveTarget"/> did not refuse.</param>
	/// <param name="schemaName">The object's code.</param>
	/// <param name="schemaUId">The UId of the object's base schema.</param>
	/// <returns>What was done.</returns>
	ObjectRightsBindingResult Bind(ObjectRightsBindingTarget target, string schemaName, Guid schemaUId);
}

/// <inheritdoc />
internal sealed class ObjectRightsPackageBinder(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPackageDataBindingWriter bindingWriter,
	IPackageTargetResolver targetResolver) : IObjectRightsPackageBinder {

	internal const string RightsSchema = "SysEntitySchemaOperationRight";

	private const string SubjectSchemaUIdColumn = "SubjectSchemaUId";
	private const string PositionColumn = "Position";

	// Id is the key: the rows are created here, so their ids reach the target with them. The operations and the position
	// are force-updated, so a later install of the package carries a changed row onto a site that already has it.
	internal static readonly IReadOnlyList<string> BoundColumns = [
		"Id", SubjectSchemaUIdColumn, "SysAdminUnit", "CanRead", "CanAppend", "CanEdit", "CanDelete", PositionColumn
	];

	private static readonly DataBindingColumnPolicy ColumnPolicy = new(
		["Id"], [SubjectSchemaUIdColumn, "SysAdminUnit", "CanRead", "CanAppend", "CanEdit", "CanDelete", PositionColumn]);

	private const int EditableInstallType = 0;

	/// <summary>The binding name of an object's rows.</summary>
	/// <param name="schemaName">The object's code.</param>
	/// <returns><c>SysEntitySchemaOperationRight_&lt;object code&gt;</c>.</returns>
	internal static string BindingNameOf(string schemaName) => PackageDataBindingNames.For(RightsSchema, schemaName);

	/// <inheritdoc />
	public ObjectRightsBindingTarget ResolveTarget(string schemaName, Guid schemaUId, string packageName,
		CreatioRequestOptions requestOptions) {
		if (schemaUId == Guid.Empty) {
			return ObjectRightsBindingTarget.Refuse($"the schema UId of '{schemaName}' was not read, so its operation "
				+ "permission rows cannot be found to bind them. Nothing was changed.");
		}
		try {
			IReadOnlyList<SchemaLayerRow> layers = ReadSchemaLayers(schemaName, requestOptions);
			string basePackage = layers.FirstOrDefault(layer => !layer.ExtendParent)?.PackageName
				?? layers.FirstOrDefault()?.PackageName;
			bool named = !string.IsNullOrWhiteSpace(packageName);
			string bindingName = BindingNameOf(schemaName);
			IReadOnlyList<BindingRow> bindings = ReadRightsBindings(requestOptions);
			// The object's own binding, written by an earlier call into another package, stays where it is: without a
			// package argument it is updated there, so a later change never leaves a stale second copy behind.
			BindingRow ownElsewhere = named ? null : bindings.FirstOrDefault(row =>
				string.Equals(row.Name, bindingName, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(row.PackageName, basePackage, StringComparison.OrdinalIgnoreCase));
			if (ownElsewhere is not null) {
				basePackage = ownElsewhere.PackageName;
			}
			if (!named && string.IsNullOrWhiteSpace(basePackage)) {
				return ObjectRightsBindingTarget.Refuse($"the package of '{schemaName}' could not be found, so there is "
					+ $"no package to bind its operation permissions to. {UsablePackageHint()}");
			}
			PackageTargetResolution package = targetResolver.Resolve(named ? packageName : basePackage,
				requireEditable: true);
			if (!package.Success) {
				return ObjectRightsBindingTarget.Refuse(named
					? $"{package.Error} {UsablePackageHint()}"
					: $"'{schemaName}' is in package '{basePackage}', which cannot receive its operation permissions: "
						+ $"{package.Error} {UsablePackageHint()}");
			}
			IReadOnlyList<string> schemaPackages = layers.Select(layer => layer.PackageName)
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			// The object's own binding in another package would keep the previous rows while this call changes them, so
			// the call is refused before any write, naming the package that holds it.
			BindingRow ownInOtherPackage = bindings.FirstOrDefault(row =>
				string.Equals(row.Name, bindingName, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(row.PackageName, package.PackageName, StringComparison.OrdinalIgnoreCase));
			if (ownInOtherPackage is not null) {
				return ObjectRightsBindingTarget.Refuse($"the operation permissions of '{schemaName}' are already bound "
					+ $"in package '{ownInOtherPackage.PackageName}' (binding '{bindingName}'); binding them to "
					+ $"'{package.PackageName}' would leave that binding with the previous rows. Pass "
					+ $"package='{ownInOtherPackage.PackageName}', or remove that binding first. Nothing was changed.");
			}
			ExistingObjectRightsBinding existing = FindOtherBinding(bindings, schemaUId, package.PackageName, bindingName,
				requestOptions);
			return new ObjectRightsBindingTarget(package.PackageName, package.PackageUId, bindingName, schemaPackages,
				existing);
		} catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			return ObjectRightsBindingTarget.Refuse("the package to bind the operation permissions to could not be read: "
				+ ObjectRightsSupport.DisplayFailure(ex));
		}
	}

	/// <inheritdoc />
	public ObjectRightsBindingResult Bind(ObjectRightsBindingTarget target, string schemaName, Guid schemaUId) {
		if (target.Existing is not null) {
			return new ObjectRightsBindingResult(ObjectRightsBindingStatus.LeftToExisting);
		}
		try {
			IReadOnlyList<Guid> rowIds = ReadStoredRowIds(schemaUId, null);
			PackageRef package = new(target.PackageUId, target.PackageName);
			PackageDataBindingRef binding = bindingWriter.FindBinding(target.PackageUId, target.BindingName);
			if (binding is not null && !string.IsNullOrWhiteSpace(binding.EntitySchemaName)
				&& !string.Equals(binding.EntitySchemaName, RightsSchema, StringComparison.OrdinalIgnoreCase)) {
				return new ObjectRightsBindingResult(ObjectRightsBindingStatus.Failed, Error:
					$"package '{target.PackageName}' has a binding named '{target.BindingName}' that delivers "
					+ $"'{binding.EntitySchemaName}', not {RightsSchema}; it was left as it is");
			}
			if (rowIds.Count == 0) {
				// An empty set is not how a binding goes: an existing one is removed, so it no longer ships rows the
				// object does not have.
				if (binding is null) {
					return new ObjectRightsBindingResult(ObjectRightsBindingStatus.NothingToBind);
				}
				bindingWriter.DeleteBinding(package, target.BindingName);
				return new ObjectRightsBindingResult(ObjectRightsBindingStatus.Removed);
			}
			DataBindingDbSchema schema = bindingWriter.ProjectSchema(RightsSchema, BoundColumns);
			bindingWriter.SaveBinding(package, target.BindingName, RightsSchema, schema,
				rowIds.Select(id => id.ToString()).ToArray(), binding?.UId, ColumnPolicy);
			return new ObjectRightsBindingResult(ObjectRightsBindingStatus.Written, rowIds.Count);
		} catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			return new ObjectRightsBindingResult(ObjectRightsBindingStatus.Failed,
				Error: ObjectRightsSupport.DisplayFailure(ex));
		}
	}

	// The package a caller can name instead: the CurrentPackageId package when it accepts changes.
	private string UsablePackageHint() {
		PackageTargetResolution current = targetResolver.Resolve(null, requireEditable: true);
		return current.Success
			? $"Pass package='{current.PackageName}' (the CurrentPackageId package) or another editable package. "
				+ "Nothing was changed."
			: "Pass package naming an editable package (unlocked, InstallType 0). Nothing was changed.";
	}

	// A read bounded by the call's options when it has them; after the save the binding reads run unbounded.
	private T Select<T>(object query, CreatioRequestOptions requestOptions)
		where T : SelectQueryHelper.SelectQueryResponseBaseDto {
		if (requestOptions is null) {
			return SelectQueryHelper.ExecuteSelectQuery<T>(applicationClient, serviceUrlBuilder, query);
		}
		CreatioRequestOptions send = requestOptions.ForNextRequest();
		return SelectQueryHelper.ExecuteSelectQuery<T>(applicationClient, serviceUrlBuilder, query, send.TimeOut,
			send.MaxAttempts, send.RetryDelay);
	}

	private IReadOnlyList<SchemaLayerRow> ReadSchemaLayers(string schemaName, CreatioRequestOptions requestOptions) {
		SchemaLayerResponse response = Select<SchemaLayerResponse>(SelectQueryHelper.BuildSelectQuery("SysSchema",
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("ExtendParent", "ExtendParent",
						OrderDirection: 1, OrderPosition: 0),
					new SelectQueryHelper.SelectQueryColumnDefinition("SysPackage.Name", "PackageName")
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition("Name", schemaName,
						SelectQueryHelper.TextDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "EntitySchemaManager",
						SelectQueryHelper.TextDataValueType)
				]), requestOptions);
		return response.Rows ?? [];
	}

	// The rows stored for the object, highest priority first; the binding holds them in this order.
	private IReadOnlyList<Guid> ReadStoredRowIds(Guid schemaUId, CreatioRequestOptions requestOptions) {
		RightRowResponse response = Select<RightRowResponse>(SelectQueryHelper.BuildSelectQuery(RightsSchema,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition(PositionColumn, PositionColumn,
						OrderDirection: 1, OrderPosition: 0),
					new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(SubjectSchemaUIdColumn, schemaUId.ToString(),
						SelectQueryHelper.GuidDataValueType)
				]), requestOptions);
		return (response.Rows ?? []).Select(row => row.Id).Where(id => id != Guid.Empty).ToArray();
	}

	// The SysEntitySchemaOperationRight bindings of editable packages. A locked package is not where the caller's own
	// binding would be, and reading every product binding would cost a request each.
	private IReadOnlyList<BindingRow> ReadRightsBindings(CreatioRequestOptions requestOptions) {
		SchemaDataBindingResponse response = Select<SchemaDataBindingResponse>(SelectQueryHelper.BuildSelectQuery("SysPackageSchemaData",
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId"),
					new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name"),
					new SelectQueryHelper.SelectQueryColumnDefinition("SysPackage.Name", "PackageName")
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition("SysSchema.Name", RightsSchema,
						SelectQueryHelper.TextDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition("SysPackage.InstallType", EditableInstallType,
						SelectQueryHelper.IntDataValueType)
				]), requestOptions);
		return response.Rows ?? [];
	}

	// A binding of this object's rows made by hand: by its rows' object, or by a row id this object holds. A binding
	// under the naming convention belongs to the object its name says, so only bindings named otherwise are read row by
	// row — the scan does not grow with the bindings this tool writes. Once the target package holds the object's own
	// binding, an earlier bind already ran this check, so later calls skip the reads.
	private ExistingObjectRightsBinding FindOtherBinding(IReadOnlyList<BindingRow> bindings, Guid schemaUId,
		string packageName, string bindingName, CreatioRequestOptions requestOptions) {
		bool ownInTarget = bindings.Any(row =>
			string.Equals(row.Name, bindingName, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(row.PackageName, packageName, StringComparison.OrdinalIgnoreCase));
		if (ownInTarget) {
			return null;
		}
		string conventionPrefix = BindingNameOf(string.Empty);
		BindingRow[] byHand = bindings.Where(row => row.UId != Guid.Empty && row.Name is not null
				&& !row.Name.StartsWith(conventionPrefix, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		if (byHand.Length == 0) {
			return null;
		}
		HashSet<Guid> rowIds = ReadStoredRowIds(schemaUId, requestOptions).ToHashSet();
		BindingRow holder = byHand.FirstOrDefault(row => bindingWriter.FetchBoundRows(row.UId, requestOptions).Any(bound =>
			ReadGuid(bound, SubjectSchemaUIdColumn) == schemaUId || rowIds.Contains(ReadGuid(bound, "Id"))));
		return holder is null ? null : new ExistingObjectRightsBinding(holder.Name, holder.PackageName);
	}

	// A bound value is a plain value, or a lookup object carrying it under "value".
	private static Guid ReadGuid(IReadOnlyDictionary<string, JsonNode> row, string column) {
		JsonNode node = row.FirstOrDefault(pair => string.Equals(pair.Key, column, StringComparison.OrdinalIgnoreCase))
			.Value;
		if (node is JsonObject lookup) {
			node = lookup["value"];
		}
		return node is JsonValue value && value.TryGetValue(out string text) && Guid.TryParse(text, out Guid id)
			? id
			: Guid.Empty;
	}

	private sealed class SchemaLayerResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<SchemaLayerRow> Rows { get; init; }
	}

	private sealed class SchemaLayerRow {
		[JsonPropertyName("ExtendParent")]
		public bool ExtendParent { get; init; }

		[JsonPropertyName("PackageName")]
		public string PackageName { get; init; }
	}

	private sealed class RightRowResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<RightRow> Rows { get; init; }
	}

	private sealed class RightRow {
		[JsonPropertyName("Id")]
		public Guid Id { get; init; }
	}

	private sealed class SchemaDataBindingResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<BindingRow> Rows { get; init; }
	}

	private sealed class BindingRow {
		[JsonPropertyName("UId")]
		public Guid UId { get; init; }

		[JsonPropertyName("Name")]
		public string Name { get; init; }

		[JsonPropertyName("PackageName")]
		public string PackageName { get; init; }
	}
}
