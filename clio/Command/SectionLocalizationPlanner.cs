using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// What a section update wrote or is about to write, as the input of a localization plan.
/// </summary>
/// <param name="Snapshot">The section's localization rows read BEFORE any write.</param>
/// <param name="SectionUpdateRan">Whether the <c>ApplicationSection</c> update ran; it deletes every snapshot row
/// (ADR F11), so every snapshot value it did not write itself must be written back.</param>
/// <param name="ProfileCulture">The connected user's culture.</param>
/// <param name="TargetCulture">The culture the caption is written in.</param>
/// <param name="LocalizedCaption">The caption to write through the localization table (a non-profile target
/// culture), trimmed; <see langword="null"/> when no caption goes through that path.</param>
/// <param name="CaptionThroughSection">Whether a caption was actually sent through the <c>ApplicationSection</c>
/// update. Only then did that update write the profile-culture caption; an icon-only update did not.</param>
/// <param name="CurrentProfileCaption">The profile-culture caption after the update; a <c>Caption</c> write must
/// carry it (ADR F12).</param>
/// <param name="DescriptionThroughSection">Whether a description was sent through the <c>ApplicationSection</c>
/// update, which writes it in the profile culture.</param>
public sealed record SectionLocalizationPlanInput(
	IReadOnlyList<SectionLocalizationRow> Snapshot,
	bool SectionUpdateRan,
	string ProfileCulture,
	string TargetCulture,
	string? LocalizedCaption,
	bool CaptionThroughSection,
	string CurrentProfileCaption,
	bool DescriptionThroughSection);

/// <summary>
/// One localized value the readback must find after the write.
/// </summary>
/// <param name="Column">Localizable column: <c>Caption</c>, <c>Description</c> or <c>ModuleHeader</c>.</param>
/// <param name="CultureName">Culture of the value.</param>
/// <param name="Value">Expected stored value.</param>
/// <param name="Verify">Whether the readback checks this value; <see langword="false"/> for a value another path
/// owns (the <c>ApplicationSection</c> update, or the default culture that lives in <c>SysModule</c> itself).</param>
/// <param name="IsTarget">Whether this is the requested caption rather than a kept value.</param>
public sealed record ExpectedLocalizationCell(
	string Column,
	string CultureName,
	string Value,
	bool Verify = true,
	bool IsTarget = false);

/// <summary>
/// The localization write of one section update and what its readback must find.
/// </summary>
/// <param name="ColumnValues">Column → culture → value to send; empty when <paramref name="HasWrites"/> is false.</param>
/// <param name="ExpectedCells">Values the readback must find.</param>
/// <param name="PreservedCultures">Non-default cultures whose values were kept or written back.</param>
/// <param name="HasWrites">Whether a localization write is needed at all.</param>
/// <param name="Snapshot">The rows read before any write; quoted in the error when writing them back fails.</param>
/// <param name="RestoresDeletedValues">Whether the write carries values the <c>ApplicationSection</c> update has
/// already deleted, so a failed write loses them unless the caller re-sends them.</param>
public sealed record SectionLocalizationPlan(
	IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ColumnValues,
	IReadOnlyList<ExpectedLocalizationCell> ExpectedCells,
	IReadOnlyList<string> PreservedCultures,
	bool HasWrites,
	IReadOnlyList<SectionLocalizationRow> Snapshot,
	bool RestoresDeletedValues);

/// <summary>
/// Plans, writes and verifies the per-culture values of a section's title, description and module header around
/// the platform's <c>ApplicationSection</c> update, which deletes every non-default culture (ADR D11, F11).
/// </summary>
public interface ISectionLocalizationPlanner {

	/// <summary>
	/// Builds the localization write: when the <c>ApplicationSection</c> update ran, every snapshot value except the
	/// ones that update has just written; plus the caption in the target culture when it is not the profile
	/// culture. A <c>Caption</c> map always carries the profile-culture caption the platform requires (F12).
	/// </summary>
	/// <param name="input">What the update wrote and the snapshot taken before it.</param>
	/// <returns>The write and the values its readback must find.</returns>
	SectionLocalizationPlan BuildPlan(SectionLocalizationPlanInput input);

	/// <summary>
	/// Sends the plan's write, then re-saves the application package's <c>SysModule_&lt;SectionCode&gt;</c> binding
	/// (F13). Does nothing when the plan has no writes. A missing binding or a failed re-save becomes a warning.
	/// </summary>
	/// <param name="client">Authenticated client of the target environment.</param>
	/// <param name="environmentSettings">Target environment.</param>
	/// <param name="sectionId">Section identifier.</param>
	/// <param name="packageUId">UId of the application's primary package.</param>
	/// <param name="sectionCode">Section code.</param>
	/// <param name="plan">The plan built by <see cref="BuildPlan"/>.</param>
	/// <param name="warnings">Receives the non-fatal findings.</param>
	/// <exception cref="InvalidOperationException">The write failed. When the plan restores values the
	/// <c>ApplicationSection</c> update deleted, the message lists every snapshot value (culture → caption,
	/// description, module header), because it is the only remaining copy.</exception>
	void Apply(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string sectionId,
		string packageUId,
		string sectionCode,
		SectionLocalizationPlan plan,
		List<string> warnings);

