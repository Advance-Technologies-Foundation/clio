using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.EntitySchema;
using Clio.Package;
using Terrasoft.Core.Entities;

namespace Clio.Command.EntitySchemaDesigner;

public interface IRemoteEntitySchemaColumnManager
{
	/// <summary>
	/// Applies one or more column mutations to the same remote schema and persists the result once.
	/// </summary>
	/// <param name="options">Ordered mutation list that targets the same package, schema, and environment.</param>
	void ModifyColumns(IEnumerable<ModifyEntitySchemaColumnOptions> options);

	/// <summary>
	/// Returns a structured snapshot of schema properties for the requested remote entity schema.
	/// </summary>
	/// <param name="options">Options that identify the package, schema, and remote environment.</param>
	/// <returns>Structured schema properties for MCP and CLI formatting.</returns>
	EntitySchemaPropertiesInfo GetSchemaProperties(GetEntitySchemaPropertiesOptions options);

	/// <summary>
	/// Returns a structured snapshot of column properties for the requested remote entity schema column.
	/// </summary>
	/// <param name="options">Options that identify the package, schema, column, and remote environment.</param>
	/// <returns>Structured column properties for MCP and CLI formatting.</returns>
	EntitySchemaColumnPropertiesInfo GetColumnProperties(GetEntitySchemaColumnPropertiesOptions options);

	/// <summary>
	/// Applies a single column mutation to a remote schema.
	/// </summary>
	/// <param name="options">Mutation details for the target package, schema, and column.</param>
	void ModifyColumn(ModifyEntitySchemaColumnOptions options);

	/// <summary>
	/// Sets schema-level properties (v1: the primary-display column) on a remote entity schema and persists
	/// the result through the shared save/publish pipeline, verifying that the change was applied.
	/// </summary>
	/// <param name="options">Options identifying the package, schema, environment, and the properties to set.</param>
	void SetSchemaProperties(SetEntitySchemaPropertiesOptions options);

	/// <summary>
	/// Prints column properties in CLI-friendly text form.
	/// </summary>
	/// <param name="options">Options that identify the package, schema, column, and remote environment.</param>
	void PrintColumnProperties(GetEntitySchemaColumnPropertiesOptions options);

	/// <summary>
	/// Prints schema properties in CLI-friendly text form.
	/// </summary>
	/// <param name="options">Options that identify the package, schema, and remote environment.</param>
	void PrintSchemaProperties(GetEntitySchemaPropertiesOptions options);
}

internal sealed class RemoteEntitySchemaColumnManager : IRemoteEntitySchemaColumnManager
{
	/// <summary>
	/// Synthetic package label reported by the merged (all-packages) schema read when no package is supplied.
	/// </summary>
	internal const string MergedSchemaPackageName = "(merged: all packages)";

	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IEntitySchemaDefaultValueSourceResolver _defaultValueSourceResolver;
	private readonly IRemoteEntitySchemaDesignerClient _entitySchemaDesignerClient;
	private readonly IRuntimeEntitySchemaReader _runtimeEntitySchemaReader;
	private readonly ILookupDefaultDisplayValueResolver _lookupDefaultDisplayValueResolver;
	private readonly IEntitySchemaCaptionCultureResolver _captionCultureResolver;
	private readonly IEntitySchemaDependencyResolver _dependencyResolver;
	private readonly ILogger _logger;

	public RemoteEntitySchemaColumnManager(IApplicationPackageListProvider applicationPackageListProvider,
		EntitySchemaColumnResolvers columnResolvers,
		IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient,
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader,
		IEntitySchemaDependencyResolver dependencyResolver,
		ILogger logger) {
		_applicationPackageListProvider = applicationPackageListProvider;
		_defaultValueSourceResolver = columnResolvers.DefaultValueSourceResolver;
		_entitySchemaDesignerClient = entitySchemaDesignerClient;
		_runtimeEntitySchemaReader = runtimeEntitySchemaReader;
		_lookupDefaultDisplayValueResolver = columnResolvers.LookupDisplayValueResolver;
		_captionCultureResolver = columnResolvers.CaptionCultureResolver;
		_dependencyResolver = dependencyResolver;
		_logger = logger;
	}

	/// <summary>
	/// Resolves the effective caption culture for a column WRITE batch via
	/// <see cref="IEntitySchemaCaptionCultureResolver"/>: an explicit <c>--caption-culture</c> override
	/// wins; otherwise the connected user's profile culture; otherwise the <c>en-US</c> fallback.
	/// </summary>
	private string ResolveEffectiveCultureName(ModifyEntitySchemaColumnOptions options) {
		return _captionCultureResolver.ResolveEffectiveCulture(options, options.CaptionCulture);
	}

	public void ModifyColumn(ModifyEntitySchemaColumnOptions options) {
		ModifyColumns([options]);
	}

	public void ModifyColumns(IEnumerable<ModifyEntitySchemaColumnOptions> options) {
		ArgumentNullException.ThrowIfNull(options);
		List<ModifyEntitySchemaColumnOptions> operations = options.ToList();
		if (operations.Count == 0) {
			throw new EntitySchemaDesignerException("At least one column mutation is required.");
		}

		ModifyEntitySchemaColumnOptions rootOperation = operations[0];
		PackageInfo package = ResolvePackage(rootOperation.Package);
		EntityDesignSchemaDto schema = LoadSchema(rootOperation.SchemaName, package.Descriptor.UId, package.Descriptor.Name, rootOperation, allowDependencyResolution: true);
		EnsureBatchTargetsSingleSchema(operations, rootOperation);
		string effectiveCultureName = ResolveEffectiveCultureName(rootOperation);
		foreach (ModifyEntitySchemaColumnOptions operation in operations) {
			ApplyColumnMutation(schema, package, operation, effectiveCultureName);
		}

		EntityDesignSchemaDto reloadedSchema = SaveAndReloadSchema(
			schema, package, rootOperation, "columns were saved");
		VerifyColumnMutations(reloadedSchema, operations, effectiveCultureName);
		foreach (ModifyEntitySchemaColumnOptions operation in operations) {
			_logger.WriteInfo(
				$"Column '{operation.ColumnName}' action '{operation.Action}' completed for schema '{operation.SchemaName}'.");
		}
	}

	/// <summary>
	/// Persists an in-memory design schema through the full designer pipeline and returns the reloaded schema.
	/// Runs <c>SaveSchema</c> → <c>SaveSchemaDbStructure</c> → publish + OData rebuild → runtime availability
	/// check, then reloads the design item so callers can verify the persisted result. Shared by the column
	/// mutation path (<see cref="ModifyColumns"/>) and the schema-property setter
	/// (<see cref="SetSchemaProperties"/>) so both write paths persist and publish identically.
	/// </summary>
	/// <param name="schema">The mutated design schema to save.</param>
	/// <param name="package">The package that owns the schema (used to reload the design item).</param>
	/// <param name="options">Remote command options identifying the target environment.</param>
	/// <param name="publishReason">Human-readable reason appended to publish progress messages.</param>
	/// <returns>The design schema reloaded after a successful save and publish.</returns>
	private EntityDesignSchemaDto SaveAndReloadSchema(EntityDesignSchemaDto schema, PackageInfo package,
		RemoteCommandOptions options, string publishReason) {
		SaveDesignItemDesignerResponse saveResponse = _entitySchemaDesignerClient.SaveSchema(schema, options);
		Guid schemaUId = saveResponse.SchemaUId != Guid.Empty ? saveResponse.SchemaUId : schema.UId;
		if (schemaUId == Guid.Empty) {
			throw new EntitySchemaDesignerException(
				$"Schema '{schema.Name}' was saved but schema UId is unavailable.");
		}
		_entitySchemaDesignerClient.SaveSchemaDbStructure(schemaUId, options);
		EntitySchemaPublishHelper.PublishAndRebuildOData(
			_entitySchemaDesignerClient, _logger, options, schema.Name, publishReason);
		RuntimeEntitySchemaResponse runtimeResponse = _entitySchemaDesignerClient.GetRuntimeEntitySchema(schemaUId,
			options);
		if (!runtimeResponse.Success || runtimeResponse.Schema == null) {
			throw new EntitySchemaDesignerException(
				$"Schema '{schema.Name}' was saved but is not available in runtime.");
		}
		return LoadSchema(schema.Name, package.Descriptor.UId, package.Descriptor.Name, options,
			allowDependencyResolution: true);
	}

