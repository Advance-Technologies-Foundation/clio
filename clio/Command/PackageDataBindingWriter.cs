using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

/// <summary>
/// Identity of a package data binding that already exists in the remote environment.
/// </summary>
/// <param name="UId">The <c>SysPackageSchemaData</c> record UId a re-save must reuse.</param>
/// <param name="EntitySchemaName">Name of the entity schema the binding delivers, as the environment reports it.</param>
public sealed record PackageDataBindingRef(Guid UId, string EntitySchemaName);

/// <summary>
/// Reads and writes Creatio package data bindings (<c>SysPackageSchemaData</c>) in a remote environment.
/// Owns everything that is the same for every binding regardless of what it delivers: resolving the target
/// package, resolving and projecting the runtime entity schema, finding an existing binding so a re-save
/// updates it in place, posting SaveSchema, and deleting a binding without touching the rows it delivered.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about which rows belong in a binding or when a name collision matters:
/// choosing rows, install-time matching policies, and collision handling belong to the callers above it.
/// This contract takes no CLI options type and parses no JSON payload, so it is callable from ordinary typed
/// code as well as from a command that parsed its arguments first.
/// </remarks>
internal interface IPackageDataBindingWriter {

	/// <summary>
	/// Resolves a package name to the UId and canonical name the binding endpoints need. A locked package is
	/// not refused here: the binding write is this caller's only effect on the environment, so whether the
	/// package accepts it is left to the write rather than pre-judged by an extra read.
	/// </summary>
	/// <param name="packageName">Name of a package installed in the remote environment.</param>
	/// <returns>The resolved package identity.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when <paramref name="packageName"/> is blank — a binding is always written into a package the
	/// caller chose, never into an environment-level default — and when the name resolves to no usable target.
	/// </exception>
	PackageRef ResolvePackage(string packageName);

	/// <summary>
	/// Fetches an entity schema's full runtime column set.
	/// </summary>
	/// <param name="schemaName">Name of the entity schema.</param>
	/// <returns>The schema descriptor carrying every runtime column.</returns>
	DataBindingDbSchema FetchSchema(string schemaName);

	/// <summary>
	/// Fetches an entity schema and reduces it to <paramref name="columnNames"/>, requiring every requested
	/// column to be present. Projections are cached per writer instance, so one command run that delivers
	/// several folders from the same projection fetches the runtime schema once.
	/// </summary>
	/// <param name="schemaName">Name of the entity schema.</param>
	/// <param name="columnNames">The columns the binding must deliver.</param>
	/// <returns>The schema descriptor reduced to the requested columns.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the environment's schema lacks any requested column. A silently reduced projection is
	/// never acceptable: dropping a key column degrades a natural-key binding into a wildcard that
	/// force-updates every row of the entity on the install target, and dropping a value column ships an
	/// empty snapshot. Both failures are invisible until install, so an absent column is a hard error.
	/// </exception>
	DataBindingDbSchema ProjectSchema(string schemaName, IReadOnlyCollection<string> columnNames);

	/// <summary>
	/// Reads the registration of <paramref name="bindingName"/> in the package, with the entity schema it
	/// delivers, so a re-save updates it in place and a caller can detect a name collision with a binding it
	/// does not own.
	/// </summary>
	/// <param name="packageUId">UId of the package that owns the binding.</param>
	/// <param name="bindingName">The binding folder name.</param>
	/// <returns>The existing binding, or <see langword="null"/> when the package has no binding by that name.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the name has more than one registration in the package — a re-save could not tell which one
	/// to update — or when the registration carries no reusable UId, which would make a re-save register a
	/// second binding under the same name instead of updating the one that is already there.
	/// </exception>
	PackageDataBindingRef FindBinding(Guid packageUId, string bindingName);

