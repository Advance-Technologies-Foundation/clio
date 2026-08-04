using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

/// <summary>
/// Outcome of binding one folder (or a folder group that stands or falls together).
/// </summary>
/// <param name="Bound">Whether the folder now delivers the requested rows.</param>
/// <param name="Warnings">
/// Human-readable reasons for every gap between what the caller asked for and what the package will actually
/// deliver, including a folder the package previously shipped and this delivery removed. Each entry means the
/// package delivers less than the caller may expect, so run summaries must relay them.
/// </param>
public sealed record PackageDataBindingOutcome(
	bool Bound,
	IReadOnlyList<string> Warnings) {

	/// <summary>Creates a successful outcome.</summary>
	public static PackageDataBindingOutcome Success() {
		return new PackageDataBindingOutcome(true, []);
	}

	/// <summary>Creates an outcome for a delivery that could not happen.</summary>
	/// <param name="warnings">Caller-facing reasons nothing was delivered, in the order they arose.</param>
	public static PackageDataBindingOutcome Refused(IReadOnlyList<string> warnings) {
		return new PackageDataBindingOutcome(false, warnings);
	}
}

/// <summary>
/// Builds package data binding folder names following the platform convention
/// <c>&lt;EntitySchema&gt;_&lt;Suffix&gt;</c>, where the suffix is the setting code, the feature code, or the
/// bound row's role.
/// </summary>
internal static class PackageDataBindingNames {

	/// <summary>The folder that binds a sys-setting's All-Users value row, e.g. <c>SysSettingsValue_LogoImage</c>.</summary>
	internal static string SysSettingsValue(string settingCode) {
		return For("SysSettingsValue", settingCode);
	}

	/// <summary>The folder that binds a sys-setting's definition row, e.g. <c>SysSettings_CrtBackgroundConfig</c>.</summary>
	internal static string SysSettings(string settingCode) {
		return For("SysSettings", settingCode);
	}

	/// <summary>The folder that binds rows of <paramref name="schemaName"/> under <paramref name="suffix"/>.</summary>
	internal static string For(string schemaName, string suffix) {
		return $"{schemaName}_{suffix}";
	}
}

/// <summary>
/// Where a package's data ends up. Each method delivers one binding folder (or a group that stands or falls
/// together) and guarantees the folder matches its source afterwards: when the source cannot be delivered, a
/// folder the package previously shipped is removed rather than left carrying a snapshot nothing backs.
/// </summary>
/// <remarks>
/// Two implementations are expected, matching clio's two development flows: one that registers the data in a
/// remote environment (used when there is no workspace) and one that writes package files on disk (used
/// inside a workspace, where nothing may change the environment and everything ships with the package push).
/// The target owns the per-entity column sets and install-time matching policies so no caller restates them.
/// </remarks>
public interface IPackageDataBinder {

	/// <summary>
	/// Names the package every subsequent delivery lands in. Call once before delivering.
	/// </summary>
	/// <param name="packageName">
	/// Name of the package that receives the data. Blank means "the package the environment's
	/// <c>CurrentPackageId</c> system setting points at"; a well-known package name is never silently
	/// substituted.
	/// </param>
	/// <returns>The resolved package name, which the caller reports to the user.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the package cannot be resolved: the named package does not exist, or no package was named
	/// and the current-package setting does not point at a usable one.
	/// </exception>
	string UsePackage(string packageName);

	/// <summary>
	/// Binds a sys-setting's All-Users default value into the <c>SysSettingsValue_&lt;code&gt;</c> folder,
	/// optionally together with the setting's own definition row (<c>SysSettings_&lt;code&gt;</c>). Matched on
	/// the setting's natural key at install time, because the value row's own Id differs per environment —
	/// keying on Id would insert a second default row beside the target's own instead of updating it. The
	/// definition, when requested, is keyed by its own Id so the value row's setting reference stays
	/// resolvable, and lands before the value row: a refusal drops both folders, and a write that fails
	/// partway can leave at most the definition, which resolves on its own. The package therefore never
	/// carries a value row whose setting it does not also ship.
	/// </summary>
	/// <param name="settingCode">The sys-setting code.</param>
	/// <param name="includeDefinition">
	/// Whether to also bind the setting's definition row. Request it only for a setting clio itself creates —
	/// a product-shipped setting is present on every installation and its definition needs no delivery.
	/// </param>
	/// <returns>
	/// A refusal when the setting is undefined, has no All-Users value, or is declared as a secret-bearing
	/// type — a secret value is never shipped inside a package.
	/// </returns>
	PackageDataBindingOutcome BindSysSettingsValue(string settingCode, bool includeDefinition = false);

