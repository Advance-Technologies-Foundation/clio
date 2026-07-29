using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

/// <summary>
/// The branding areas that <see cref="IBrandingBindingService"/> can bind into a package. Each area maps to
/// its own set of package data bindings, reconciled independently, so each apply command binds only the area
/// it owns without affecting the other.
/// </summary>
public enum BrandingScope {
	/// <summary>The product logo settings (login, menu, configuration, top panel) and splash suppression.</summary>
	Logos,

	/// <summary>The shell background image, its gallery membership, and the background configuration setting.</summary>
	Background
}

/// <summary>
/// Registers Creatio package data bindings for the branding the apply commands (<c>set-logo</c>,
/// <c>set-background-image</c>) wrote at runtime, so the branding ships with the package instead of living
/// only on the source environment. Modeled on <see cref="ILookupRegistrationService"/>: it discovers the
/// live branding rows, then reconciles the package's <c>SysPackageSchemaData</c> bindings to match —
/// creating a binding that does not exist yet, updating one that does (refreshing the delivered value
/// snapshots), and dropping one whose source row is gone. It never deletes the underlying runtime rows
/// (images or settings) — only package bindings.
/// </summary>
public interface IBrandingBindingService {
	/// <summary>
	/// Reconciles the logo data bindings in package <paramref name="packageName"/>. Each logo slot (login,
	/// menu, configuration, dark-surface toolbar, splash suppression) has its own binding, and a slot
	/// participates only when it was applied in this run (<paramref name="appliedSettingCodes"/>) or was
	/// already shipped by an earlier run — a slot the user never branded is never bound, so the package
	/// cannot overwrite an install target's own logo with this environment's stock value.
	/// </summary>
	/// <param name="packageName">Target package that receives the bindings.</param>
	/// <param name="appliedSettingCodes">The logo setting codes the current run applied.</param>
	/// <returns>A report of what was bound, skipped, or dropped.</returns>
	BrandingScopeReport BindLogos(string packageName, IReadOnlyCollection<string> appliedSettingCodes);

	/// <summary>
	/// Reconciles the background data bindings in package <paramref name="packageName"/> against the
	/// environment's current background: the configuration value and definition, the image and its gallery
	/// membership (for an image background), and the <c>UsePanelIconBackground</c> All-Users off-state.
	/// </summary>
	/// <param name="packageName">Target package that receives the bindings.</param>
	/// <returns>A report of what was bound, skipped, or dropped.</returns>
	BrandingScopeReport BindBackground(string packageName);
}

/// <summary>Per-scope outcome of a branding binding reconcile.</summary>
/// <param name="Scope">The branding area this outcome describes.</param>
/// <param name="Bound">Labels of the rows delivered by the scope's bindings.</param>
/// <param name="Skipped">Human-readable reasons for everything the scope did not deliver.</param>
/// <param name="BindingsDropped">
/// True when the run deleted at least one of the scope's package bindings — either because
/// <c>remove</c> was requested, or because a previously shipped binding no longer has a source row. Either way
/// the package now delivers less than it did, which the run summary must surface.
/// </param>
public sealed record BrandingScopeReport(
	BrandingScope Scope,
	IReadOnlyList<string> Bound,
	IReadOnlyList<string> Skipped,
	bool BindingsDropped);