	/// <summary>
	/// Creates or refreshes a binding: posts SaveSchema with the desired bound-record set under
	/// <paramref name="existingBindingUId"/> when one was found, or as a new registration otherwise.
	/// Passing a reduced <paramref name="boundRecordIds"/> set drops rows from the binding; an empty set is
	/// not how a binding is removed — use <see cref="DeleteBinding"/> for that.
	/// </summary>
	/// <remarks>
	/// The platform saves the binding to the database only. When file system development mode is enabled,
	/// or its state cannot be read, a warning is logged after the save naming the package folder on disk
	/// that does not have the change and the command that writes it there (<c>pkg-to-file-system</c>).
	/// The warning never fails the save.
	/// </remarks>
	/// <param name="package">The package that receives the binding.</param>
	/// <param name="bindingName">The binding folder name.</param>
	/// <param name="entitySchemaName">Name of the entity schema the binding delivers.</param>
	/// <param name="schema">The projected schema describing the columns the binding delivers.</param>
	/// <param name="boundRecordIds">Ids of the rows the binding delivers.</param>
	/// <param name="existingBindingUId">UId of the binding to update in place, or null to create one.</param>
	/// <param name="columnPolicy">
	/// Optional install-time matching policy. Omit it for rows whose Ids the install target shares; supply
	/// one to deliver an environment-random row by its natural key.
	/// </param>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the environment rejects the save, and when the delivered columns carry no key the install
	/// target could match the rows on — neither <c>Id</c> nor a <paramref name="columnPolicy"/> key column.
	/// </exception>
	void SaveBinding(
		PackageRef package,
		string bindingName,
		string entitySchemaName,
		DataBindingDbSchema schema,
		IReadOnlyList<string> boundRecordIds,
		Guid? existingBindingUId = null,
		DataBindingColumnPolicy columnPolicy = null);

	/// <summary>
	/// Deletes the package's registration of <paramref name="bindingName"/>. Removes only the registration —
	/// the rows the binding delivered stay in the environment. The endpoint keys on (package, name) alone, so
	/// a caller that must not destroy a binding it does not own has to check <see cref="FindBinding"/> first.
	/// </summary>
	/// <remarks>
	/// Like <see cref="SaveBinding"/>, this changes the database only, and logs the same file system
	/// development mode warning: the package folder on disk still carries the binding.
	/// </remarks>
	/// <param name="package">The package that owns the binding.</param>
	/// <param name="bindingName">The binding folder name.</param>
	/// <exception cref="InvalidOperationException">Thrown when the environment rejects the delete.</exception>
	void DeleteBinding(PackageRef package, string bindingName);

	/// <summary>
	/// Returns whether a row with the given primary key exists in the entity table, so a caller can tell a
	/// bindable row from a dangling id. A failed probe is propagated rather than reported as "absent".
	/// </summary>
	/// <param name="schemaName">Name of the entity schema to probe.</param>
	/// <param name="rowId">Primary key of the row.</param>
	/// <returns><see langword="true"/> when the row exists.</returns>
	bool RowExists(string schemaName, Guid rowId);
}

