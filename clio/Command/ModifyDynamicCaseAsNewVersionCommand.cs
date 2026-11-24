using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Options for saving a change of a dynamic case (DCM) as a NEW, inactive version of it through the
/// ProcessDesignService package. Consumed by the MCP <c>modify-dynamic-case-as-new-version</c> tool, which sets these
/// properties directly; there is no CLI verb. One of <see cref="CaseName"/>, <see cref="CaseUId"/> and
/// <see cref="EntitySchemaName"/> names the family's ACTIVE member, which the version is copied from.
/// </summary>
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, DynamicCaseChangeRequirement.MinimumProcessBuilderVersion,
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class ModifyDynamicCaseAsNewVersionOptions : EnvironmentOptions {

	/// <summary>The active member's schema name.</summary>
	public string CaseName { get; set; }

	/// <summary>The active member's schema UId.</summary>
	public string CaseUId { get; set; }

	/// <summary>The object; the server accepts it only when the object has exactly one case.</summary>
	public string EntitySchemaName { get; set; }

	/// <summary>
	/// The package the version is saved in; <see langword="null"/> for the source's package, which the server accepts
	/// only when that package accepts changes.
	/// </summary>
	public string PackageName { get; set; }

	/// <summary>The version's whole desired stage list in order, or <see langword="null"/> for the source's stages.</summary>
	public IReadOnlyList<DynamicCaseStageItem> Stages { get; set; }

	/// <summary>The version's caption; <see langword="null"/> keeps the source's.</summary>
	public string Caption { get; set; }

	/// <summary>The version's filter; <see langword="null"/> keeps the source's unless <see cref="RemoveFilter"/>.</summary>
	public DynamicCaseFilterArgument Filter { get; set; }

	/// <summary>Remove the filter in the version, so that it would start for every record.</summary>
	public bool RemoveFilter { get; set; }

	/// <summary>The user's yes to a version without a stage that records hold.</summary>
	public bool ConfirmStageRecords { get; set; }

	/// <summary>The user's yes to renaming a stage's lookup value, which acts at once for every record.</summary>
	public bool ConfirmValueRename { get; set; }

	/// <summary>Run every check and return the planned version without writing anything.</summary>
	public bool DryRun { get; set; }
}

/// <summary>
/// Saves a change of a dynamic case as a new, inactive version and reports the outcome: a one-line verdict, the result
/// document, the notes to relay and the warnings. The exit code is 0 for <c>planned</c> and <c>version-created</c>,
/// and 1 for everything else.
/// </summary>
public class ModifyDynamicCaseAsNewVersionCommand(IDynamicCaseService dynamicCaseService, ILogger logger)
	: Command<ModifyDynamicCaseAsNewVersionOptions> {

	private const string NothingWritten = " Nothing was written.";

	private const string NothingWrittenPhrase = "nothing was written";

	private static readonly JsonSerializerOptions ResultJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <inheritdoc />
	public override int Execute(ModifyDynamicCaseAsNewVersionOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("environment-name is required and cannot be empty.");
			}
			ModifyDynamicCaseAsNewVersionResult result = dynamicCaseService.ModifyAsNewVersion(options);
			Report(result);
			return result.Success ? 0 : 1;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>The one-line verdict of an outcome, the sentence an agent repeats to the user.</summary>
	/// <param name="result">The result document.</param>
	internal static string Verdict(ModifyDynamicCaseAsNewVersionResult result) {
		string name = result.Version?.Name ?? "the version";
		string source = result.SourceName ?? "the active case";
		return result.Outcome switch {
			DynamicCaseVersionOutcomes.Planned => $"Dry run: version '{name}' of '{source}' is planned as the diff shows; "
				+ "it would be created inactive." + NothingWritten,
			DynamicCaseVersionOutcomes.VersionCreated => $"Version '{name}' of '{source}' created, INACTIVE (UId: "
				+ $"{result.Version?.UId}): '{source}' stays active and running cases stay on it. Activate the version "
				+ "with set-active-dynamic-case-version only after the user has checked it and said yes.",
			DynamicCaseVersionOutcomes.FailedRolledBack => $"The version was not created [{result.ErrorCode}]; nothing of "
				+ $"it stays: {result.ErrorMessage}",
			DynamicCaseVersionOutcomes.FailedLeftover when result.Version is null => $"No version was saved, but the call "
				+ $"could not undo everything it wrote [{result.ErrorCode}]: {result.ErrorMessage} Left behind: "
				+ $"{string.Join("; ", result.Leftovers ?? [])}. Run describe-dynamic-case before any retry.",
			DynamicCaseVersionOutcomes.FailedLeftover => $"Version '{name}' was saved but the call failed "
				+ $"[{result.ErrorCode}]: {result.ErrorMessage} It stays: {string.Join("; ", result.Leftovers ?? [])}. Do "
				+ "not activate it; run describe-dynamic-case before any retry.",
			DynamicCaseVersionOutcomes.Unknown => result.ErrorMessage,
			_ => $"Refused [{result.ErrorCode}]: {result.ErrorMessage}"
				+ (SaysNothingWasWritten(result) ? string.Empty : NothingWritten)
		};
	}

	// Some refusals already say it in their own words; repeating it would read as two separate claims.
	private static bool SaysNothingWasWritten(ModifyDynamicCaseAsNewVersionResult result) =>
		result.ErrorMessage?.Contains(NothingWrittenPhrase, StringComparison.OrdinalIgnoreCase) == true;

	private void Report(ModifyDynamicCaseAsNewVersionResult result) {
		string verdict = Verdict(result);
		if (result.Success) {
			logger.WriteInfo(verdict);
		} else {
			logger.WriteError(verdict);
		}
		logger.WriteInfo(JsonSerializer.Serialize(result, ResultJsonOptions));
		foreach (string note in (result.Notes ?? []).Where(note => !string.IsNullOrWhiteSpace(note))) {
			logger.WriteInfo(note);
		}
		foreach (string warning in (result.Warnings ?? []).Where(warning => !string.IsNullOrWhiteSpace(warning))) {
			logger.WriteWarning(warning);
		}
	}
}