internal sealed class BrandingBindingService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationPackageListProvider packageListProvider,
	IDataBindingSchemaClient schemaClient,
	ISysSettingsManager sysSettingsManager,
	ILogger logger) : IBrandingBindingService {

	/// <summary>
	/// The package that receives branding data bindings when the caller names none. <c>Custom</c> exists on
	/// every installation, so the branding always lands in a package even without an explicit choice.
	/// </summary>
	public const string DefaultPackageName = "Custom";

	/// <summary>Returns the caller-chosen binding package name, or <see cref="DefaultPackageName"/> when blank.</summary>
	public static string ResolvePackageName(string packageName) =>
		string.IsNullOrWhiteSpace(packageName) ? DefaultPackageName : packageName;

	private static readonly Guid AllUsersAdminUnitId = new("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private const string SysSettingsValueSchema = "SysSettingsValue";
	private const string SysSettingsSchema = "SysSettings";
	private const string SysImageSchema = "SysImage";
	private const string SysImageInTagSchema = "SysImageInTag";
	private const string ShellBackgroundTagName = "shell_background";

	private const string AdminUnitFeatureStateSchema = "AdminUnitFeatureState";
	private const string FeatureSchema = "Feature";
	private const string PanelIconBackgroundFeatureCode = "UsePanelIconBackground";

	private const string SecureTextValueTypeName = "SecureText";

	private static readonly IReadOnlyList<string> LogoSettingCodes = [
		SetLogoCommand.LoginLogoCode, SetLogoCommand.MenuLogoCode,
		SetLogoCommand.ConfigurationLogoCode, SetLogoCommand.DarkLogoCode, SetLogoCommand.HideSplashLogoCode
	];

	private static readonly IReadOnlyList<string> SysSettingsValueColumns = [
		"Id", "SysSettings", "SysAdminUnit", "IsDef",
		"TextValue", "IntegerValue", "FloatValue", "BooleanValue", "DateTimeValue", "GuidValue", "BinaryValue"
	];
	private static readonly IReadOnlyList<string> SysSettingsValueKeyColumns = ["SysSettings", "SysAdminUnit"];
	private static readonly IReadOnlyList<string> SysSettingsValueForceUpdateColumns = [
		"IsDef", "TextValue", "IntegerValue", "FloatValue", "BooleanValue", "DateTimeValue", "GuidValue", "BinaryValue"
	];

	private static readonly DataBindingColumnPolicy SysSettingsValuePolicy =
		new(SysSettingsValueKeyColumns, SysSettingsValueForceUpdateColumns);

	private static readonly IReadOnlyList<string> SysImageColumns = ["Id", "Name", "Data", "MimeType"];
	private static readonly IReadOnlyList<string> SysImageInTagColumns = ["Id", "Entity", "Tag"];
	private static readonly IReadOnlyList<string> SysSettingsColumns = [
		"Id", "Code", "Name", "ValueTypeName", "IsCacheable", "IsPersonal", "IsSSPAvailable", "Description"
	];

	private static readonly IReadOnlyList<string> AdminUnitFeatureStateColumns = [
		"Id", "Feature", "SysAdminUnit", "FeatureState"
	];
	private static readonly IReadOnlyList<string> AdminUnitFeatureStateKeyColumns = ["Feature", "SysAdminUnit"];
	private static readonly IReadOnlyList<string> AdminUnitFeatureStateForceUpdateColumns = ["FeatureState"];
	private static readonly DataBindingColumnPolicy AdminUnitFeatureStatePolicy =
		new(AdminUnitFeatureStateKeyColumns, AdminUnitFeatureStateForceUpdateColumns);
	private static readonly IReadOnlyList<string> FeatureColumns = ["Id", "Code", "Name"];

	private const string LogoBindingNamePrefix = "ClioBranding_Logo_";
	private const string BackgroundConfigBindingName = "ClioBranding_BackgroundConfig";
	private const string BackgroundConfigDefBindingName = "ClioBranding_BackgroundConfigDef";
	private const string BackgroundImageBindingName = "ClioBranding_BackgroundImage";
	private const string BackgroundGalleryBindingName = "ClioBranding_BackgroundGallery";
	private const string PanelIconFeatureBindingName = "ClioBranding_PanelIconFeature";
	private const string PanelIconFeatureDefBindingName = "ClioBranding_PanelIconFeatureDef";

	/// <summary>The binding folder name of one logo slot, e.g. <c>ClioBranding_Logo_LogoImage</c>.</summary>
	internal static string LogoBindingName(string settingCode) => LogoBindingNamePrefix + settingCode;

	/// <inheritdoc />
	public BrandingScopeReport BindLogos(string packageName, IReadOnlyCollection<string> appliedSettingCodes) {
		PackageRef packageRef = ResolvePackage(packageName);
		HashSet<string> applied = new(appliedSettingCodes ?? [], StringComparer.OrdinalIgnoreCase);
		List<string> unknown = applied.Where(code => !LogoSettingCodes.Contains(code, StringComparer.OrdinalIgnoreCase)).ToList();
		if (unknown.Count > 0) {
			throw new InvalidOperationException(
				$"Unknown logo setting code(s): {string.Join(", ", unknown)}. Known codes: {string.Join(", ", LogoSettingCodes)}.");
		}

		List<string> bound = [];
		List<string> skipped = [];
		bool anyDropped = false;
		foreach (string code in LogoSettingCodes) {
			string bindingName = LogoBindingName(code);
			if (!applied.Contains(code) && FindExistingBindingUId(packageRef.UId, bindingName) is null) {
				continue;
			}
			anyDropped |= ReconcileLogoSlot(packageRef, code, bindingName, bound, skipped);
		}
		return new BrandingScopeReport(BrandingScope.Logos, bound, skipped, anyDropped);
	}

	/// <inheritdoc />
	public BrandingScopeReport BindBackground(string packageName) {
		PackageRef packageRef = ResolvePackage(packageName);
		return ReconcileBackgroundScope(packageRef);
	}

	private PackageRef ResolvePackage(string packageName) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new InvalidOperationException("Package name is required to bind branding.");
		}
		return ResolvePackageRef(packageName);
	}

	/// <summary>
	/// Reconciles one logo slot's binding against the setting's live All-Users default value row, with the
	/// natural-key / force-update policy. A slot with no deliverable row — the setting is undefined, has no
	/// All-Users value, or is defined as a secret-bearing type — is reported as skipped (never silently
	/// omitted) and any previously shipped binding for it is dropped and reported.
	/// </summary>
	/// <returns><see langword="true"/> when a previously shipped binding was dropped.</returns>
	private bool ReconcileLogoSlot(
		PackageRef packageRef, string code, string bindingName, List<string> bound, List<string> skipped) {
		SysSettingsDefinition definition = FindSysSettingsDefinition(code);
		if (definition is null) {
			skipped.Add($"{code}: no All-Users value on this environment");
			return ReportDroppedBinding(packageRef, bindingName, skipped);
		}
		if (string.Equals(definition.ValueTypeName, SecureTextValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			skipped.Add(
				$"{code}: the setting is defined as {SecureTextValueTypeName} on this environment, and a secret " +
				"value is never shipped in a package");
			return ReportDroppedBinding(packageRef, bindingName, skipped);
		}
		string valueRowId = FindAllUsersValueRowId(definition.Id);
		if (valueRowId is null) {
			skipped.Add($"{code}: no All-Users value on this environment");
			return ReportDroppedBinding(packageRef, bindingName, skipped);
		}
		SaveBinding(packageRef, bindingName, SysSettingsValueSchema, SysSettingsValueColumns, [valueRowId],
			SysSettingsValuePolicy);
		bound.Add(code);
		return false;
	}

	/// <summary>
	/// Reconciles the background bindings: the CrtBackgroundConfig value row and definition, and — when the
	/// background is an image — the SysImage row and its gallery membership row. Each is bound under its own
	/// folder so a later background change refreshes them and a color-only background drops the image folders.
	/// </summary>
	private BrandingScopeReport ReconcileBackgroundScope(PackageRef packageRef) {
		List<string> bound = [];
		List<string> skipped = [];
		bool anyDropped = false;

		string configRowId = FindAllUsersValueRowId(SetBackgroundImageCommand.BackgroundConfigCode);
		if (configRowId is null) {
			skipped.Add($"{SetBackgroundImageCommand.BackgroundConfigCode}: no background configured on this environment");
			anyDropped |= ReportDroppedBinding(packageRef, BackgroundConfigBindingName, skipped);
			anyDropped |= ReportDroppedBinding(packageRef, BackgroundConfigDefBindingName, skipped);
		} else {
			SaveBinding(packageRef, BackgroundConfigBindingName, SysSettingsValueSchema, SysSettingsValueColumns,
				[configRowId], SysSettingsValuePolicy);
			bound.Add(SetBackgroundImageCommand.BackgroundConfigCode);
			BindBackgroundConfigDefinition(packageRef, bound, skipped);
		}

		anyDropped |= BindBackgroundImageAndGallery(packageRef, bound, skipped);
		anyDropped |= BindPanelIconBackgroundFeature(packageRef, bound, skipped);
		return new BrandingScopeReport(BrandingScope.Background, bound, skipped, BindingsDropped: anyDropped);
	}

	/// <summary>
	/// Binds the All-Users off-state of the <c>UsePanelIconBackground</c> feature (the panel-icon background that
	/// would otherwise hide the shell background), plus — defensively, by Id — its <c>Feature</c> definition so
	/// the state row's Feature reference resolves on the target. When the feature is not defined or has no
	/// All-Users state row, both folders are dropped so the package does not ship a stale feature toggle.
	/// </summary>
	/// <returns><see langword="true"/> when a previously shipped binding was dropped.</returns>
	private bool BindPanelIconBackgroundFeature(PackageRef packageRef, List<string> bound, List<string> skipped) {
		Guid? featureId = FindFeatureDefinitionId(PanelIconBackgroundFeatureCode);
		if (featureId is null) {
			skipped.Add($"{PanelIconBackgroundFeatureCode}: the feature is not defined on this environment");
			bool droppedState = ReportDroppedBinding(packageRef, PanelIconFeatureBindingName, skipped);
			bool droppedDef = ReportDroppedBinding(packageRef, PanelIconFeatureDefBindingName, skipped);
			return droppedState || droppedDef;
		}

		string stateRowId = FindAllUsersFeatureStateRowId(featureId.Value);
		if (stateRowId is null) {
			skipped.Add(
				$"{PanelIconBackgroundFeatureCode}: no All-Users feature state on this environment " +
				"(the feature was not turned off here)");
			bool droppedState = ReportDroppedBinding(packageRef, PanelIconFeatureBindingName, skipped);
			bool droppedDef = ReportDroppedBinding(packageRef, PanelIconFeatureDefBindingName, skipped);
			return droppedState || droppedDef;
		}

		SaveBinding(packageRef, PanelIconFeatureBindingName, AdminUnitFeatureStateSchema, AdminUnitFeatureStateColumns,
			[stateRowId], AdminUnitFeatureStatePolicy);
		bound.Add($"{PanelIconBackgroundFeatureCode} feature state");
		SaveBinding(packageRef, PanelIconFeatureDefBindingName, FeatureSchema, FeatureColumns,
			[featureId.Value.ToString()], columnPolicy: null);
		bound.Add($"{PanelIconBackgroundFeatureCode} feature definition");
		return false;
	}

	/// <summary>
	/// Binds the CrtBackgroundConfig SysSettings definition row by Id so the value row's setting reference
	/// resolves on a target that does not already ship the definition. Keyed by Id (not Code): the definition
	/// is usually product-shipped with a stable id, and preserving the id keeps the value-row reference intact.
	/// </summary>
	private void BindBackgroundConfigDefinition(PackageRef packageRef, List<string> bound, List<string> skipped) {
		SysSettingsDefinition definition = FindSysSettingsDefinition(SetBackgroundImageCommand.BackgroundConfigCode);
		if (definition is null) {
			skipped.Add($"{SetBackgroundImageCommand.BackgroundConfigCode} definition: not found");
			return;
		}
		SaveBinding(packageRef, BackgroundConfigDefBindingName, SysSettingsSchema, SysSettingsColumns,
			[definition.Id.ToString()], columnPolicy: null);
		bound.Add($"{SetBackgroundImageCommand.BackgroundConfigCode} definition");
	}

	/// <summary>
	/// When the background is an image, binds the SysImage row and its gallery membership row by Id; when it is
	/// a color (or unset, or unreadable), drops those two folders so the package does not ship a stale image.
	/// Every outcome is recorded in <paramref name="bound"/> or <paramref name="skipped"/>.
	/// </summary>
	/// <returns><see langword="true"/> when a previously shipped binding was dropped.</returns>
	private bool BindBackgroundImageAndGallery(PackageRef packageRef, List<string> bound, List<string> skipped) {
		BackgroundImageResolution resolution = ResolveConfiguredBackgroundImage();
		if (resolution.ImageId is null) {
			skipped.Add(resolution.Kind switch {
				BackgroundImageKind.Unreadable =>
					$"background image: the {SetBackgroundImageCommand.BackgroundConfigCode} value is not readable " +
					"as background configuration JSON, so no image could be identified",
				BackgroundImageKind.NotConfigured =>
					"background image: no background is configured on this environment",
				_ => "background image: the configured background is a colour, not an image"
			});
			bool droppedImage = ReportDroppedBinding(packageRef, BackgroundImageBindingName, skipped);
			bool droppedGallery = ReportDroppedBinding(packageRef, BackgroundGalleryBindingName, skipped);
			return droppedImage || droppedGallery;
		}

		Guid imageId = resolution.ImageId.Value;
		if (!RowExists(SysImageSchema, imageId)) {
			skipped.Add($"background image {imageId}: not found on this environment");
			bool droppedImage = ReportDroppedBinding(packageRef, BackgroundImageBindingName, skipped);
			bool droppedGallery = ReportDroppedBinding(packageRef, BackgroundGalleryBindingName, skipped);
			return droppedImage || droppedGallery;
		}

		SaveBinding(packageRef, BackgroundImageBindingName, SysImageSchema, SysImageColumns,
			[imageId.ToString()], columnPolicy: null);
		bound.Add("background image");

		GalleryMembership membership = FindGalleryMembership(imageId);
		if (membership is null) {
			skipped.Add("background gallery membership: not found");
			return ReportDroppedBinding(packageRef, BackgroundGalleryBindingName, skipped);
		}
		if (membership.TagId != SetBackgroundImageCommand.ShellBackgroundTagId) {
			skipped.Add(
				$"background gallery membership: this environment's {ShellBackgroundTagName} tag has a customized id " +
				$"({membership.TagId}) that would not resolve on an install target, so the membership row was not bound");
			return ReportDroppedBinding(packageRef, BackgroundGalleryBindingName, skipped);
		}
		SaveBinding(packageRef, BackgroundGalleryBindingName, SysImageInTagSchema, SysImageInTagColumns,
			[membership.RowId], columnPolicy: null);
		bound.Add("background gallery membership");
		return false;
	}

	/// <summary>
	/// Deletes a binding that the current live state no longer supports and, when one was actually deleted,
	/// records it in <paramref name="skipped"/>. Reconciling away a previously shipped binding is a change to
	/// the package the user must see in the report, not a silent side effect.
	/// </summary>
	/// <returns><see langword="true"/> when a binding was actually deleted.</returns>
	private bool ReportDroppedBinding(PackageRef packageRef, string bindingName, List<string> skipped) {
		if (!DeleteBindingIfExists(packageRef, bindingName)) {
			return false;
		}
		skipped.Add($"{bindingName}: previously shipped binding removed, it no longer has a source row");
		return true;
	}

	/// <summary>Why <see cref="ResolveConfiguredBackgroundImage"/> produced no image id.</summary>
	private enum BackgroundImageKind {
		/// <summary>No background is configured on this environment at all.</summary>
		NotConfigured,

		/// <summary>A background is configured, but it is a colour rather than an image.</summary>
		NoImage,

		/// <summary>The configuration value could not be parsed as background configuration JSON.</summary>
		Unreadable,

		/// <summary>The background is an image and its id was resolved.</summary>
		Image
	}

	/// <summary>Outcome of reading the CrtBackgroundConfig value.</summary>
	private sealed record BackgroundImageResolution(BackgroundImageKind Kind, Guid? ImageId);

	/// <summary>
	/// Parses the CrtBackgroundConfig value into the configured image id. Distinguishes a colour/unset
	/// background from a value that cannot be parsed, so the caller can report the two differently instead of
	/// treating a corrupted configuration as a deliberate colour background.
	/// </summary>
	private BackgroundImageResolution ResolveConfiguredBackgroundImage() {
		string configJson = sysSettingsManager.GetAllUsersDefaultByCode(SetBackgroundImageCommand.BackgroundConfigCode);
		if (string.IsNullOrWhiteSpace(configJson)) {
			return new BackgroundImageResolution(BackgroundImageKind.NotConfigured, null);
		}
		try {
			using JsonDocument document = JsonDocument.Parse(configJson);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) {
				return new BackgroundImageResolution(BackgroundImageKind.Unreadable, null);
			}
			if (!root.TryGetProperty("imageId", out JsonElement imageIdElement)) {
				return new BackgroundImageResolution(BackgroundImageKind.NoImage, null);
			}
			if (imageIdElement.ValueKind == JsonValueKind.Null) {
				return new BackgroundImageResolution(BackgroundImageKind.NoImage, null);
			}
			if (imageIdElement.ValueKind != JsonValueKind.String
				|| !Guid.TryParse(imageIdElement.GetString(), out Guid parsed)) {
				return new BackgroundImageResolution(BackgroundImageKind.Unreadable, null);
			}
			return parsed == Guid.Empty
				? new BackgroundImageResolution(BackgroundImageKind.NoImage, null)
				: new BackgroundImageResolution(BackgroundImageKind.Image, parsed);
		} catch (JsonException) {
			return new BackgroundImageResolution(BackgroundImageKind.Unreadable, null);
		}
	}

	/// <summary>
	/// Creates or refreshes a binding: projects the runtime schema to <paramref name="columnNames"/>, resolves
	/// the existing binding id (so a re-save updates in place), and posts SaveSchema with the desired bound-id
	/// set. Passing a reduced set drops rows; the caller deletes an empty binding instead of calling this.
	/// </summary>
	private void SaveBinding(
		PackageRef packageRef,
		string bindingName,
		string schemaName,
		IReadOnlyCollection<string> columnNames,
		IReadOnlyList<string> boundRecordIds,
		DataBindingColumnPolicy? columnPolicy) {
		DataBindingDbSchema schema = FetchProjectedSchema(schemaName, columnNames);
		Guid? existingUId = FindExistingBindingUId(packageRef.UId, bindingName, schemaName);
		string requestBody = DataBindingDbService.BuildSaveSchemaDataRequest(
			packageRef, bindingName, schemaName, schema, boundRecordIds.ToList(), existingUId, columnPolicy);
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData), requestBody);
		DataBindingDbService.ThrowIfUnsuccessful(response, "SaveSchema");
		logger.WriteInfo($"Bound {boundRecordIds.Count} row(s) into '{bindingName}' ({schemaName}).");
	}

	/// <summary>
	/// Fetches the runtime schema and projects it to <paramref name="columnNames"/>, requiring every requested
	/// column to exist. A silently reduced projection is never acceptable here: dropping a key column would
	/// degrade a natural-key binding into a wildcard that force-updates every row of the setting on the target
	/// (personal overrides included), and dropping a value column would ship an empty snapshot. Both failures
	/// are invisible until install, so an absent column is a hard error instead.
	/// </summary>
	private DataBindingDbSchema FetchProjectedSchema(string schemaName, IReadOnlyCollection<string> columnNames) {
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
				$"Schema '{schemaName}' on this environment is missing column(s) required by the branding " +
				$"binding: {string.Join(", ", missingColumns)}. Binding a partial projection would ship an " +
				"incomplete or wildcard-matching binding, so the operation was stopped.");
		}
		return new DataBindingDbSchema(
			schema.UId, schema.Name, projected.Select(c => c.Name).ToList(), projected);
	}

	private PackageRef ResolvePackageRef(string packageName) {
		PackageInfo package = packageListProvider.GetPackages()
			.FirstOrDefault(pkg => string.Equals(pkg.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));
		if (package is null) {
			throw new InvalidOperationException(
				$"Package '{packageName}' was not found in the remote environment. Check the name against list-packages.");
		}
		return new PackageRef(package.Descriptor.UId, package.Descriptor.Name);
	}

	/// <summary>Returns the id of a setting's All-Users default value row, or null when the setting has no such row.</summary>
	private string FindAllUsersValueRowId(string settingCode) {
		SysSettingsDefinition definition = FindSysSettingsDefinition(settingCode);
		return definition is null ? null : FindAllUsersValueRowId(definition.Id);
	}

	private string FindAllUsersValueRowId(Guid definitionId) {
		BrandingRowIdResponse response = SelectQueryHelper.ExecuteSelectQuery<BrandingRowIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				SysSettingsValueSchema,
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition("SysSettings", definitionId.ToString(), SelectQueryHelper.GuidDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition("SysAdminUnit", AllUsersAdminUnitId.ToString(), SelectQueryHelper.GuidDataValueType)
				]));
		return SingleRowId(response,
			$"All-Users value rows of setting '{definitionId}'");
	}

	/// <summary>Returns the id of a feature's persisted <c>Feature</c> definition row by code, or null when it is not defined.</summary>
	private Guid? FindFeatureDefinitionId(string featureCode) {
		BrandingRowIdResponse response = SelectQueryHelper.ExecuteSelectQuery<BrandingRowIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				FeatureSchema,
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[new SelectQueryHelper.SelectQueryFilterDefinition("Code", featureCode, SelectQueryHelper.TextDataValueType)]));
		string id = SingleRowId(response, $"the '{featureCode}' feature definition");
		return id is not null && Guid.TryParse(id, out Guid featureId) ? featureId : null;
	}

	/// <summary>Returns the id of a feature's All-Users <c>AdminUnitFeatureState</c> row, or null when there is none.</summary>
	private string FindAllUsersFeatureStateRowId(Guid featureId) {
		BrandingRowIdResponse response = SelectQueryHelper.ExecuteSelectQuery<BrandingRowIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				AdminUnitFeatureStateSchema,
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition("Feature", featureId.ToString(), SelectQueryHelper.GuidDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition("SysAdminUnit", AllUsersAdminUnitId.ToString(), SelectQueryHelper.GuidDataValueType)
				]));
		return SingleRowId(response, $"All-Users feature state of feature '{featureId}'");
	}

	/// <summary>The definition metadata of a system setting: its row id and declared value type.</summary>
	private sealed record SysSettingsDefinition(Guid Id, string ValueTypeName);

	/// <summary>
	/// Resolves a setting's definition row by code, returning its id and <c>ValueTypeName</c>. The value type is
	/// carried so the caller can refuse to bind a setting that a customization redefined as a secret type.
	/// </summary>
	private SysSettingsDefinition FindSysSettingsDefinition(string settingCode) {
		BrandingSettingDefinitionResponse response =
			SelectQueryHelper.ExecuteSelectQuery<BrandingSettingDefinitionResponse>(
				applicationClient, serviceUrlBuilder,
				SelectQueryHelper.BuildSelectQuery(
					SysSettingsSchema,
					[
						new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
						new SelectQueryHelper.SelectQueryColumnDefinition("ValueTypeName", "ValueTypeName")
					],
					[new SelectQueryHelper.SelectQueryFilterDefinition("Code", settingCode, SelectQueryHelper.TextDataValueType)]));
		if (response.Rows.Count > 1) {
			throw new InvalidOperationException(
				$"System setting '{settingCode}' has multiple definitions on this environment, so the branding " +
				"binding cannot tell which one to deliver.");
		}
		BrandingSettingDefinitionDto row = response.Rows.FirstOrDefault();
		return row is not null && Guid.TryParse(row.Id, out Guid definitionId)
			? new SysSettingsDefinition(definitionId, row.ValueTypeName ?? string.Empty)
			: null;
	}

	/// <summary>An image's shell-background gallery membership row, and the tag id it was found under.</summary>
	private sealed record GalleryMembership(string RowId, Guid TagId);

	/// <summary>
	/// Finds the image's shell-background gallery membership row. The tag id it matched under is returned with
	/// it, because a membership discovered under a customized tag id cannot be delivered to another environment.
	/// </summary>
	private GalleryMembership FindGalleryMembership(Guid imageId) {
		Guid wellKnownTagId = SetBackgroundImageCommand.ShellBackgroundTagId;
		string rowId = QueryGalleryMembershipRowId(imageId, wellKnownTagId);
		if (rowId is not null) {
			return new GalleryMembership(rowId, wellKnownTagId);
		}
		string resolvedTagId = FindShellBackgroundTagId();
		if (resolvedTagId is null || !Guid.TryParse(resolvedTagId, out Guid namedTagId) || namedTagId == wellKnownTagId) {
			return null;
		}
		rowId = QueryGalleryMembershipRowId(imageId, namedTagId);
		return rowId is null ? null : new GalleryMembership(rowId, namedTagId);
	}

	private string QueryGalleryMembershipRowId(Guid imageId, Guid tagId) {
		BrandingRowIdResponse response = SelectQueryHelper.ExecuteSelectQuery<BrandingRowIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				SysImageInTagSchema,
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition("Entity", imageId.ToString(), SelectQueryHelper.GuidDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition("Tag", tagId.ToString(), SelectQueryHelper.GuidDataValueType)
				]));
		return SingleRowId(response, $"the gallery membership of image '{imageId}' under tag '{tagId}'");
	}

	private string FindShellBackgroundTagId() {
		BrandingRowIdResponse response = SelectQueryHelper.ExecuteSelectQuery<BrandingRowIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				"SysImageTag",
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[new SelectQueryHelper.SelectQueryFilterDefinition("Name", ShellBackgroundTagName, SelectQueryHelper.TextDataValueType)]));
		return SingleRowId(response, $"the '{ShellBackgroundTagName}' image tag");
	}

	private bool RowExists(string schemaName, Guid rowId) {
		BrandingRowIdResponse response = SelectQueryHelper.ExecuteSelectQuery<BrandingRowIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				schemaName,
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[new SelectQueryHelper.SelectQueryFilterDefinition("Id", rowId.ToString(), SelectQueryHelper.GuidDataValueType)]));
		return SingleRowId(response, $"row '{rowId}' of '{schemaName}'") is not null;
	}

	/// <summary>
	/// Resolves the UId of an existing branding binding so a re-save updates it in place. When
	/// <paramref name="expectedSchemaName"/> is supplied and the existing binding delivers a different entity
	/// schema, the reconcile stops: the name collided with a binding this command does not own, and silently
	/// re-saving it under the branding schema would destroy whatever it delivered.
	/// </summary>
	private Guid? FindExistingBindingUId(Guid packageUId, string bindingName, string expectedSchemaName = null) {
		BrandingBindingUIdResponse response = SelectQueryHelper.ExecuteSelectQuery<BrandingBindingUIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				"SysPackageSchemaData",
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId"),
					new SelectQueryHelper.SelectQueryColumnDefinition("SysSchema.Name", "EntitySchemaName")
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition("Name", bindingName, SelectQueryHelper.TextDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition("SysPackage.UId", packageUId.ToString(), SelectQueryHelper.GuidDataValueType)
				]));
		if (response.Rows.Count > 1) {
			throw new InvalidOperationException(
				$"Package data binding '{bindingName}' has multiple registrations in package '{packageUId}'.");
		}
		BrandingBindingUIdDto row = response.Rows.FirstOrDefault();
		if (row is null) {
			return null;
		}
		if (expectedSchemaName is not null
			&& !string.IsNullOrWhiteSpace(row.EntitySchemaName)
			&& !string.Equals(row.EntitySchemaName, expectedSchemaName, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(
				$"Package data binding '{bindingName}' already exists for schema '{row.EntitySchemaName}', " +
				$"but branding delivery needs it for '{expectedSchemaName}'. Rename or remove the existing " +
				"binding before binding branding into this package.");
		}
		return Guid.TryParse(row.UId, out Guid parsed) ? parsed : null;
	}

	private bool DeleteBindingIfExists(PackageRef packageRef, string bindingName) {
		if (FindExistingBindingUId(packageRef.UId, bindingName) is null) {
			return false;
		}
		string body = JsonSerializer.Serialize(new { packageUId = packageRef.UId.ToString(), packageSchemaDataName = bindingName });
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeletePackageSchemaData), body);
		DataBindingDbService.ThrowIfUnsuccessful(response, "DeletePackageSchemaDataRequest");
		logger.WriteInfo($"Removed branding binding '{bindingName}'.");
		return true;
	}

	/// <summary>
	/// Returns the single matching row id, or null when nothing matched. More than one match is an error rather
	/// than a first-wins pick: the reconciler would otherwise deliver a row the caller never chose, and — for a
	/// natural-key binding — force-update the target from it. Mirrors the multiplicity guards the sibling
	/// lookup-registration and package-binding queries already apply.
	/// </summary>
	private static string SingleRowId(BrandingRowIdResponse response, string subject) {
		if (response.Rows.Count > 1) {
			throw new InvalidOperationException(
				$"Expected at most one match for {subject} on this environment, but found {response.Rows.Count}. " +
				"Resolve the duplicates before binding branding, so the package delivers a row you chose.");
		}
		string id = response.Rows.FirstOrDefault()?.Id;
		return string.IsNullOrWhiteSpace(id) ? null : id;
	}

	private sealed class BrandingRowIdResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[System.Text.Json.Serialization.JsonPropertyName("rows")]
		public List<BrandingRowIdDto> Rows { get; init; } = [];
	}

	private sealed class BrandingRowIdDto {
		[System.Text.Json.Serialization.JsonPropertyName("Id")]
		public string Id { get; init; }
	}

	private sealed class BrandingBindingUIdResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[System.Text.Json.Serialization.JsonPropertyName("rows")]
		public List<BrandingBindingUIdDto> Rows { get; init; } = [];
	}

	private sealed class BrandingBindingUIdDto {
		[System.Text.Json.Serialization.JsonPropertyName("UId")]
		public string UId { get; init; }

		[System.Text.Json.Serialization.JsonPropertyName("EntitySchemaName")]
		public string EntitySchemaName { get; init; }
	}

	private sealed class BrandingSettingDefinitionResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[System.Text.Json.Serialization.JsonPropertyName("rows")]
		public List<BrandingSettingDefinitionDto> Rows { get; init; } = [];
	}

	private sealed class BrandingSettingDefinitionDto {
		[System.Text.Json.Serialization.JsonPropertyName("Id")]
		public string Id { get; init; }

		[System.Text.Json.Serialization.JsonPropertyName("ValueTypeName")]
		public string ValueTypeName { get; init; }
	}
}