	/// <summary>
	/// Checks the readback against every value the plan marked for verification.
	/// </summary>
	/// <param name="plan">The plan built by <see cref="BuildPlan"/>.</param>
	/// <param name="storedLocalizations">The localization rows read after the write.</param>
	/// <exception cref="InvalidOperationException">A value is missing or differs; the message names each
	/// column and culture.</exception>
	void Verify(SectionLocalizationPlan plan, IReadOnlyList<SectionLocalizationRow> storedLocalizations);
}

/// <inheritdoc />
public sealed class SectionLocalizationPlanner(IApplicationSectionLocalizationClient sectionLocalizationClient)
	: ISectionLocalizationPlanner {

	/// <summary>Section title column.</summary>
	internal const string CaptionColumn = "Caption";

	/// <summary>Section description column.</summary>
	internal const string DescriptionColumn = "Description";

	/// <summary>Section module header column.</summary>
	internal const string ModuleHeaderColumn = "ModuleHeader";

	/// <inheritdoc />
	public SectionLocalizationPlan BuildPlan(SectionLocalizationPlanInput input) {
		ArgumentNullException.ThrowIfNull(input);
		Dictionary<string, Dictionary<string, string>> columns = new(StringComparer.Ordinal);
		List<ExpectedLocalizationCell> expected = [];
		foreach (SectionLocalizationRow row in input.Snapshot) {
			bool isProfileCulture = SameCulture(row.CultureName, input.ProfileCulture);
			bool isTargetCulture = input.LocalizedCaption is not null && SameCulture(row.CultureName, input.TargetCulture);
			bool captionWrittenBySection = isProfileCulture && input.CaptionThroughSection;
			bool descriptionWrittenBySection = isProfileCulture && input.DescriptionThroughSection;
			AddSnapshotCell(columns, expected, input.SectionUpdateRan, CaptionColumn, row.CultureName, row.Caption,
				skip: isTargetCulture || captionWrittenBySection);
			AddSnapshotCell(columns, expected, input.SectionUpdateRan, DescriptionColumn, row.CultureName, row.Description,
				skip: descriptionWrittenBySection);
			AddSnapshotCell(columns, expected, input.SectionUpdateRan, ModuleHeaderColumn, row.CultureName, row.ModuleHeader,
				skip: false);
		}

		// Computed before the requested caption is added: only snapshot values were deleted by the update.
		bool restoresDeletedValues = input.SectionUpdateRan && columns.Count > 0;
		if (input.LocalizedCaption is not null) {
			GetColumn(columns, CaptionColumn)[input.TargetCulture] = input.LocalizedCaption;
			// The default culture lives in SysModule itself, not in the localization rows the readback sees (F10), so
			// it cannot be verified there; the service reports that case as a warning.
			bool targetIsInLocalizationRows =
				!SameCulture(input.TargetCulture, EntitySchemaDesignerSupport.DefaultCultureName);
			expected.Add(new ExpectedLocalizationCell(
				CaptionColumn, input.TargetCulture, input.LocalizedCaption, Verify: targetIsInLocalizationRows, IsTarget: true));
		}

		if (columns.TryGetValue(CaptionColumn, out Dictionary<string, string>? captionMap)
			&& !captionMap.Keys.Any(culture => SameCulture(culture, input.ProfileCulture))) {
			captionMap[input.ProfileCulture] = input.CurrentProfileCaption;
		}

		bool hasWrites = input.LocalizedCaption is not null || (input.SectionUpdateRan && columns.Count > 0);
		List<string> preservedCultures = expected
			.Where(cell => cell.Verify && !cell.IsTarget)
			.Select(cell => cell.CultureName)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(culture => culture, StringComparer.OrdinalIgnoreCase)
			.ToList();
		return new SectionLocalizationPlan(
			hasWrites
				? columns.ToDictionary(
					column => column.Key,
					column => (IReadOnlyDictionary<string, string>)column.Value,
					StringComparer.Ordinal)
				: new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal),
			expected,
			preservedCultures,
			hasWrites,
			input.Snapshot,
			restoresDeletedValues);
	}

	/// <inheritdoc />
	public void Apply(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string sectionId,
		string packageUId,
		string sectionCode,
		SectionLocalizationPlan plan,
		List<string> warnings) {
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(warnings);
		if (!plan.HasWrites) {
			return;
		}

		try {
			sectionLocalizationClient.WriteLocalizations(client, environmentSettings, sectionId, plan.ColumnValues);
		} catch (Exception exception) when (plan.RestoresDeletedValues && exception is not OperationCanceledException) {
			// F11: the ApplicationSection update has already deleted these rows; this snapshot is the only copy left.
			throw new InvalidOperationException(
				"The section was updated, but writing back its values in other cultures failed: "
				+ $"{exception.Message} The platform deleted them during the update. Re-send each value with "
				+ $"update-app-section caption + caption-culture, or enter it in Creatio. Values before the update: "
				+ $"{DescribeSnapshot(plan.Snapshot)}.",
				exception);
		}

		TryRefreshPackageBinding(client, environmentSettings, packageUId, sectionCode, warnings);
	}

	/// <inheritdoc />
	public void Verify(SectionLocalizationPlan plan, IReadOnlyList<SectionLocalizationRow> storedLocalizations) {
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(storedLocalizations);
		List<string> mismatches = plan.ExpectedCells
			.Where(cell => cell.Verify)
			.Where(cell => !string.Equals(ReadCell(storedLocalizations, cell.Column, cell.CultureName), cell.Value,
				StringComparison.Ordinal))
			.Select(cell => $"{cell.Column} [{cell.CultureName}]")
			.ToList();
		if (mismatches.Count > 0) {
			throw new InvalidOperationException(
				"The section was saved, but these localized values are not stored as expected: " +
				$"{string.Join(", ", mismatches)}. Check the culture in the Languages section and the section in " +
				"Creatio, then retry.");
		}
	}

	/// <summary>
	/// Reads one column of one culture from localization rows.
	/// </summary>
	/// <param name="rows">Localization rows.</param>
	/// <param name="column">Column name: <c>Caption</c>, <c>Description</c> or <c>ModuleHeader</c>.</param>
	/// <param name="culture">Culture name, compared case-insensitively.</param>
	/// <returns>The value, or <see langword="null"/> when the culture has no row.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="column"/> is not a localizable column.</exception>
	internal static string? ReadCell(IReadOnlyList<SectionLocalizationRow> rows, string column, string culture) {
		SectionLocalizationRow? row = rows.FirstOrDefault(item => SameCulture(item.CultureName, culture));
		if (row is null) {
			return null;
		}

		return column switch {
			CaptionColumn => row.Caption,
			DescriptionColumn => row.Description,
			ModuleHeaderColumn => row.ModuleHeader,
			_ => throw new ArgumentOutOfRangeException(nameof(column), column, "Not a localizable section column.")
		};
	}

	private void TryRefreshPackageBinding(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string packageUId,
		string sectionCode,
		List<string> warnings) {
		// F13: a direct localization write leaves the application package's SysModule data snapshot stale; re-saving
		// the binding unchanged re-reads every culture. The values themselves are already stored, so a failure here
		// is reported, not thrown.
		try {
			if (!sectionLocalizationClient.RefreshSectionPackageBinding(client, environmentSettings, packageUId, sectionCode)) {
				warnings.Add(
					$"Package data binding 'SysModule_{sectionCode}' was not found in the application package; the " +
					"translated section title is stored in the environment but is not part of the package data.");
			}
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			// The warning goes out in a SUCCESS response, which no caller redacts; bound and redact the server text.
			warnings.Add(
				$"The translated section title is stored, but re-saving package data binding 'SysModule_{sectionCode}' " +
				$"failed: {ApplicationSectionLocalizationClient.DescribeServerText(exception.Message)}");
		}
	}

	private static void AddSnapshotCell(
		Dictionary<string, Dictionary<string, string>> columns,
		List<ExpectedLocalizationCell> expected,
		bool sectionUpdateRan,
		string column,
		string culture,
		string? value,
		bool skip) {
		if (string.IsNullOrEmpty(value)) {
			return;
		}

		expected.Add(new ExpectedLocalizationCell(column, culture, value, Verify: !skip));
		if (!skip && sectionUpdateRan) {
			GetColumn(columns, column)[culture] = value;
		}
	}

	private static Dictionary<string, string> GetColumn(
		Dictionary<string, Dictionary<string, string>> columns,
		string column) {
		if (!columns.TryGetValue(column, out Dictionary<string, string>? map)) {
			map = new Dictionary<string, string>(StringComparer.Ordinal);
			columns[column] = map;
		}

		return map;
	}

	private static string DescribeSnapshot(IReadOnlyList<SectionLocalizationRow> snapshot) =>
		string.Join("; ", snapshot.Select(row =>
			$"{row.CultureName}: {CaptionColumn}={Quote(row.Caption)}, {DescriptionColumn}={Quote(row.Description)}, "
			+ $"{ModuleHeaderColumn}={Quote(row.ModuleHeader)}"));

	private static string Quote(string? value) => value is null ? "(none)" : $"'{value}'";

	private static bool SameCulture(string left, string right) =>
		string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
