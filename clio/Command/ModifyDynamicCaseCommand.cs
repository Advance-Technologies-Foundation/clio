using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Options for changing an existing dynamic case (DCM) in place through the ProcessDesignService package. Consumed by
/// the MCP <c>modify-dynamic-case</c> tool, which sets these properties directly; there is no CLI verb. One of
/// <see cref="CaseName"/>, <see cref="CaseUId"/> and <see cref="EntitySchemaName"/> names the case.
/// </summary>
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, DynamicCaseChangeRequirement.MinimumProcessBuilderVersion,
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class ModifyDynamicCaseOptions : EnvironmentOptions {

	/// <summary>The case's schema name.</summary>
	public string CaseName { get; set; }

	/// <summary>The case's schema UId.</summary>
	public string CaseUId { get; set; }

	/// <summary>The object; the server accepts it only when the object has exactly one case.</summary>
	public string EntitySchemaName { get; set; }

	/// <summary>
	/// The whole desired stage list in order, or <see langword="null"/> to leave the stages as they are. A stored stage
	/// the list leaves out is removed.
	/// </summary>
	public IReadOnlyList<DynamicCaseStageItem> Stages { get; set; }

	/// <summary>The case caption; <see langword="null"/> leaves it.</summary>
	public string Caption { get; set; }

	/// <summary>Disable or enable the case; the server accepts it only with no other change.</summary>
	public bool? Enabled { get; set; }

	/// <summary>The case's new filter; <see langword="null"/> leaves it unless <see cref="RemoveFilter"/>.</summary>
	public DynamicCaseFilterArgument Filter { get; set; }

	/// <summary>Remove the case's filter, so that it starts for every record.</summary>
	public bool RemoveFilter { get; set; }

	/// <summary>The user's yes to changing a case that has running cases.</summary>
	public bool ConfirmRunningInstances { get; set; }

	/// <summary>Run every check and return the planned change without writing anything.</summary>
	public bool DryRun { get; set; }
}

/// <summary>
/// Changes an existing dynamic case and reports the outcome: a one-line verdict, the result document, the notes to
/// relay and the warnings. The exit code is 0 for <c>planned</c>, <c>modified</c> and <c>unchanged</c>, and 1 for
/// everything else.
/// </summary>
public class ModifyDynamicCaseCommand(IDynamicCaseService dynamicCaseService, ILogger logger)
	: Command<ModifyDynamicCaseOptions> {

	private const string NothingWritten = " Nothing was written.";

	private const string NothingWrittenPhrase = "nothing was written";

	private static readonly JsonSerializerOptions ResultJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <inheritdoc />
	public override int Execute(ModifyDynamicCaseOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("environment-name is required and cannot be empty.");
			}
			ModifyDynamicCaseResult result = dynamicCaseService.Modify(options);
			Report(result);
			return result.Success ? 0 : 1;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>The one-line verdict of an outcome, the sentence an agent repeats to the user.</summary>
	/// <param name="result">The result document.</param>
	internal static string Verdict(ModifyDynamicCaseResult result) {
		string name = result.Case?.SchemaName ?? "the case";
		return result.Outcome switch {
			DynamicCaseModifyOutcomes.Planned => $"Dry run: the change to '{name}' is planned as the diff shows."
				+ NothingWritten,
			DynamicCaseModifyOutcomes.Modified => $"Dynamic case '{name}' changed (UId: {result.Case?.SchemaUId}).",
			DynamicCaseModifyOutcomes.Unchanged => $"Dynamic case '{name}' already matches the request; nothing was "
				+ "written.",
			DynamicCaseModifyOutcomes.FailedRestored => $"The change failed [{result.ErrorCode}] and the case was "
				+ $"restored as it was: {result.ErrorMessage}",
			DynamicCaseModifyOutcomes.FailedLeftover => $"The change failed [{result.ErrorCode}] and the case could "
				+ $"not be fully restored: {result.ErrorMessage} Left behind: {string.Join("; ", result.Leftovers ?? [])}.",
			DynamicCaseModifyOutcomes.Unknown => result.ErrorMessage,
			_ => $"Refused [{result.ErrorCode}]: {result.ErrorMessage}"
				+ (SaysNothingWasWritten(result) ? string.Empty : NothingWritten)
		};
	}

	// Some refusals already say it in their own words; repeating it would read as two separate claims.
	private static bool SaysNothingWasWritten(ModifyDynamicCaseResult result) =>
		result.ErrorMessage?.Contains(NothingWrittenPhrase, StringComparison.OrdinalIgnoreCase) == true;

	private void Report(ModifyDynamicCaseResult result) {
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