/// <inheritdoc />
internal sealed class PackageDataBindingWriter(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPackageTargetResolver targetResolver,
	IDataBindingSchemaClient schemaClient,
	IFileDesignModeStateReader fileDesignModeStateReader,
	ILogger logger) : IPackageDataBindingWriter {

	private const string PackageSchemaDataSchema = "SysPackageSchemaData";

	/// <summary>
	/// The command that writes a database-only binding change into the package folder on disk. Its CLI
	/// verb and its MCP tool share this name.
	/// </summary>
	internal const string FileSystemExportCommandName = "pkg-to-file-system";

	/// <summary>
	/// The command that loads the package folders on disk into the database. Named in the warning because it
	/// makes the database match the disk, so it must not run before the export.
	/// </summary>
	internal const string FileSystemImportCommandName = "pkg-to-db";

	private readonly Dictionary<string, DataBindingDbSchema> _projectedSchemas = new(StringComparer.Ordinal);

	private bool _fileDesignModeStateRead;
	private bool? _isFileDesignModeEnabled;

	/// <inheritdoc />
	public PackageRef ResolvePackage(string packageName) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new InvalidOperationException("Package name is required to write a package data binding.");
		}
		PackageTargetResolution resolution = targetResolver.Resolve(packageName);
		if (!resolution.Success) {
			throw new InvalidOperationException(resolution.Error);
		}
		return new PackageRef(resolution.PackageUId, resolution.PackageName);
	}

	/// <inheritdoc />
	public DataBindingDbSchema FetchSchema(string schemaName) {
		DataBindingSchema schema = schemaClient.Fetch(schemaName);
		return new DataBindingDbSchema(
			schema.UId,
			schema.Name,
			schema.Columns.Select(column => column.Name).ToList(),
			schema.Columns);
	}

	/// <inheritdoc />
	public DataBindingDbSchema ProjectSchema(string schemaName, IReadOnlyCollection<string> columnNames) {
		string projectionKey = $"{schemaName}({string.Join(",", columnNames)})";
		if (_projectedSchemas.TryGetValue(projectionKey, out DataBindingDbSchema cached)) {
			return cached;
		}
		DataBindingSchema schema = schemaClient.Fetch(schemaName);
		HashSet<string> requested = new(columnNames, StringComparer.OrdinalIgnoreCase);
		List<DataBindingSchemaColumn> projected = schema.Columns
			.Where(column => requested.Contains(column.Name))
			.ToList();

		HashSet<string> found = projected.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		List<string> missingColumns = columnNames
			.Where(name => !found.Contains(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (missingColumns.Count > 0) {
			throw new InvalidOperationException(
				$"Schema '{schemaName}' on this environment is missing column(s) required by the data " +
				$"binding: {string.Join(", ", missingColumns)}. Binding a partial projection would ship an " +
				"incomplete or wildcard-matching binding, so the operation was stopped.");
		}
		DataBindingDbSchema projectedSchema = new(
			schema.UId, schema.Name, projected.Select(column => column.Name).ToList(), projected);
		_projectedSchemas[projectionKey] = projectedSchema;
		return projectedSchema;
	}

	/// <inheritdoc />
	public PackageDataBindingRef FindBinding(Guid packageUId, string bindingName) {
		PackageSchemaDataResponse response = SelectQueryHelper.ExecuteSelectQuery<PackageSchemaDataResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				PackageSchemaDataSchema,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId"),
					new SelectQueryHelper.SelectQueryColumnDefinition("SysSchema.Name", "EntitySchemaName")
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(
						"Name", bindingName, SelectQueryHelper.TextDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition(
						"SysPackage.UId", packageUId.ToString(), SelectQueryHelper.GuidDataValueType)
				]));
		if (response.Rows.Count > 1) {
			throw new InvalidOperationException(
				$"Package data binding '{bindingName}' has multiple registrations in package '{packageUId}'.");
		}
		PackageSchemaDataDto row = response.Rows.FirstOrDefault();
		if (row is null) {
			return null;
		}
		if (!Guid.TryParse(row.UId, out Guid bindingUId) || bindingUId == Guid.Empty) {
			throw new InvalidOperationException(
				$"Package data binding '{bindingName}' in package '{packageUId}' carries an unusable UId " +
				$"'{row.UId}', so a re-save could not update it in place.");
		}
		return new PackageDataBindingRef(bindingUId, row.EntitySchemaName);
	}

	/// <inheritdoc />
	public void SaveBinding(
		PackageRef package,
		string bindingName,
		string entitySchemaName,
		DataBindingDbSchema schema,
		IReadOnlyList<string> boundRecordIds,
		Guid? existingBindingUId = null,
		DataBindingColumnPolicy columnPolicy = null) {
		string requestBody = BuildSaveSchemaDataRequest(
			package, bindingName, entitySchemaName, schema, boundRecordIds.ToList(), existingBindingUId,
			columnPolicy);
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData), requestBody);
		DataServiceResponse.ThrowIfUnsuccessful(response, "SaveSchema");
		WarnIfPackageFolderIsNotUpdated(package, bindingName,
			existingBindingUId.HasValue ? BindingChange.Refreshed : BindingChange.Created);
	}

	/// <inheritdoc />
	public void DeleteBinding(PackageRef package, string bindingName) {
		string body = JsonSerializer.Serialize(new {
			packageUId = package.UId.ToString(),
			packageSchemaDataName = bindingName
		});
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeletePackageSchemaData), body);
		DataServiceResponse.ThrowIfUnsuccessful(response, "DeletePackageSchemaDataRequest");
		WarnIfPackageFolderIsNotUpdated(package, bindingName, BindingChange.Removed);
	}

	/// <summary>
	/// Logs a warning when the change just made may be missing from the package folder on disk. The
	/// platform's binding endpoints write to the database only; in file system development mode the package
	/// folder is what gets committed, so without this warning a binding that "succeeded" never reaches the
	/// next environment. The state is read once per writer instance; the writer is transient, so a caller
	/// that resolves it again reads the state again.
	/// </summary>
	private void WarnIfPackageFolderIsNotUpdated(PackageRef package, string bindingName, BindingChange change) {
		if (!_fileDesignModeStateRead) {
			_isFileDesignModeEnabled = ReadFileDesignModeState();
			_fileDesignModeStateRead = true;
		}
		if (_isFileDesignModeEnabled == false) {
			return;
		}
		logger.WriteWarning(BuildPackageFolderWarning(_isFileDesignModeEnabled, package.Name, bindingName, change));
	}

	/// <summary>
	/// Reads the file system development mode state for the warning, treating every failure as an unknown
	/// state. The binding has already been committed when this runs: an exception escaping here would report
	/// that committed write as failed, and a caller such as <c>sync-schemas</c> seed-data would then replay a
	/// non-idempotent insert and duplicate rows. Only out-of-memory propagates, since nothing can continue then.
	/// </summary>
	private bool? ReadFileDesignModeState() {
		try {
			return fileDesignModeStateReader.GetIsFileDesignModeEnabled();
		} catch (Exception exception) when (exception is not OutOfMemoryException) {
			return null;
		}
	}

	/// <summary>Builds the warning text for a binding change the package folder on disk does not have.</summary>
	/// <param name="isFileDesignModeEnabled"><see langword="true"/> when enabled; <see langword="null"/> when unknown.</param>
	/// <param name="packageName">Package that owns the binding.</param>
	/// <param name="bindingName">The binding folder name.</param>
	/// <param name="change">What happened to the binding in the database.</param>
	internal static string BuildPackageFolderWarning(
		bool? isFileDesignModeEnabled, string packageName, string bindingName, BindingChange change) {
		string folder = $"Pkg/{packageName}/Data/{bindingName}";
		string verb = change switch {
			BindingChange.Created => "created in",
			BindingChange.Refreshed => "re-saved in",
			_ => "removed from"
		};
		string remedy =
			$"To write the database state to disk, run {FileSystemExportCommandName} for this environment " +
			$"(CLI: clio {FileSystemExportCommandName} -e <environment>; MCP: clio-run with command " +
			$"{FileSystemExportCommandName}). It rewrites " +
			"every package folder on disk from the database, except client modules and C# source code, and " +
			"removes on-disk items the database does not have, so commit or copy on-disk work that is not in " +
			$"the database yet before running it. Run it before the next {FileSystemImportCommandName}, which " +
			"makes the database match the files on disk.";
		if (isFileDesignModeEnabled is null) {
			return $"Data binding '{bindingName}' of package '{packageName}' was {verb} the database, but clio " +
				"could not read whether file system development mode is enabled on this environment. If it is, " +
				$"the package folder on disk ({folder}) was not updated. {remedy}";
		}
		string consequence = change switch {
			BindingChange.Created => "a commit of that package ships it without this binding",
			BindingChange.Refreshed => "a commit of that package carries the binding as the last export wrote it",
			_ => "a commit of that package still ships the removed binding"
		};
		return $"File system development mode is enabled: data binding '{bindingName}' of package " +
			$"'{packageName}' was {verb} the database only. The package folder on disk ({folder}) was not " +
			$"updated by this change, so {consequence}. {remedy}";
	}

	/// <summary>What happened to a binding in the database.</summary>
	internal enum BindingChange {
		/// <summary>A new binding registration was saved.</summary>
		Created,

		/// <summary>An existing binding registration was saved again, with new rows or columns or unchanged.</summary>
		Refreshed,

		/// <summary>The binding registration was deleted.</summary>
		Removed
	}

	/// <inheritdoc />
	public bool RowExists(string schemaName, Guid rowId) {
		EntityRowIdResponse response = SelectQueryHelper.ExecuteSelectQuery<EntityRowIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				schemaName,
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(
						"Id", rowId.ToString(), SelectQueryHelper.GuidDataValueType)
				]));
		return response.Rows.Any(row => !string.IsNullOrWhiteSpace(row.Id));
	}

	/// <summary>Builds the SaveSchema payload for a package data binding.</summary>
	internal static string BuildSaveSchemaDataRequest(
		PackageRef packageRef,
		string bindingName,
		string entitySchemaName,
		DataBindingDbSchema schema,
		List<string> boundRecordIds,
		Guid? existingBindingUId = null,
		DataBindingColumnPolicy columnPolicy = null) {
		string schemaDataUId = (existingBindingUId ?? Guid.NewGuid()).ToString();

		HashSet<string> keyColumns = null;
		HashSet<string> forceUpdateColumns = null;
		if (columnPolicy is not null) {
			keyColumns = new HashSet<string>(columnPolicy.KeyColumns, StringComparer.OrdinalIgnoreCase);
			forceUpdateColumns = new HashSet<string>(columnPolicy.ForceUpdateColumns, StringComparer.OrdinalIgnoreCase);
			ValidateColumnPolicy(schema, keyColumns, forceUpdateColumns);
		}

		var columnsArray = schema.SchemaColumns.Select(col => new {
			id = Guid.NewGuid().ToString(),
			uId = col.UId.ToString(),
			isForceUpdate = forceUpdateColumns?.Contains(col.Name) ?? false,
			isKey = keyColumns is null
				? string.Equals(col.Name, "Id", StringComparison.OrdinalIgnoreCase)
				: keyColumns.Contains(col.Name),
			name = col.Name,
			caption = col.Name,
			dataValueTypeUId = ResolveBindingDataTypeValueUId(col).ToString()
		}).ToArray();
		if (!columnsArray.Any(column => column.isKey)) {
			throw new InvalidOperationException(
				$"Binding '{bindingName}' would deliver schema '{entitySchemaName}' with no key column, so the " +
				"install target would match every row of the entity instead of the delivered one. Include the Id " +
				"column in the delivered set, or supply a column policy naming the natural key.");
		}

		var payload = new {
			uId = schemaDataUId,
			name = bindingName,
			package = new {
				uId = packageRef.UId.ToString(),
				name = packageRef.Name
			},
			entitySchemaUId = schema.EntitySchemaUId.ToString(),
			entitySchemaName,
			installType = 0,
			columns = columnsArray,
			boundRecordIds = boundRecordIds.ToArray()
		};

		return JsonSerializer.Serialize(payload);
	}

	/// <summary>Resolves a runtime column's data-value-type identifier for the binding payload.</summary>
	internal static Guid ResolveBindingDataTypeValueUId(DataBindingSchemaColumn column) {
		try {
			return DataValueTypeMap.FromRuntimeValueType(column.DataValueType);
		}
		catch (InvalidOperationException exception) {
			throw new InvalidOperationException(
				$"Column '{column.Name}' uses unsupported runtime dataValueType '{column.DataValueType}' for DB-first binding generation.",
				exception);
		}
	}

	private static void ValidateColumnPolicy(
		DataBindingDbSchema schema,
		HashSet<string> keyColumns,
		HashSet<string> forceUpdateColumns) {
		if (keyColumns.Count == 0) {
			throw new InvalidOperationException("A data-binding column policy must declare at least one key column.");
		}

		HashSet<string> schemaColumnNames = schema.SchemaColumns
			.Select(col => col.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		List<string> missingColumns = keyColumns.Concat(forceUpdateColumns)
			.Where(name => !schemaColumnNames.Contains(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (missingColumns.Count > 0) {
			throw new InvalidOperationException(
				$"Data-binding column policy references columns not present in schema '{schema.SchemaName}': " +
				$"{string.Join(", ", missingColumns)}.");
		}

		List<string> keyedAndForced = keyColumns
			.Where(forceUpdateColumns.Contains)
			.ToList();
		if (keyedAndForced.Count > 0) {
			throw new InvalidOperationException(
				"A data-binding key column cannot also be force-updated: " +
				$"{string.Join(", ", keyedAndForced)}.");
		}
	}

	private sealed class PackageSchemaDataResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<PackageSchemaDataDto> Rows { get; init; } = [];
	}

	private sealed class PackageSchemaDataDto {
		[JsonPropertyName("UId")]
		public string UId { get; init; }

		[JsonPropertyName("EntitySchemaName")]
		public string EntitySchemaName { get; init; }
	}

	private sealed class EntityRowIdResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<EntityRowIdDto> Rows { get; init; } = [];
	}

	private sealed class EntityRowIdDto {
		[JsonPropertyName("Id")]
		public string Id { get; init; }
	}
}