	/// <summary>
	/// Removes the folders <see cref="BindSysSettingsValue"/> would have delivered for
	/// <paramref name="settingCode"/>, when this delivery owns them. Use it when the caller decides the setting
	/// must not ship after all — a setting whose value points at a row this delivery could not bind would
	/// install a reference the target cannot resolve.
	/// </summary>
	/// <param name="settingCode">The sys-setting code.</param>
	/// <param name="includeDefinition">Whether the setting's definition folder is part of the same delivery.</param>
	/// <returns>A warning for each removal and for each collision that prevented one.</returns>
	IReadOnlyList<string> RemoveSysSettingsValue(string settingCode, bool includeDefinition = false);

	/// <summary>
	/// Binds one row of any entity by its own Id into the <c>&lt;schema&gt;_&lt;suffix&gt;</c> folder. Correct
	/// when the Id is stable across environments — a row clio created, or a product-shipped row whose Id must
	/// be preserved so references to it stay intact.
	/// </summary>
	/// <param name="schemaName">Entity schema of the row.</param>
	/// <param name="bindingSuffix">The folder suffix naming the bound row's role.</param>
	/// <param name="columns">
	/// Columns the folder delivers. Must include <c>Id</c>: it is the key the install target matches the
	/// delivered row on, and a set without it matches every row of the entity instead.
	/// </param>
	/// <param name="rowId">Id of the row.</param>
	/// <returns>A refusal when the row does not exist.</returns>
	PackageDataBindingOutcome BindRow(
		string schemaName,
		string bindingSuffix,
		IReadOnlyList<string> columns,
		Guid rowId);

	/// <summary>
	/// Binds a feature's confirmed All-Users off-state — the <c>AdminUnitFeatureState</c> row matched on
	/// the feature's natural key, into <c>AdminUnitFeatureState_&lt;code&gt;</c> — plus, defensively by Id,
	/// its <c>Feature</c> definition row into <c>Feature_&lt;code&gt;</c>, which lands first so the package
	/// never carries a state row whose feature it does not also ship. Only a state row this method itself
	/// confirmed to be off is ever delivered: the state binding force-updates the flag on install, so
	/// shipping a row that is still on would turn the feature back on for the target. The state can never be
	/// assumed from the caller's own turn-off attempt — the attempt may have been skipped or may have failed.
	/// </summary>
	/// <param name="featureCode">The feature code.</param>
	/// <returns>
	/// A refusal — dropping both folders — when the feature is not defined, has no All-Users state row, or
	/// that row is not confirmed off.
	/// </returns>
	PackageDataBindingOutcome BindFeatureOffState(string featureCode);

	/// <summary>
	/// Removes a binding folder the package previously shipped, when this delivery owns it. Removes only the
	/// package registration — the rows it delivered stay where they are. A same-name folder that delivers a
	/// different entity schema, or one whose schema the environment does not report, is left untouched with a
	/// warning: deleting a registration this delivery cannot identify as its own would destroy package data it
	/// does not own.
	/// </summary>
	/// <param name="folderName">Binding folder name.</param>
	/// <param name="expectedSchemaName">The entity schema this delivery would have shipped under the name.</param>
	/// <returns>A warning for the deletion or for the collision that prevented it.</returns>
	IReadOnlyList<string> RemoveBinding(string folderName, string expectedSchemaName);
}

