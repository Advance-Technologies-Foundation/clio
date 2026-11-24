using System;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Options for making one member of a dynamic case's version family the active one through the ProcessDesignService
/// package. Consumed by the MCP <c>set-active-dynamic-case-version</c> tool, which sets these properties directly;
/// there is no CLI verb. Exactly one of <see cref="VersionName"/> and <see cref="VersionUId"/> names the member.
/// </summary>
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, DynamicCaseChangeRequirement.MinimumProcessBuilderVersion,
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class SetActiveDynamicCaseVersionOptions : EnvironmentOptions {

	/// <summary>The member to activate, by schema name; the family root included.</summary>
	public string VersionName { get; set; }

	/// <summary>The member to activate, by schema UId; the family root included.</summary>
	public string VersionUId { get; set; }

	/// <summary>Run every check and return the planned switch without writing anything.</summary>
	public bool DryRun { get; set; }
}

/// <summary>
/// Makes a member of a dynamic case's version family the active one and reports the outcome: a one-line verdict, the
/// result document, the notes to relay and the warnings. The exit code is 0 for <c>planned</c>, <c>activated</c> and
/// <c>unchanged</c>, and 1 for everything else.
/// </summary>
public class SetActiveDynamicCaseVersionCommand(IDynamicCaseService dynamicCaseService, ILogger logger)
	: Command<SetActiveDynamicCaseVersionOptions> {

	private const string NothingWritten = " Nothing was written.";

	private const string NothingWrittenPhrase = "nothing was written";

	private static readonly JsonSerializerOptions ResultJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <inheritdoc />
	public override int Execute(SetActiveDynamicCaseVersionOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("environment-name is required and cannot be empty.");
			}
			SetActiveDynamicCaseVersionResult result = dynamicCaseService.SetActiveVersion(options);
			Report(result);
			return result.Success ? 0 : 1;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>The one-line verdict of an outcome, the sentence an agent repeats to the user.</summary>
	/// <param name="result">The result document.</param>
	internal static string Verdict(SetActiveDynamicCaseVersionResult result) {
		string active = result.ActiveVersion?.Name ?? "the version";
		string previous = result.PreviousActiveVersion?.Name;
		return result.Outcome switch {
			DynamicCaseActivationOutcomes.Planned => $"Dry run: '{active}' would become the active version"
				+ (previous is null ? "." : $" instead of '{previous}'.") + NothingWritten,
			DynamicCaseActivationOutcomes.Activated => $"'{active}' is now the active version of the case"
				+ (previous is null ? "" : $" (was '{previous}')") + ": the bar and new records use it; running cases "
				+ "stay on the version they started on.",
			DynamicCaseActivationOutcomes.Unchanged => $"'{active}' is already the active version; nothing was written.",
			DynamicCaseActivationOutcomes.FailedNotRestored => $"The switch did not read back as asked [{result.ErrorCode}]: "
				+ $"{result.ErrorMessage} "
				+ (result.ActiveVersion is null
					? "The family has no active version now. "
					: $"The active version is '{active}'. ")
				+ "Nothing was undone; the same call can be repeated once the cause is fixed.",
			DynamicCaseActivationOutcomes.FailedLeftover => $"No switch [{result.ErrorCode}]: {result.ErrorMessage} "
				+ $"Left behind: {string.Join("; ", result.Leftovers ?? [])}.",
			DynamicCaseActivationOutcomes.Unknown => result.ErrorMessage,
			_ => $"Refused [{result.ErrorCode}]: {result.ErrorMessage}"
				+ (SaysNothingWasWritten(result) ? string.Empty : NothingWritten)
		};
	}

	// Some refusals already say it in their own words; repeating it would read as two separate claims.
	private static bool SaysNothingWasWritten(SetActiveDynamicCaseVersionResult result) =>
		result.ErrorMessage?.Contains(NothingWrittenPhrase, StringComparison.OrdinalIgnoreCase) == true;

	private void Report(SetActiveDynamicCaseVersionResult result) {
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