	public void SetSchemaProperties(SetEntitySchemaPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.PrimaryDisplayColumn)) {
			throw new EntitySchemaDesignerException(SetEntitySchemaPropertiesOptions.NoPropertyToSetError);
		}
		PackageInfo package = ResolvePackage(options.Package);
		EntityDesignSchemaDto schema = LoadSchema(options.SchemaName, package.Descriptor.UId,
			package.Descriptor.Name, options, allowDependencyResolution: true);
		string requestedColumnName = options.PrimaryDisplayColumn.Trim();
		// Resolve by name against own then inherited columns (modern server contract: the primary-display
		// column is matched by the column's uId object, NOT a legacy flat primaryDisplayColumnUId).
		(EntitySchemaColumnDto targetColumn, _) = FindColumnForRead(schema, requestedColumnName);
		schema.PrimaryDisplayColumn = targetColumn;

		EntityDesignSchemaDto reloadedSchema = SaveAndReloadSchema(
			schema, package, options, "schema properties were saved");
		// The server performs no validation and silently no-ops if a target version expects the legacy
		// primaryDisplayColumnUId; verify the readback so that silent no-op becomes a clear failure.
		if (!string.Equals(reloadedSchema.PrimaryDisplayColumn?.Name, requestedColumnName,
			StringComparison.OrdinalIgnoreCase)) {
			throw new EntitySchemaDesignerException(
				$"Primary-display column '{requestedColumnName}' was not persisted for schema '{schema.Name}'. " +
				"The target environment may not support setting the primary-display column through this API.");
		}
		_logger.WriteInfo(
			$"Primary-display column set to '{requestedColumnName}' for schema '{options.SchemaName}'.");
	}

	public EntitySchemaColumnPropertiesInfo GetColumnProperties(GetEntitySchemaColumnPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		PackageInfo package = ResolvePackage(options.Package);
		EntityDesignSchemaDto schema = LoadSchema(options.SchemaName, package.Descriptor.UId, package.Descriptor.Name, options, allowDependencyResolution: false);
		(EntitySchemaColumnDto column, string source) = FindColumnForRead(schema, options.ColumnName);
		string cultureName = EntitySchemaDesignerSupport.GetCurrentCultureName();
		EntitySchemaDefaultValueConfig? defaultValueConfig = EntitySchemaDesignerSupport.CreateDefaultValueConfig(
			column.DefValue);
		defaultValueConfig = EnrichLookupConstDefaultValue(defaultValueConfig, column, options);
		return new EntitySchemaColumnPropertiesInfo(
			schema.Name,
			schema.Package?.Name ?? options.Package,
			column.Name,
			source,
			EntitySchemaDesignerSupport.GetLocalizableValue(column.Caption, cultureName),
			EntitySchemaDesignerSupport.GetLocalizableValue(column.Description, cultureName),
			EntitySchemaDesignerSupport.GetFriendlyTypeName(column.DataValueType),
			IsRequired(column.RequirementType),
			column.Indexed,
			column.IsValueCloneable,
			column.IsTrackChangesInDB,
			EntitySchemaDesignerSupport.CreateDefaultValueConfig(column.DefValue)?.Source,
			EntitySchemaDesignerSupport.GetFriendlyDefaultValue(column.DefValue),
			column.ReferenceSchema?.Name,
			column.List,
			column.CascadeConnection,
			column.DoNotControlIntegrity,
			column.MultiLineText,
			column.LocalizableText,
			column.AccentInsensitive,
			column.ValueMasked || column.Masked,
			column.FormatValidated,
			column.UseSeconds,
			defaultValueConfig,
			EntitySchemaDesignerSupport.GetFriendlyUsageType(column.UsageType));
	}

	/// <summary>
	/// Enriches a lookup <c>Const</c> default's GUID-only readback with the referenced record's display
	/// value (or an honest record-resolution marker). No-op for any other default source, non-lookup
	/// columns, or a missing/empty GUID. Fail-soft: returns the original config unchanged when enrichment
	/// yields nothing, so the readback never regresses versus the GUID-only behavior it augments.
	/// </summary>
	private EntitySchemaDefaultValueConfig? EnrichLookupConstDefaultValue(
		EntitySchemaDefaultValueConfig? config,
		EntitySchemaColumnDto column,
		GetEntitySchemaColumnPropertiesOptions options) {
		if (config is null
			|| column.DefValue?.ValueSourceType != EntitySchemaColumnDefSource.Const
			|| string.IsNullOrWhiteSpace(column.ReferenceSchema?.Name)
			|| !Guid.TryParse(config.Value?.ToString(), out Guid recordId)
			|| recordId == Guid.Empty) {
			return config;
		}
		LookupDefaultResolution resolution = _lookupDefaultDisplayValueResolver.Resolve(
			column.ReferenceSchema!.Name, recordId, options);
		if (resolution.DisplayValue is null && resolution.RecordResolution is null) {
			return config;
		}
		return config.WithDisplay(resolution.DisplayValue, resolution.RecordResolution);
	}

	public void PrintColumnProperties(GetEntitySchemaColumnPropertiesOptions options) {
		EntitySchemaColumnPropertiesInfo column = GetColumnProperties(options);
		WriteInfo("Entity schema column properties");
		WriteInfo($"Schema: {column.SchemaName}");
		WriteInfo($"Package: {column.PackageName}");
		WriteInfo($"Column: {column.ColumnName}");
		WriteInfo($"Source: {column.Source}");
		WriteInfo($"Title: {FormatText(column.Title)}");
		WriteInfo($"Description: {FormatText(column.Description)}");
		WriteInfo($"Type: {column.Type}");
		WriteInfo($"Required: {FormatBoolean(column.Required)}");
		WriteInfo($"Indexed: {FormatBoolean(column.Indexed)}");
		WriteInfo($"Cloneable: {FormatBoolean(column.Cloneable)}");
		WriteInfo($"Track changes: {FormatBoolean(column.TrackChanges)}");
		WriteInfo($"Default value source: {FormatText(column.DefaultValueSource)}");
		WriteInfo($"Default value: {FormatText(column.DefaultValue)}");
		WriteInfo($"Reference schema: {FormatText(column.ReferenceSchemaName)}");
		if (column.DefaultValueConfig?.DisplayValue != null) {
			WriteInfo($"Default value display: {column.DefaultValueConfig.DisplayValue}");
		}
		if (column.DefaultValueConfig?.RecordResolution != null) {
			WriteInfo($"Default value record resolution: {column.DefaultValueConfig.RecordResolution}");
		}
		WriteInfo($"Simple lookup: {FormatBoolean(column.SimpleLookup)}");
		WriteInfo($"Cascade: {FormatBoolean(column.Cascade)}");
		WriteInfo($"Do not control integrity: {FormatBoolean(column.DoNotControlIntegrity)}");
		WriteInfo($"Multiline text: {FormatBoolean(column.MultilineText)}");
		WriteInfo($"Localizable text: {FormatBoolean(column.LocalizableText)}");
		WriteInfo($"Accent insensitive: {FormatBoolean(column.AccentInsensitive)}");
		WriteInfo($"Masked: {FormatBoolean(column.Masked)}");
		WriteInfo($"Format validated: {FormatBoolean(column.FormatValidated)}");
		WriteInfo($"Use seconds: {FormatBoolean(column.UseSeconds)}");
		WriteInfo($"Usage type: {column.UsageType}");
	}

	public EntitySchemaPropertiesInfo GetSchemaProperties(GetEntitySchemaPropertiesOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.Package)) {
			return GetMergedSchemaProperties(options);
		}
		PackageInfo package = ResolvePackage(options.Package);
		EntityDesignSchemaDto schema = LoadSchema(options.SchemaName, package.Descriptor.UId, package.Descriptor.Name, options, allowDependencyResolution: false);
		string cultureName = EntitySchemaDesignerSupport.GetCurrentCultureName();
		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		List<EntitySchemaColumnDto> inheritedColumns = schema.InheritedColumns?.ToList() ?? [];
		List<EntitySchemaPropertyColumnInfo> columns = ownColumns
			.Select(column => MapSchemaPropertyColumn(column, "own", cultureName))
			.Concat(inheritedColumns.Select(column => MapSchemaPropertyColumn(column, "inherited", cultureName)))
			.ToList();
		return new EntitySchemaPropertiesInfo(
			schema.Name,
			EntitySchemaDesignerSupport.GetLocalizableValue(schema.Caption, cultureName),
			EntitySchemaDesignerSupport.GetLocalizableValue(schema.Description, cultureName),
			schema.Package?.Name ?? package.Descriptor.Name,
			schema.ParentSchema?.Name,
			schema.ExtendParent,
			schema.PrimaryColumn?.Name,
			schema.PrimaryDisplayColumn?.Name,
			ownColumns.Count,
			inheritedColumns.Count,
			schema.Indexes?.Count() ?? 0,
			schema.IsTrackChangesInDB,
			schema.IsDBView,
			schema.IsSSPAvailable,
			schema.IsVirtual,
			schema.UseRecordDeactivation,
			schema.ShowInAdvancedMode,
			schema.AdministratedByOperations,
			schema.AdministratedByColumns,
			schema.AdministratedByRecords,
			schema.UseDenyRecordRights,
			schema.UseLiveEditing,
			columns);
	}

	/// <summary>
	/// Builds the effective (merged) schema properties snapshot that unions columns from every package layer,
	/// including customizations contributed by packages other than the one that originally defines the schema.
	/// </summary>
	/// <param name="options">Options that identify the schema and remote environment. <c>Package</c> is ignored here.</param>
	/// <returns>
	/// Structured schema properties whose <c>columns</c> reflect the full runtime column set. Most schema-level
	/// metadata (title, description, extend-parent, db-view, track-changes, virtual, show-in-advanced-mode and the
	/// administration flags) and the per-column <c>indexed</c> flag are mapped from the runtime payload. A few
	/// fields are not exposed by the by-name runtime endpoint and are therefore reported as <c>null</c> in this
	/// mode so a caller can distinguish "unavailable" from a genuine value: <c>parent-schema-name</c> (only the
	/// parent UId is available), <c>indexes-count</c>, <c>ssp-available</c>, <c>use-record-deactivation</c>,
	/// <c>use-deny-record-rights</c> and <c>use-live-editing</c>. Supply a package to read those authoritative
	/// schema-level values from a single package layer.
	/// Note that <c>own-column-count</c>/<c>inherited-column-count</c> and each column's <c>source</c> are derived
	/// here from runtime parent-entity-schema inheritance (the <c>IsInherited</c> flag), whereas the single-package
	/// path splits on package-layer ownership. The two splits are NOT comparable across modes.
	/// </returns>
	private EntitySchemaPropertiesInfo GetMergedSchemaProperties(GetEntitySchemaPropertiesOptions options) {
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			throw new EntitySchemaDesignerException("Schema name is required.");
		}
		RuntimeEntitySchemaResult runtimeSchema = ReadMergedRuntimeSchema(options.SchemaName.Trim());
		List<EntitySchemaPropertyColumnInfo> columns = runtimeSchema.Columns
			.Select(MapRuntimePropertyColumn)
			.ToList();
		int inheritedColumnCount = runtimeSchema.Columns.Count(column => column.IsInherited);
		int ownColumnCount = runtimeSchema.Columns.Count - inheritedColumnCount;
		string? primaryColumnName = runtimeSchema.Columns
			.FirstOrDefault(column => column.UId == runtimeSchema.PrimaryColumnUId)?.Name;
		return new EntitySchemaPropertiesInfo(
			runtimeSchema.Name,
			Title: runtimeSchema.Caption,
			Description: runtimeSchema.Description,
			PackageName: MergedSchemaPackageName,
			ParentSchemaName: null,
			ExtendParent: runtimeSchema.ExtendParent,
			PrimaryColumnName: primaryColumnName,
			PrimaryDisplayColumnName: runtimeSchema.PrimaryDisplayColumnName,
			OwnColumnCount: ownColumnCount,
			InheritedColumnCount: inheritedColumnCount,
			// The by-name runtime endpoint does not expose these fields; emit null (not a default) so a machine
			// consumer can distinguish "unavailable in merged mode" from a genuine value. Supply a package to read them.
			IndexesCount: null,
			TrackChangesInDb: runtimeSchema.IsTrackChangesInDB,
			DbView: runtimeSchema.IsDBView,
			SspAvailable: null,
			Virtual: runtimeSchema.IsVirtual,
			UseRecordDeactivation: null,
			ShowInAdvancedMode: runtimeSchema.ShowInAdvancedMode,
			AdministratedByOperations: runtimeSchema.AdministratedByOperations,
			AdministratedByColumns: runtimeSchema.AdministratedByColumns,
			AdministratedByRecords: runtimeSchema.AdministratedByRecords,
			UseDenyRecordRights: null,
			UseLiveEditing: null,
			Columns: columns);
	}

	/// <summary>
	/// Reads the runtime schema for the merged view, translating low-level reader failures into the domain
	/// <see cref="EntitySchemaDesignerException"/> so both read paths surface a uniform exception type.
	/// </summary>
	/// <remarks>
	/// The reader reaches <see cref="IApplicationClient"/> and <c>JsonSerializer</c>, so beyond the
	/// <see cref="InvalidOperationException"/> it raises for an unsuccessful/HTML response it can surface transport
	/// and parse faults (<see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/>,
	/// <see cref="JsonException"/>). The MCP <c>get-entity-schema-properties</c> tool calls this path directly
	/// without the <c>BaseTool</c> catch-all, so all realistic failure types are normalized here.
	/// </remarks>
	private RuntimeEntitySchemaResult ReadMergedRuntimeSchema(string schemaName) {
		try {
			return _runtimeEntitySchemaReader.GetByName(schemaName);
		} catch (Exception exception) when (exception is InvalidOperationException
			or HttpRequestException
			or JsonException
			or TaskCanceledException) {
			throw new EntitySchemaDesignerException(exception.Message, exception);
		}
	}

	private static EntitySchemaPropertyColumnInfo MapRuntimePropertyColumn(RuntimeEntitySchemaColumnResult column) {
		return new EntitySchemaPropertyColumnInfo(
			column.Name,
			column.UId,
			column.IsInherited ? "inherited" : "own",
			column.Caption,
			column.Description,
			EntitySchemaDesignerSupport.GetFriendlyTypeName(column.DataValueType),
			column.IsRequired,
			column.IsIndexed,
			column.ReferenceSchemaName);
	}

	public void PrintSchemaProperties(GetEntitySchemaPropertiesOptions options) {
		EntitySchemaPropertiesInfo schema = GetSchemaProperties(options);
		WriteInfo("Entity schema properties");
		WriteInfo($"Name: {schema.Name}");
		WriteInfo($"Title: {FormatText(schema.Title)}");
		WriteInfo($"Description: {FormatText(schema.Description)}");
		WriteInfo($"Package: {schema.PackageName}");
		WriteInfo($"Parent schema: {FormatText(schema.ParentSchemaName)}");
		WriteInfo($"Extend parent: {FormatBoolean(schema.ExtendParent)}");
		WriteInfo($"Primary column: {FormatText(schema.PrimaryColumnName)}");
		WriteInfo($"Primary display column: {FormatText(schema.PrimaryDisplayColumnName)}");
		WriteInfo($"Own columns: {schema.OwnColumnCount}");
		WriteInfo($"Inherited columns: {schema.InheritedColumnCount}");
		WriteInfo($"Indexes: {FormatNullableCount(schema.IndexesCount)}");
		WriteInfo($"Track changes in DB: {FormatBoolean(schema.TrackChangesInDb)}");
		WriteInfo($"DB view: {FormatBoolean(schema.DbView)}");
		WriteInfo($"SSP available: {FormatBoolean(schema.SspAvailable)}");
		WriteInfo($"Virtual: {FormatBoolean(schema.Virtual)}");
		WriteInfo($"Use record deactivation: {FormatBoolean(schema.UseRecordDeactivation)}");
		WriteInfo($"Show in advanced mode: {FormatBoolean(schema.ShowInAdvancedMode)}");
		WriteInfo($"Administrated by operations: {FormatBoolean(schema.AdministratedByOperations)}");
		WriteInfo($"Administrated by columns: {FormatBoolean(schema.AdministratedByColumns)}");
		WriteInfo($"Administrated by records: {FormatBoolean(schema.AdministratedByRecords)}");
		WriteInfo($"Use deny record rights: {FormatBoolean(schema.UseDenyRecordRights)}");
		WriteInfo($"Use live editing: {FormatBoolean(schema.UseLiveEditing)}");
	}

	private void AddColumn(EntityDesignSchemaDto schema, PackageInfo package, ModifyEntitySchemaColumnOptions options,
		string effectiveCultureName) {
		EnsureNameIsUnique(schema, options.ColumnName, null);
		int dataValueType = ParseSupportedType(options.Type, "add");
		ValidateOptionsForType(options, dataValueType, isAdd: true);
		TitleLocalizationNormalizationResult titleNormalization =
			EntitySchemaDesignerSupport.NormalizeTitleLocalizations(
				options.TitleLocalizations,
				ResolveEffectiveTitle(options.Title, options.ColumnName),
				"title-localizations",
				effectiveCultureName);
		IReadOnlyDictionary<string, string>? descriptionLocalizations = options.DescriptionLocalizations == null
			? null
			: EntitySchemaDesignerSupport.NormalizeLocalizationMap(
				options.DescriptionLocalizations,
				"description-localizations");
		EntitySchemaColumnDto column = new() {
			UId = Guid.NewGuid(),
			Name = options.ColumnName,
			DataValueType = dataValueType,
			Caption = EntitySchemaDesignerSupport.CreateLocalizableStrings(
				titleNormalization.Localizations,
				titleNormalization.EffectiveTitle,
				effectiveCultureName),
			Description = EntitySchemaDesignerSupport.CreateLocalizableStrings(
				descriptionLocalizations,
				options.Description,
				effectiveCultureName),
			RequirementType = MapRequirementType(options.Required),
			Indexed = options.Indexed ?? false,
			IsValueCloneable = options.Cloneable ?? false,
			IsTrackChangesInDB = options.TrackChanges ?? false,
			MultiLineText = options.MultilineText ?? false,
			LocalizableText = options.LocalizableText ?? false,
			AccentInsensitive = options.AccentInsensitive ?? false,
			Masked = options.Masked ?? false,
			ValueMasked = options.Masked ?? false,
			FormatValidated = options.FormatValidated ?? false,
			UseSeconds = options.UseSeconds ?? false,
			List = options.SimpleLookup ?? false,
			CascadeConnection = options.Cascade ?? false,
			DoNotControlIntegrity = options.DoNotControlIntegrity ?? false
		};
		// Resolve the reference schema BEFORE applying the default. A lookup Const default carries a
		// record GUID that must be validated against the referenced schema (record-existence check in
		// EntitySchemaDefaultValueSourceResolver.ResolveConst); that validation is skipped when the
		// reference schema name is unknown, so it has to be set on the column first. (Previously
		// ApplyDefaultValue ran while column.ReferenceSchema was still null, so an add of a lookup
		// column with a Const default pointing at a missing record was silently accepted.)
		if (dataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["lookup"]) {
			ManagerItemDto referenceSchema = ResolveReferenceSchema(package.Descriptor.UId, options.ReferenceSchemaName,
				options);
			column.ReferenceSchema = CreateReferenceSchema(referenceSchema);
		} else if (EntitySchemaDesignerSupport.IsImageLookupDataValueType(dataValueType)) {
			// ImageLookup ("Image link") is the reference type required by crt.ImageInput. It always points at the
			// platform SysImage schema and is indexed, mirroring the server-side EntitySchemaDesigner behavior.
			column.ReferenceSchema = EntitySchemaDesignerSupport.CreateSysImageReferenceSchema();
			column.Indexed = true;
		}

		ApplyDefaultValue(column, options, preserveWhenUnspecified: false, options);
		ApplyUsageType(column, options);

		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		ownColumns.Add(column);
		schema.Columns = ownColumns;
		if (schema.PrimaryDisplayColumn == null && column.IsTextType()) {
			schema.PrimaryDisplayColumn = column;
		}
	}

	private void ModifyColumn(EntityDesignSchemaDto schema, PackageInfo package, ModifyEntitySchemaColumnOptions options,
		string effectiveCultureName) {
		(EntitySchemaColumnDto column, bool isInherited) = FindColumnForMutation(schema, options.ColumnName);
		if (isInherited) {
			// An inherited column is owned by the parent schema: only its caption/description may be overridden
			// on this (child/replacing) schema, applied in place on the InheritedColumns entry. Its uId, name,
			// and type stay unchanged, so the server persists a caption override keyed
			// '<Schema>.Columns.<Column>.Caption' without redefining the column or touching the parent.
			ApplyInheritedColumnCaptionOverride(column, options, effectiveCultureName);
			return;
		}
		int effectiveDataValueType = options.Type == null
			? column.DataValueType ?? 0
			: ParseSupportedType(options.Type, "modify");
		ValidateOptionsForType(options, effectiveDataValueType, isAdd: false);
		if (!string.IsNullOrWhiteSpace(options.NewName)) {
			EnsureNameIsUnique(schema, options.NewName, column.UId);
			column.Name = options.NewName.Trim();
		}

		if (options.Type != null) {
			column.DataValueType = effectiveDataValueType;
		}

		ApplyColumnCaptionAndDescription(column, options, effectiveCultureName);
		ApplyColumnScalarOptions(column, options);

		ApplyColumnTypeConfiguration(package, column, options, effectiveDataValueType);
	}

	/// <summary>
	/// Applies a caption/description-only override to an inherited column, in place on the schema's
	/// <c>InheritedColumns</c> entry. Rejects any attempt to change a non-caption property (name, type, or any
	/// flag) of an inherited column, and rejects a modify that carries no caption/description change at all.
	/// </summary>
	private static void ApplyInheritedColumnCaptionOverride(EntitySchemaColumnDto column,
		ModifyEntitySchemaColumnOptions options, string effectiveCultureName) {
		if (HasNonCaptionInheritedMutation(options)) {
			throw new EntitySchemaDesignerException(
				$"Column '{options.ColumnName}' is inherited; only its caption and description can be overridden. " +
				"Its name, type, and flags are read-only.");
		}
		if (!HasCaptionOrDescriptionChange(options)) {
			throw new EntitySchemaDesignerException(
				$"Column '{options.ColumnName}' is inherited; provide title-localizations (or a description) to override its caption.");
		}
		ApplyColumnCaptionAndDescription(column, options, effectiveCultureName);
	}

	/// <summary>Returns whether the modify request changes the column caption and/or description.</summary>
	private static bool HasCaptionOrDescriptionChange(ModifyEntitySchemaColumnOptions options) {
		return !string.IsNullOrWhiteSpace(options.Title)
			|| options.TitleLocalizations?.Count > 0
			|| !string.IsNullOrWhiteSpace(options.Description)
			|| options.DescriptionLocalizations?.Count > 0;
	}

	/// <summary>
	/// Returns whether the modify request touches any property other than caption/description — the set that is
	/// read-only on an inherited column (name, type, reference, and every scalar/flag/default option).
	/// </summary>
	private static bool HasNonCaptionInheritedMutation(ModifyEntitySchemaColumnOptions options) {
		return !string.IsNullOrWhiteSpace(options.NewName)
			|| !string.IsNullOrWhiteSpace(options.Type)
			|| !string.IsNullOrWhiteSpace(options.ReferenceSchemaName)
			|| options.Required.HasValue
			|| options.Indexed.HasValue
			|| options.Cloneable.HasValue
			|| options.TrackChanges.HasValue
			|| !string.IsNullOrWhiteSpace(options.DefaultValueSource)
			|| options.DefaultValue != null
			|| options.DefaultValueConfig != null
			|| options.MultilineText.HasValue
			|| options.LocalizableText.HasValue
			|| options.AccentInsensitive.HasValue
			|| options.Masked.HasValue
			|| options.FormatValidated.HasValue
			|| options.UseSeconds.HasValue
			|| options.SimpleLookup.HasValue
			|| options.Cascade.HasValue
			|| options.DoNotControlIntegrity.HasValue
			|| !string.IsNullOrWhiteSpace(options.UsageType);
	}

	/// <summary>
	/// Writes the column caption and description using the effective culture (override &gt; profile &gt; en-US).
	/// </summary>
	private static void ApplyColumnCaptionAndDescription(EntitySchemaColumnDto column,
		ModifyEntitySchemaColumnOptions options, string effectiveCultureName) {
		List<LocalizableStringDto> caption = column.Caption?.ToList() ?? [];
		List<LocalizableStringDto> description = column.Description?.ToList() ?? [];
		if (options.TitleLocalizations != null) {
			TitleLocalizationNormalizationResult titleNormalization =
				EntitySchemaDesignerSupport.NormalizeTitleLocalizations(
					options.TitleLocalizations,
					options.Title,
					"title-localizations",
					effectiveCultureName);
			EntitySchemaDesignerSupport.ReplaceLocalizableValues(
				caption,
				titleNormalization.Localizations!);
		} else if (!string.IsNullOrWhiteSpace(options.Title)) {
			EntitySchemaDesignerSupport.SetLocalizableValue(caption, options.Title.Trim(), effectiveCultureName);
		}
		if (options.DescriptionLocalizations != null) {
			EntitySchemaDesignerSupport.ReplaceLocalizableValues(
				description,
				EntitySchemaDesignerSupport.NormalizeLocalizationMap(
					options.DescriptionLocalizations,
					"description-localizations")!);
		} else if (!string.IsNullOrWhiteSpace(options.Description)) {
			EntitySchemaDesignerSupport.SetLocalizableValue(description, options.Description, effectiveCultureName);
		}
		column.Caption = caption;
		column.Description = description;
	}

	/// <summary>
	/// Applies the optional scalar column flags and default value left unspecified-as-unchanged.
	/// </summary>
	private void ApplyColumnScalarOptions(EntitySchemaColumnDto column, ModifyEntitySchemaColumnOptions options) {
		if (options.Required.HasValue) {
			column.RequirementType = MapRequirementType(options.Required);
		}
		if (options.Indexed.HasValue) {
			column.Indexed = options.Indexed.Value;
		}
		if (options.Cloneable.HasValue) {
			column.IsValueCloneable = options.Cloneable.Value;
		}
		if (options.TrackChanges.HasValue) {
			column.IsTrackChangesInDB = options.TrackChanges.Value;
		}
		ApplyDefaultValue(column, options, preserveWhenUnspecified: true, options);
		if (options.MultilineText.HasValue) {
			column.MultiLineText = options.MultilineText.Value;
		}
		if (options.LocalizableText.HasValue) {
			column.LocalizableText = options.LocalizableText.Value;
		}
		if (options.AccentInsensitive.HasValue) {
			column.AccentInsensitive = options.AccentInsensitive.Value;
		}
		if (options.Masked.HasValue) {
			column.Masked = options.Masked.Value;
			column.ValueMasked = options.Masked.Value;
		}
		if (options.FormatValidated.HasValue) {
			column.FormatValidated = options.FormatValidated.Value;
		}
		if (options.UseSeconds.HasValue) {
			column.UseSeconds = options.UseSeconds.Value;
		}
		ApplyUsageType(column, options);
	}

	/// <summary>
	/// Applies the optional <c>--usage-type</c> value to the column when supplied, mapping the friendly name
	/// to its backend ordinal. When omitted the column's current UsageType is left unchanged. Throws a
	/// user-friendly <see cref="EntitySchemaDesignerException"/> on an unrecognized value, before any save.
	/// </summary>
	private static void ApplyUsageType(EntitySchemaColumnDto column, ModifyEntitySchemaColumnOptions options) {
		if (string.IsNullOrWhiteSpace(options.UsageType)) {
			return;
		}
		if (!EntitySchemaDesignerSupport.TryParseUsageType(options.UsageType, out int usageType)) {
			throw new EntitySchemaDesignerException("usage-type must be one of: General, Advanced, None.");
		}
		column.UsageType = usageType;
	}

	/// <summary>
	/// Applies lookup / image-lookup reference configuration for the column based on its data value type.
	/// </summary>
	private void ApplyColumnTypeConfiguration(PackageInfo package, EntitySchemaColumnDto column,
		ModifyEntitySchemaColumnOptions options, int effectiveDataValueType) {
		bool isLookupType = effectiveDataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["lookup"];
		bool isImageLookupType = EntitySchemaDesignerSupport.IsImageLookupDataValueType(effectiveDataValueType);
		if (isLookupType) {
			if (!string.IsNullOrWhiteSpace(options.ReferenceSchemaName)) {
				ManagerItemDto referenceSchema = ResolveReferenceSchema(package.Descriptor.UId, options.ReferenceSchemaName,
					options);
				column.ReferenceSchema = CreateReferenceSchema(referenceSchema);
			} else if (column.ReferenceSchema == null) {
				throw new EntitySchemaDesignerException(
					$"Lookup column '{options.ColumnName}' must specify --reference-schema.");
			}
			if (options.SimpleLookup.HasValue) {
				column.List = options.SimpleLookup.Value;
			}
			if (options.Cascade.HasValue) {
				column.CascadeConnection = options.Cascade.Value;
			}
			if (options.DoNotControlIntegrity.HasValue) {
				column.DoNotControlIntegrity = options.DoNotControlIntegrity.Value;
			}
		} else if (isImageLookupType) {
			// ImageLookup ("Image link") always references SysImage and is indexed; never a simple lookup.
			column.ReferenceSchema = EntitySchemaDesignerSupport.CreateSysImageReferenceSchema();
			column.Indexed = true;
			column.List = false;
			column.CascadeConnection = false;
			column.DoNotControlIntegrity = false;
		} else {
			column.ReferenceSchema = null;
			column.List = false;
			column.CascadeConnection = false;
			column.DoNotControlIntegrity = false;
		}
	}

	private void RemoveColumn(EntityDesignSchemaDto schema, string columnName) {
		(EntitySchemaColumnDto column, bool isInherited) = FindColumnForMutation(schema, columnName);
		if (isInherited) {
			throw new EntitySchemaDesignerException(
				$"Column '{columnName}' is inherited and cannot be removed.");
		}
		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		ownColumns.RemoveAll(item => item.UId == column.UId);
		schema.Columns = ownColumns;
		List<EntitySchemaColumnDto> remainingColumns = GetAllColumns(schema);
		schema.PrimaryColumn = ReplaceReferencedColumn(schema.PrimaryColumn, column,
			remainingColumns.FirstOrDefault(item => item.IsGuidType()), requiredReferenceName: "Primary column");
		schema.PrimaryDisplayColumn = ReplaceReferencedColumn(schema.PrimaryDisplayColumn, column,
			remainingColumns.FirstOrDefault(item => item.IsTextType()));
		schema.PrimaryImageColumn = ReplaceReferencedColumn(schema.PrimaryImageColumn, column, null);
		schema.PrimaryColorColumn = ReplaceReferencedColumn(schema.PrimaryColorColumn, column, null);
		schema.PrimaryOrderColumn = ReplaceReferencedColumn(schema.PrimaryOrderColumn, column, null);
		schema.HierarchyColumn = ReplaceReferencedColumn(schema.HierarchyColumn, column, null);
		schema.OwnerColumn = ReplaceReferencedColumn(schema.OwnerColumn, column, null);
		schema.MasterRecordColumn = ReplaceReferencedColumn(schema.MasterRecordColumn, column, null);
		schema.CreatedByColumn = ReplaceReferencedColumn(schema.CreatedByColumn, column, null);
		schema.CreatedOnColumn = ReplaceReferencedColumn(schema.CreatedOnColumn, column, null);
		schema.ModifiedByColumn = ReplaceReferencedColumn(schema.ModifiedByColumn, column, null);
		schema.ModifiedOnColumn = ReplaceReferencedColumn(schema.ModifiedOnColumn, column, null);
	}

	private EntitySchemaColumnDto ReplaceReferencedColumn(EntitySchemaColumnDto currentReference,
		EntitySchemaColumnDto removedColumn, EntitySchemaColumnDto fallbackColumn, string requiredReferenceName = null) {
		if (currentReference?.UId != removedColumn.UId) {
			return currentReference;
		}
		if (!string.IsNullOrWhiteSpace(requiredReferenceName) && fallbackColumn == null) {
			throw new EntitySchemaDesignerException(
				$"Cannot remove column '{removedColumn.Name}' because it is the {requiredReferenceName.ToLowerInvariant()} and no valid fallback exists.");
		}
		return fallbackColumn;
	}

	private void ValidateOptionsForType(ModifyEntitySchemaColumnOptions options, int dataValueType, bool isAdd) {
		bool isLookup = dataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["lookup"];
		bool isImageLookup = EntitySchemaDesignerSupport.IsImageLookupDataValueType(dataValueType);
		ValidateLookupOptions(options, isLookup, isImageLookup, isAdd);
		ValidateTextOptions(options, dataValueType);
		ValidateMaskedOption(options, dataValueType);
		ValidateDateTimeOptions(options, dataValueType);
		ValidateDefaultValueOptions(options, dataValueType, options);
	}

	private static void ValidateLookupOptions(
		ModifyEntitySchemaColumnOptions options,
		bool isLookup,
		bool isImageLookup,
		bool isAdd) {
		if (isLookup) {
			if (string.IsNullOrWhiteSpace(options.ReferenceSchemaName) && isAdd) {
				throw new EntitySchemaDesignerException("Lookup columns require --reference-schema.");
			}
			return;
		}
		if (!HasLookupSpecificOptions(options)) {
			return;
		}
		if (isImageLookup) {
			throw new EntitySchemaDesignerException(
				"ImageLookup ('Image link') columns reference the SysImage schema automatically; " +
				"do not pass --reference-schema or other lookup-specific options.");
		}
		throw new EntitySchemaDesignerException(
			"Lookup-specific options can be used only when the effective column type is Lookup.");
	}

	private static bool HasLookupSpecificOptions(ModifyEntitySchemaColumnOptions options) {
		return !string.IsNullOrWhiteSpace(options.ReferenceSchemaName)
			|| options.SimpleLookup.HasValue
			|| options.Cascade.HasValue
			|| options.DoNotControlIntegrity.HasValue;
	}

	private static void ValidateTextOptions(ModifyEntitySchemaColumnOptions options, int dataValueType) {
		if (EntitySchemaDesignerSupport.IsTextLikeDataValueType(dataValueType) || !HasTextSpecificOptions(options)) {
			return;
		}
		throw new EntitySchemaDesignerException(
			"Text-specific options can be used only when the effective column type is Text.");
	}

	private static bool HasTextSpecificOptions(ModifyEntitySchemaColumnOptions options) {
		return options.MultilineText.HasValue
			|| options.LocalizableText.HasValue
			|| options.AccentInsensitive.HasValue
			|| options.FormatValidated.HasValue;
	}

	private static void ValidateMaskedOption(ModifyEntitySchemaColumnOptions options, int dataValueType) {
		if (!options.Masked.HasValue) {
			return;
		}

		bool isTextLikeType = EntitySchemaDesignerSupport.IsTextLikeDataValueType(dataValueType);
		bool isSecureTextType = dataValueType == EntitySchemaDesignerSupport.SupportedDataValueTypes["secureText"];
		if (!isTextLikeType && !isSecureTextType) {
			throw new EntitySchemaDesignerException(
				"Masked option can be used only when the effective column type is Text or SecureText.");
		}
	}

	private static void ValidateDateTimeOptions(ModifyEntitySchemaColumnOptions options, int dataValueType) {
		if (!EntitySchemaDesignerSupport.IsDateTimeLikeDataValueType(dataValueType) && options.UseSeconds.HasValue) {
			throw new EntitySchemaDesignerException(
				"--use-seconds can be used only when the effective column type is DateTime.");
		}
	}

	private void ValidateDefaultValueOptions(
		ModifyEntitySchemaColumnOptions options,
		int dataValueType,
		RemoteCommandOptions remoteOptions) {
		if (UsesUnsupportedLegacyBinaryDefaultValue(options, dataValueType)) {
			throw new EntitySchemaDesignerException(
				$"Type '{EntitySchemaDesignerSupport.GetFriendlyTypeName(dataValueType)}' does not support --default-value or --default-value-source Const.");
		}
		EntitySchemaDefaultValueConfig? defaultValueConfig = EntitySchemaDesignerSupport.ResolveDefaultValueConfig(
			options.DefaultValueConfig,
			options.DefaultValueSource,
			options.DefaultValue,
			$"Column '{options.ColumnName}'");
		if (defaultValueConfig != null) {
			defaultValueConfig = _defaultValueSourceResolver.Resolve(
				defaultValueConfig,
				dataValueType,
				$"Column '{options.ColumnName}'",
				remoteOptions);
		}
		EntitySchemaDesignerSupport.ValidateDefaultValueConfig(defaultValueConfig, dataValueType,
			$"Column '{options.ColumnName}'");
	}

	private static bool UsesUnsupportedLegacyBinaryDefaultValue(
		ModifyEntitySchemaColumnOptions options,
		int dataValueType) {
		if (options.DefaultValueConfig != null
			|| !EntitySchemaDesignerSupport.IsBinaryLikeDataValueType(dataValueType)) {
			return false;
		}
		EntitySchemaColumnDefSource? defaultValueSource =
			EntitySchemaDesignerSupport.ParseDefaultValueSource(options.DefaultValueSource);
		return options.DefaultValue != null || defaultValueSource == EntitySchemaColumnDefSource.Const;
	}

	/// <summary>
	/// Resolves a column to mutate by name, preferring an own column and falling back to an inherited one.
	/// Returns whether the match is inherited so the caller can enforce the caption-only rule for inherited
	/// columns. Throws when the column exists on neither collection.
	/// </summary>
	private static (EntitySchemaColumnDto Column, bool IsInherited) FindColumnForMutation(
		EntityDesignSchemaDto schema, string columnName) {
		EntitySchemaColumnDto ownColumn = (schema.Columns?.ToList() ?? []).FirstOrDefault(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
		if (ownColumn != null) {
			return (ownColumn, false);
		}

		EntitySchemaColumnDto inheritedColumn = (schema.InheritedColumns?.ToList() ?? []).FirstOrDefault(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
		if (inheritedColumn != null) {
			return (inheritedColumn, true);
		}

		throw new EntitySchemaDesignerException(
			$"Column '{columnName}' was not found in schema '{schema.Name}'.");
	}

	private (EntitySchemaColumnDto Column, string Source) FindColumnForRead(EntityDesignSchemaDto schema, string columnName) {
		EntitySchemaColumnDto ownColumn = (schema.Columns?.ToList() ?? []).FirstOrDefault(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
		if (ownColumn != null) {
			return (ownColumn, "own");
		}

		EntitySchemaColumnDto inheritedColumn = (schema.InheritedColumns?.ToList() ?? []).FirstOrDefault(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
		if (inheritedColumn != null) {
			return (inheritedColumn, "inherited");
		}

		throw new EntitySchemaDesignerException(
			$"Column '{columnName}' was not found in schema '{schema.Name}'.");
	}

	private static EntitySchemaPropertyColumnInfo MapSchemaPropertyColumn(
		EntitySchemaColumnDto column,
		string source,
		string cultureName) {
		return new EntitySchemaPropertyColumnInfo(
			column.Name,
			column.UId,
			source,
			EntitySchemaDesignerSupport.GetLocalizableValue(column.Caption, cultureName),
			EntitySchemaDesignerSupport.GetLocalizableValue(column.Description, cultureName),
			EntitySchemaDesignerSupport.GetFriendlyTypeName(column.DataValueType),
			IsRequired(column.RequirementType),
			column.Indexed,
			column.ReferenceSchema?.Name);
	}

	private void EnsureNameIsUnique(EntityDesignSchemaDto schema, string name, Guid? excludeUId) {
		bool hasDuplicate = GetAllColumns(schema)
			.Any(column => column.UId != excludeUId
				&& string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase));
		if (hasDuplicate) {
			throw new EntitySchemaDesignerException(
				$"Column '{name}' already exists in schema '{schema.Name}'.");
		}
	}

	private EntityDesignSchemaDto LoadSchema(string schemaName, Guid packageUId, string packageName,
		RemoteCommandOptions options, bool allowDependencyResolution) {
		GetSchemaDesignItemRequestDto request = new() {
			Name = schemaName,
			PackageUId = packageUId,
			UseFullHierarchy = false
		};
		DesignerResponse<EntityDesignSchemaDto>? response =
			_entitySchemaDesignerClient.TryGetSchemaDesignItem(request, options);
		bool schemaUnavailable = response == null || response.Schema == null;
		if (allowDependencyResolution && schemaUnavailable && _dependencyResolver.TryAutoResolve(schemaName, packageName)) {
			_logger.WriteInfo(
				$"Retrying GetSchemaDesignItem for '{schemaName}' after auto-dependency resolution...");
			response = _entitySchemaDesignerClient.TryGetSchemaDesignItem(request, options);
			schemaUnavailable = response == null || response.Schema == null;
		}
		if (schemaUnavailable) {
			response = _entitySchemaDesignerClient.GetSchemaDesignItem(request, options);
		}
		EntityDesignSchemaDto schema = response!.Schema
			?? throw new EntitySchemaDesignerException(
				$"GetSchemaDesignItem returned no schema for '{schemaName}'.");
		schema.Columns = schema.Columns?.ToList() ?? [];
		schema.InheritedColumns = schema.InheritedColumns?.ToList() ?? [];
		schema.Indexes = schema.Indexes?.ToList() ?? [];
		return schema;
	}

	private static int ParseSupportedType(string typeName, string actionName) {
		if (string.IsNullOrWhiteSpace(typeName)) {
			throw new EntitySchemaDesignerException($"--type is required for '{actionName}' action.");
		}
		if (!EntitySchemaDesignerSupport.TryResolveDataValueType(typeName, out int dataValueType)) {
			throw new EntitySchemaDesignerException(
				$"Unsupported type '{typeName}'. Supported types: {EntitySchemaDesignerSupport.GetSupportedTypesList()}.");
		}
		return dataValueType;
	}

	private ManagerItemDto ResolveReferenceSchema(Guid packageUId, string referenceSchemaName,
		RemoteCommandOptions options) {
		AvailableEntitySchemasResponse response = _entitySchemaDesignerClient.GetAvailableReferenceSchemas(
			new GetAvailableSchemasRequestDto {
				PackageUId = packageUId,
				UseFullHierarchy = false,
				AllowVirtual = false
			},
			options);
		ManagerItemDto referenceSchema = response.Items?.FirstOrDefault(item =>
			string.Equals(item.Name, referenceSchemaName, StringComparison.OrdinalIgnoreCase));
		return referenceSchema ?? throw new EntitySchemaDesignerException(
			$"Reference schema '{referenceSchemaName}' was not found.");
	}

	private static EntityDesignSchemaDto CreateReferenceSchema(ManagerItemDto referenceSchema) {
		return new EntityDesignSchemaDto {
			UId = referenceSchema.UId,
			Name = referenceSchema.Name,
			Caption = [EntitySchemaDesignerSupport.CreateLocalizableString(referenceSchema.Caption)]
		};
	}

	private PackageInfo ResolvePackage(string packageName) {
		PackageInfo package = _applicationPackageListProvider.GetPackages()
			.FirstOrDefault(item =>
				string.Equals(item.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));
		return package ?? throw new EntitySchemaDesignerException($"Package '{packageName}' was not found.");
	}

	private static List<EntitySchemaColumnDto> GetAllColumns(EntityDesignSchemaDto schema) {
		List<EntitySchemaColumnDto> ownColumns = schema.Columns?.ToList() ?? [];
		List<EntitySchemaColumnDto> inheritedColumns = schema.InheritedColumns?.ToList() ?? [];
		return ownColumns.Concat(inheritedColumns).ToList();
	}

	private static EntitySchemaColumnAction NormalizeAction(string action) {
		if (!Enum.TryParse(action, true, out EntitySchemaColumnAction normalizedAction)) {
			throw new EntitySchemaDesignerException(
				$"Unsupported action '{action}'. Supported actions: add, modify, remove.");
		}
		return normalizedAction;
	}

	private static int MapRequirementType(bool? required) {
		return required == true
			? (int)EntitySchemaColumnRequirementType.ApplicationLevel
			: (int)EntitySchemaColumnRequirementType.None;
	}

	private static bool IsRequired(int requirementType) {
		return requirementType != (int)EntitySchemaColumnRequirementType.None;
	}

	private void ApplyDefaultValue(
		EntitySchemaColumnDto column,
		ModifyEntitySchemaColumnOptions options,
		bool preserveWhenUnspecified,
		RemoteCommandOptions remoteOptions) {
		EntitySchemaDefaultValueConfig? defaultValueConfig = EntitySchemaDesignerSupport.ResolveDefaultValueConfig(
			options.DefaultValueConfig,
			options.DefaultValueSource,
			options.DefaultValue,
			$"Column '{options.ColumnName}'");
		if (defaultValueConfig == null) {
			if (!preserveWhenUnspecified) {
				column.DefValue = null;
			}
			return;
		}
		EntitySchemaColumnDefSource defaultValueSource = EntitySchemaDesignerSupport.ParseDefaultValueSource(
			defaultValueConfig.Source)
			?? throw new EntitySchemaDesignerException(
				$"Column '{options.ColumnName}' requires default-value-config.source.");
		if (defaultValueSource == EntitySchemaColumnDefSource.None) {
			column.DefValue = new EntitySchemaColumnDefValueDto { ValueSourceType = EntitySchemaColumnDefSource.None };
			return;
		}
		defaultValueConfig = _defaultValueSourceResolver.Resolve(
			defaultValueConfig,
			column.DataValueType ?? 0,
			$"Column '{options.ColumnName}'",
			remoteOptions,
			column.ReferenceSchema?.Name);
		column.DefValue = EntitySchemaDesignerSupport.CreateDefaultValueDto(defaultValueConfig,
			$"Column '{options.ColumnName}'");
	}

	private void ApplyColumnMutation(EntityDesignSchemaDto schema, PackageInfo package,
		ModifyEntitySchemaColumnOptions options, string effectiveCultureName) {
		EntitySchemaColumnAction action = NormalizeAction(options.Action);
		switch (action) {
			case EntitySchemaColumnAction.Add:
				AddColumn(schema, package, options, effectiveCultureName);
				return;
			case EntitySchemaColumnAction.Modify:
				ModifyColumn(schema, package, options, effectiveCultureName);
				return;
			case EntitySchemaColumnAction.Remove:
				RemoveColumn(schema, options.ColumnName);
				return;
			default:
				throw new EntitySchemaDesignerException($"Unsupported action '{options.Action}'.");
		}
	}

	private static void EnsureBatchTargetsSingleSchema(
		IEnumerable<ModifyEntitySchemaColumnOptions> operations,
		ModifyEntitySchemaColumnOptions rootOperation) {
		bool hasDifferentTarget = operations.Any(operation =>
			!string.Equals(operation.Package, rootOperation.Package, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(operation.SchemaName, rootOperation.SchemaName, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(operation.Environment, rootOperation.Environment, StringComparison.Ordinal)
			|| !string.Equals(operation.Uri, rootOperation.Uri, StringComparison.Ordinal)
			|| !string.Equals(operation.Login, rootOperation.Login, StringComparison.Ordinal)
			|| !string.Equals(operation.Password, rootOperation.Password, StringComparison.Ordinal)
			|| !string.Equals(operation.ClientId, rootOperation.ClientId, StringComparison.Ordinal)
			|| !string.Equals(operation.ClientSecret, rootOperation.ClientSecret, StringComparison.Ordinal)
			|| !string.Equals(operation.AuthAppUri, rootOperation.AuthAppUri, StringComparison.Ordinal));
		if (hasDifferentTarget) {
			throw new EntitySchemaDesignerException(
				"All batch column mutations must target the same package, schema, and environment.");
		}
	}

	private static void VerifyColumnMutations(
		EntityDesignSchemaDto reloadedSchema,
		IEnumerable<ModifyEntitySchemaColumnOptions> operations,
		string effectiveCultureName) {
		HashSet<string> ownColumnNames = (reloadedSchema.Columns?.ToList() ?? [])
			.Select(column => column.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		List<EntitySchemaColumnDto> inheritedColumns = reloadedSchema.InheritedColumns?.ToList() ?? [];

		// Expected OWN-column presence, accounting for renames (old name absent, new name present). An
		// inherited-column caption/description override stays in InheritedColumns (never in Columns), so it is
		// verified via VerifyInheritedCaptionOverride and excluded from the own-column presence check to avoid
		// a false "could not be reloaded" failure.
		Dictionary<string, bool> expectedColumnPresence = new(StringComparer.OrdinalIgnoreCase);
		foreach (ModifyEntitySchemaColumnOptions operation in operations) {
			EntitySchemaColumnAction action = NormalizeAction(operation.Action);
			string columnName = operation.ColumnName.Trim();

			if (action == EntitySchemaColumnAction.Modify && string.IsNullOrWhiteSpace(operation.NewName)
				&& !ownColumnNames.Contains(columnName)) {
				EntitySchemaColumnDto inheritedMatch = inheritedColumns.FirstOrDefault(column =>
					string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
				if (inheritedMatch != null) {
					VerifyInheritedCaptionOverride(inheritedMatch, operation, effectiveCultureName);
					continue;
				}
			}

			if (action == EntitySchemaColumnAction.Modify && !string.IsNullOrWhiteSpace(operation.NewName)) {
				expectedColumnPresence[columnName] = false;
				expectedColumnPresence[operation.NewName.Trim()] = true;
				continue;
			}

			expectedColumnPresence[columnName] = action != EntitySchemaColumnAction.Remove;
		}

		foreach ((string columnName, bool shouldExist) in expectedColumnPresence) {
			bool columnExists = ownColumnNames.Contains(columnName);
			if (shouldExist && !columnExists) {
				throw new EntitySchemaDesignerException(
					$"Column '{columnName}' could not be reloaded after save.");
			}
			if (!shouldExist && columnExists) {
				throw new EntitySchemaDesignerException(
					$"Column '{columnName}' is still present after save.");
			}
		}
	}

	/// <summary>
	/// Asserts that a caption override requested on an inherited column is reflected on the reloaded column in
	/// the effective culture (en-US fallback). A description-only override supplies no expected caption and is
	/// verified by existence alone.
	/// </summary>
	private static void VerifyInheritedCaptionOverride(
		EntitySchemaColumnDto reloadedColumn,
		ModifyEntitySchemaColumnOptions options,
		string effectiveCultureName) {
		string expectedCaption = ResolveExpectedCaption(options, effectiveCultureName);
		if (expectedCaption == null) {
			return;
		}
		string actualCaption = EntitySchemaDesignerSupport.GetLocalizableValue(
			reloadedColumn.Caption, effectiveCultureName);
		if (!string.Equals(actualCaption, expectedCaption, StringComparison.Ordinal)) {
			throw new EntitySchemaDesignerException(
				$"Caption override for inherited column '{options.ColumnName}' was not persisted " +
				$"(expected '{expectedCaption}', got '{actualCaption ?? "<none>"}').");
		}
	}

	/// <summary>
	/// Resolves the caption value a modify request is expected to write in the effective culture: the
	/// effective-culture entry of <c>title-localizations</c> (en-US fallback, then first non-empty), else the
	/// scalar title. Returns <see langword="null"/> when the request changes no caption.
	/// </summary>
	private static string ResolveExpectedCaption(
		ModifyEntitySchemaColumnOptions options,
		string effectiveCultureName) {
		if (options.TitleLocalizations is { Count: > 0 } localizations) {
			// Match culture keys case-insensitively so the expected caption resolves the same way
			// GetLocalizableValue reads the actual caption back (which uses OrdinalIgnoreCase).
			return FindCultureValue(localizations, effectiveCultureName)
				?? FindCultureValue(localizations, EntitySchemaDesignerSupport.DefaultCultureName)
				?? localizations.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
		}
		return string.IsNullOrWhiteSpace(options.Title) ? null : options.Title.Trim();
	}

	private static string FindCultureValue(IReadOnlyDictionary<string, string> localizations, string cultureName) {
		return localizations
			.Where(localization => string.Equals(localization.Key, cultureName, StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrWhiteSpace(localization.Value))
			.Select(localization => localization.Value.Trim())
			.FirstOrDefault();
	}

	private static string FormatBoolean(bool value) {
		return value ? "true" : "false";
	}

	private static string FormatBoolean(bool? value) {
		return value.HasValue ? FormatBoolean(value.Value) : "<unknown>";
	}

	private static string FormatNullableCount(int? value) {
		return value.HasValue ? value.Value.ToString() : "<unknown>";
	}

	private static string FormatText(string? value) {
		return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
	}

	private static string ResolveEffectiveTitle(string? title, string columnName) {
		return string.IsNullOrWhiteSpace(title) ? columnName : title.Trim();
	}

	private void WriteInfo(string message) {
		_logger.WriteInfo(message);
	}
}

/// <summary>
/// Groups column-metadata resolution services injected into <see cref="RemoteEntitySchemaColumnManager"/>.
/// </summary>
internal sealed record EntitySchemaColumnResolvers(
	IEntitySchemaDefaultValueSourceResolver DefaultValueSourceResolver,
	ILookupDefaultDisplayValueResolver LookupDisplayValueResolver,
	IEntitySchemaCaptionCultureResolver CaptionCultureResolver);

internal enum EntitySchemaColumnAction
{
	Add,
	Modify,
	Remove
}