/// <summary>
/// Binds package data by registering it in a remote environment's <c>SysPackageSchemaData</c> — the
/// target for the no-workspace flow, where the rows already live on that environment.
/// </summary>
internal sealed class EnvironmentPackageDataBinder(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IPackageDataBindingWriter bindingWriter,
	IPackageTargetResolver targetResolver,
	ILogger logger) : IPackageDataBinder {

	private const string SysSettingsValueSchema = "SysSettingsValue";
	private const string SysSettingsSchema = "SysSettings";
	private const string AdminUnitFeatureStateSchema = "AdminUnitFeatureState";
	private const string FeatureSchema = "Feature";

	private const string SecureTextValueTypeName = "SecureText";

	private const string SysSettingsColumn = "SysSettings";
	private const string SysAdminUnitColumn = "SysAdminUnit";
	private const string FeatureColumn = "Feature";
	private const string FeatureStateColumn = "FeatureState";

	private static readonly IReadOnlyList<string> SysSettingsValueColumns = [
		"Id", SysSettingsColumn, SysAdminUnitColumn, "IsDef",
		"TextValue", "IntegerValue", "FloatValue", "BooleanValue", "DateTimeValue", "GuidValue", "BinaryValue"
	];
	private static readonly IReadOnlyList<string> SysSettingsValueKeyColumns = [SysSettingsColumn, SysAdminUnitColumn];
	private static readonly IReadOnlyList<string> SysSettingsValueForceUpdateColumns = [
		"IsDef", "TextValue", "IntegerValue", "FloatValue", "BooleanValue", "DateTimeValue", "GuidValue", "BinaryValue"
	];
	private static readonly DataBindingColumnPolicy SysSettingsValuePolicy =
		new(SysSettingsValueKeyColumns, SysSettingsValueForceUpdateColumns);

	private static readonly IReadOnlyList<string> SysSettingsColumns = [
		"Id", "Code", "Name", "ValueTypeName", "IsCacheable", "IsPersonal", "IsSSPAvailable", "Description"
	];

	private static readonly IReadOnlyList<string> AdminUnitFeatureStateColumns = [
		"Id", FeatureColumn, SysAdminUnitColumn, FeatureStateColumn
	];
	private static readonly IReadOnlyList<string> AdminUnitFeatureStateKeyColumns = [FeatureColumn, SysAdminUnitColumn];
	private static readonly IReadOnlyList<string> AdminUnitFeatureStateForceUpdateColumns = [FeatureStateColumn];
	private static readonly DataBindingColumnPolicy AdminUnitFeatureStatePolicy =
		new(AdminUnitFeatureStateKeyColumns, AdminUnitFeatureStateForceUpdateColumns);
	private static readonly IReadOnlyList<string> FeatureColumns = ["Id", "Code", "Name"];

	private PackageRef _package;

	/// <inheritdoc />
	public string UsePackage(string packageName) {
		PackageTargetResolution resolution = targetResolver.Resolve(packageName);
		if (!resolution.Success) {
			throw new InvalidOperationException(resolution.Error);
		}
		_package = new PackageRef(resolution.PackageUId, resolution.PackageName);
		return _package.Name;
	}

	/// <inheritdoc />
	public PackageDataBindingOutcome BindSysSettingsValue(string settingCode, bool includeDefinition = false) {
		string folderName = PackageDataBindingNames.SysSettingsValue(settingCode);
		string definitionFolderName = includeDefinition ? PackageDataBindingNames.SysSettings(settingCode) : null;
		List<string> warnings = [];
		SysSettingsDefinition definition = FindSysSettingsDefinition(settingCode);
		if (definition is null) {
			warnings.Add(
				$"{settingCode}: the setting is not defined on this environment, so there is nothing to bind. A " +
				"value binding references its setting by id, so a target that does not ship this definition could " +
				"not resolve the row either");
			return RefuseSettingBinding(
				folderName, definitionFolderName, warnings, BindingRemovalCause.SourceRowUnavailable);
		}
		if (IsSecretBearing(definition)) {
			warnings.Add(
				$"{settingCode}: the setting is defined as {SecureTextValueTypeName} on this environment, and a " +
				"secret value is never shipped in a package");
			return RefuseSettingBinding(
				folderName, definitionFolderName, warnings, BindingRemovalCause.SourceRowNotShippable);
		}
		string valueRowId = FindAllUsersValueRowId(definition.Id);
		if (valueRowId is null) {
			warnings.Add($"{settingCode}: no All-Users value on this environment");
			return RefuseSettingBinding(
				folderName, definitionFolderName, warnings, BindingRemovalCause.SourceRowUnavailable);
		}

		if (definitionFolderName is not null) {
			SaveBinding(definitionFolderName, SysSettingsSchema, SysSettingsColumns,
				[definition.Id.ToString()], columnPolicy: null);
		}
		SaveBinding(folderName, SysSettingsValueSchema, SysSettingsValueColumns, [valueRowId],
			SysSettingsValuePolicy);
		return PackageDataBindingOutcome.Success();
	}

	/// <inheritdoc />
	public IReadOnlyList<string> RemoveSysSettingsValue(string settingCode, bool includeDefinition = false) {
		List<string> warnings = [];
		RemoveIfShipped(
			PackageDataBindingNames.SysSettingsValue(settingCode), SysSettingsValueSchema, warnings,
			BindingRemovalCause.WithdrawnByCaller);
		if (includeDefinition) {
			RemoveIfShipped(
				PackageDataBindingNames.SysSettings(settingCode), SysSettingsSchema, warnings,
				BindingRemovalCause.WithdrawnByCaller);
		}
		return warnings;
	}

	/// <inheritdoc />
	public PackageDataBindingOutcome BindRow(
		string schemaName,
		string bindingSuffix,
		IReadOnlyList<string> columns,
		Guid rowId) {
		string folderName = PackageDataBindingNames.For(schemaName, bindingSuffix);
		List<string> warnings = [];
		if (rowId == Guid.Empty || !bindingWriter.RowExists(schemaName, rowId)) {
			warnings.Add($"{folderName}: row '{rowId}' not found on this environment");
			RemoveIfShipped(folderName, schemaName, warnings, BindingRemovalCause.SourceRowUnavailable);
			return PackageDataBindingOutcome.Refused(warnings);
		}
		SaveBinding(folderName, schemaName, columns, [rowId.ToString()], columnPolicy: null);
		return PackageDataBindingOutcome.Success();
	}

	/// <inheritdoc />
	public PackageDataBindingOutcome BindFeatureOffState(string featureCode) {
		string stateFolderName = PackageDataBindingNames.For(AdminUnitFeatureStateSchema, featureCode);
		string definitionFolderName = PackageDataBindingNames.For(FeatureSchema, featureCode);
		List<string> warnings = [];
		Guid? featureId = FindFeatureDefinitionId(featureCode);
		if (featureId is null) {
			warnings.Add($"{featureCode}: the feature is not defined on this environment");
			return RefuseFeatureBinding(
				stateFolderName, definitionFolderName, warnings, BindingRemovalCause.SourceRowUnavailable);
		}

		FeatureStateRow stateRow = FindAllUsersFeatureStateRow(featureId.Value);
		if (stateRow is null) {
			warnings.Add(
				$"{featureCode}: no All-Users feature state on this environment " +
				"(the feature was not turned off here)");
			return RefuseFeatureBinding(
				stateFolderName, definitionFolderName, warnings, BindingRemovalCause.SourceRowUnavailable);
		}
		if (stateRow.IsOff != true) {
			warnings.Add(
				$"{featureCode}: the All-Users feature state on this environment is " +
				$"{(stateRow.IsOff is null ? "not readable as an on/off value" : "still on")}, and only a confirmed " +
				"off-state is bound — the binding force-updates FeatureState on install, so delivering this row " +
				"would turn the feature back on for the target");
			return RefuseFeatureBinding(
				stateFolderName, definitionFolderName, warnings, BindingRemovalCause.SourceRowNotShippable);
		}

		SaveBinding(definitionFolderName, FeatureSchema, FeatureColumns,
			[featureId.Value.ToString()], columnPolicy: null);
		SaveBinding(stateFolderName, AdminUnitFeatureStateSchema, AdminUnitFeatureStateColumns,
			[stateRow.RowId], AdminUnitFeatureStatePolicy);
		return PackageDataBindingOutcome.Success();
	}

	/// <inheritdoc />
	public IReadOnlyList<string> RemoveBinding(string folderName, string expectedSchemaName) {
		List<string> warnings = [];
		RemoveIfShipped(folderName, expectedSchemaName, warnings, BindingRemovalCause.WithdrawnByCaller);
		return warnings;
	}

	private PackageDataBindingOutcome RefuseSettingBinding(
		string folderName, string definitionFolderName, List<string> warnings, BindingRemovalCause cause) {
		RemoveIfShipped(folderName, SysSettingsValueSchema, warnings, cause);
		if (definitionFolderName is not null) {
			RemoveIfShipped(definitionFolderName, SysSettingsSchema, warnings, cause);
		}
		return PackageDataBindingOutcome.Refused(warnings);
	}

	private PackageDataBindingOutcome RefuseFeatureBinding(
		string stateFolderName, string definitionFolderName, List<string> warnings, BindingRemovalCause cause) {
		RemoveIfShipped(stateFolderName, AdminUnitFeatureStateSchema, warnings, cause);
		RemoveIfShipped(definitionFolderName, FeatureSchema, warnings, cause);
		return PackageDataBindingOutcome.Refused(warnings);
	}

	private void SaveBinding(
		string folderName,
		string schemaName,
		IReadOnlyCollection<string> columnNames,
		IReadOnlyList<string> boundRecordIds,
		DataBindingColumnPolicy columnPolicy) {
		PackageRef package = RequirePackage();
		DataBindingDbSchema schema = bindingWriter.ProjectSchema(schemaName, columnNames);
		PackageDataBindingRef existing = bindingWriter.FindBinding(package.UId, folderName);
		if (existing is not null && !DeliversConfirmedSchema(existing, schemaName)) {
			throw new InvalidOperationException(DeliversForeignSchema(existing, schemaName)
				? $"Package data binding '{folderName}' already exists for schema "
					+ $"'{existing.EntitySchemaName}', but this delivery needs it for '{schemaName}'. Rename or "
					+ "remove the existing binding first."
				: $"Package data binding '{folderName}' already exists, but the environment did not report which "
					+ $"entity schema it delivers, so this delivery cannot confirm it is the '{schemaName}' folder "
					+ "it would refresh — and refreshing replaces the rows and the schema the registration "
					+ "currently carries.");
		}
		bindingWriter.SaveBinding(
			package, folderName, schemaName, schema, boundRecordIds, existing?.UId, columnPolicy);
		logger.WriteInfo($"Bound {boundRecordIds.Count} row(s) into '{folderName}' ({schemaName}).");
	}

	private void RemoveIfShipped(
		string folderName, string expectedSchemaName, List<string> warnings, BindingRemovalCause cause) {
		PackageRef package = RequirePackage();
		PackageDataBindingRef existing = bindingWriter.FindBinding(package.UId, folderName);
		if (existing is null) {
			return;
		}
		if (DeliversForeignSchema(existing, expectedSchemaName)) {
			warnings.Add(
				$"{folderName}: left untouched — the package already carries a binding of this name for schema " +
				$"'{existing.EntitySchemaName}', which this delivery does not own.");
			return;
		}
		if (string.IsNullOrWhiteSpace(existing.EntitySchemaName)) {
			warnings.Add(
				$"{folderName}: left untouched — the environment did not report which entity schema the package's " +
				$"binding of this name delivers, so this delivery cannot confirm it is the '{expectedSchemaName}' " +
				"folder it would have shipped.");
			return;
		}
		bindingWriter.DeleteBinding(package, folderName);
		logger.WriteInfo($"Removed data binding '{folderName}'.");
		warnings.Add($"{folderName}: previously shipped binding removed, {DescribeRemovalCause(cause)}");
	}

	private static string DescribeRemovalCause(BindingRemovalCause cause) {
		return cause switch {
			BindingRemovalCause.WithdrawnByCaller => "this delivery no longer ships it",
			BindingRemovalCause.SourceRowNotShippable => "the row it would ship must not travel in a package",
			_ => "it no longer has a source row"
		};
	}

	private static bool DeliversConfirmedSchema(PackageDataBindingRef existing, string expectedSchemaName) {
		return string.Equals(existing.EntitySchemaName, expectedSchemaName, StringComparison.OrdinalIgnoreCase);
	}

	private static bool DeliversForeignSchema(PackageDataBindingRef existing, string expectedSchemaName) {
		return !string.IsNullOrWhiteSpace(existing.EntitySchemaName)
			&& !string.Equals(existing.EntitySchemaName, expectedSchemaName, StringComparison.OrdinalIgnoreCase);
	}

	private PackageRef RequirePackage() {
		if (_package is null) {
			throw new InvalidOperationException(
				$"No package was selected before delivering package data. Call {nameof(UsePackage)} first.");
		}
		return _package;
	}

	private static bool IsSecretBearing(SysSettingsDefinition definition) {
		return string.Equals(definition.ValueTypeName, SecureTextValueTypeName, StringComparison.OrdinalIgnoreCase);
	}

	private SysSettingsDefinition FindSysSettingsDefinition(string settingCode) {
		SettingDefinitionResponse response =
			SelectQueryHelper.ExecuteSelectQuery<SettingDefinitionResponse>(
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
				$"System setting '{settingCode}' has multiple definitions on this environment, so the delivery " +
				"cannot tell which one to package.");
		}
		SettingDefinitionDto row = response.Rows.FirstOrDefault();
		return row is not null && Guid.TryParse(row.Id, out Guid definitionId)
			? new SysSettingsDefinition(definitionId, row.ValueTypeName ?? string.Empty)
			: null;
	}

	private string FindAllUsersValueRowId(Guid definitionId) {
		RowIdResponse response = SelectQueryHelper.ExecuteSelectQuery<RowIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				SysSettingsValueSchema,
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(SysSettingsColumn, definitionId.ToString(), SelectQueryHelper.GuidDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition(SysAdminUnitColumn, SysAdminUnitIds.AllEmployees.ToString(), SelectQueryHelper.GuidDataValueType)
				]));
		return SingleRowId(response, $"All-Users value rows of setting '{definitionId}'");
	}

	private Guid? FindFeatureDefinitionId(string featureCode) {
		RowIdResponse response = SelectQueryHelper.ExecuteSelectQuery<RowIdResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				FeatureSchema,
				[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
				[new SelectQueryHelper.SelectQueryFilterDefinition("Code", featureCode, SelectQueryHelper.TextDataValueType)]));
		string id = SingleRowId(response, $"the '{featureCode}' feature definition");
		return id is not null && Guid.TryParse(id, out Guid featureId) ? featureId : null;
	}

	private FeatureStateRow FindAllUsersFeatureStateRow(Guid featureId) {
		FeatureStateResponse response = SelectQueryHelper.ExecuteSelectQuery<FeatureStateResponse>(
			applicationClient, serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(
				AdminUnitFeatureStateSchema,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
					new SelectQueryHelper.SelectQueryColumnDefinition(FeatureStateColumn, FeatureStateColumn)
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(FeatureColumn, featureId.ToString(), SelectQueryHelper.GuidDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition(SysAdminUnitColumn, SysAdminUnitIds.AllEmployees.ToString(), SelectQueryHelper.GuidDataValueType)
				]));
		EnsureAtMostOneRow(response.Rows.Count, $"All-Users feature state of feature '{featureId}'");
		FeatureStateDto row = response.Rows.FirstOrDefault();
		return row is null || string.IsNullOrWhiteSpace(row.Id)
			? null
			: new FeatureStateRow(row.Id, ReadOffState(row.FeatureState));
	}

	private static bool? ReadOffState(JsonElement? featureState) {
		return featureState?.ValueKind switch {
			JsonValueKind.Number => featureState.Value.TryGetInt32(out int state) ? state == 0 : null,
			JsonValueKind.False => true,
			JsonValueKind.True => false,
			JsonValueKind.String => ReadOffStateFromText(featureState.Value.GetString()),
			_ => null
		};
	}

	private static bool? ReadOffStateFromText(string featureState) {
		if (bool.TryParse(featureState, out bool flag)) {
			return !flag;
		}
		return int.TryParse(featureState, NumberStyles.Integer, CultureInfo.InvariantCulture, out int state)
			? state == 0
			: null;
	}

	private static string SingleRowId(RowIdResponse response, string subject) {
		EnsureAtMostOneRow(response.Rows.Count, subject);
		string id = response.Rows.FirstOrDefault()?.Id;
		return string.IsNullOrWhiteSpace(id) ? null : id;
	}

	private static void EnsureAtMostOneRow(int rowCount, string subject) {
		if (rowCount > 1) {
			throw new InvalidOperationException(
				$"Expected at most one match for {subject} on this environment, but found {rowCount}. " +
				"Resolve the duplicates before delivering package data, so the package delivers a row you chose.");
		}
	}

	private enum BindingRemovalCause {
		SourceRowUnavailable,
		SourceRowNotShippable,
		WithdrawnByCaller
	}

	private sealed record SysSettingsDefinition(Guid Id, string ValueTypeName);

	private sealed record FeatureStateRow(string RowId, bool? IsOff);

	private sealed class RowIdResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[System.Text.Json.Serialization.JsonPropertyName("rows")]
		public List<RowIdDto> Rows { get; init; } = [];
	}

	private sealed class RowIdDto {
		[System.Text.Json.Serialization.JsonPropertyName("Id")]
		public string Id { get; init; }
	}

	private sealed class FeatureStateResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[System.Text.Json.Serialization.JsonPropertyName("rows")]
		public List<FeatureStateDto> Rows { get; init; } = [];
	}

	private sealed class FeatureStateDto {
		[System.Text.Json.Serialization.JsonPropertyName("Id")]
		public string Id { get; init; }

		[System.Text.Json.Serialization.JsonPropertyName("FeatureState")]
		public JsonElement? FeatureState { get; init; }
	}

	private sealed class SettingDefinitionResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[System.Text.Json.Serialization.JsonPropertyName("rows")]
		public List<SettingDefinitionDto> Rows { get; init; } = [];
	}

	private sealed class SettingDefinitionDto {
		[System.Text.Json.Serialization.JsonPropertyName("Id")]
		public string Id { get; init; }

		[System.Text.Json.Serialization.JsonPropertyName("ValueTypeName")]
		public string ValueTypeName { get; init; }
	}
}
