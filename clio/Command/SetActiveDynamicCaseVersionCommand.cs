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

	/// <summary>The user's yes to a switch that a member in a package that does not accept changes may outrank;
	/// without it such a switch is refused <c>package-position-confirmation-required</c>.</summary>
	public bool ConfirmPackagePosition { get; set; }
}

/// <summary>
/// Makes a member of a dynamic case's version family the active one and reports the outcome in two lines: a one-line
/// verdict that also counts the warnings, then the result document, whose <c>notes</c> and <c>warnings</c> are the one
/// copy of what to relay. The exit code is 0 for <c>planned</c>, <c>activated</c> and <c>unchanged</c>, and 1 for
/// everything else.
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
		// The documented repeat after failed-not-restored: the target is already the platform's pick and the call only
		// clears the flags other members still carry, so "instead of" / "was" would name the target as its own
		// predecessor.
		bool staysActive = previous is not null && IsSameMember(result.ActiveVersion, result.PreviousActiveVersion);
		bool warned = result.Warnings?.Any(warning => !string.IsNullOrWhiteSpace(warning)) == true;
		return result.Outcome switch {
			DynamicCaseActivationOutcomes.Planned when staysActive => $"Dry run: '{active}' would stay the active "
				+ "version, and the switch would clear the active flag the other editable members still carry"
				+ (warned ? ", unless a warning below stands in the way." : ".") + NothingWritten,
			DynamicCaseActivationOutcomes.Planned => $"Dry run: '{active}' would become the active version"
				+ (previous is null ? string.Empty : $" instead of '{previous}'")
				+ (warned ? " unless a warning below stands in the way." : ".") + NothingWritten,
			DynamicCaseActivationOutcomes.Activated when staysActive => $"'{active}' stays the active version of the "
				+ "case, and the switch cleared the active flag the other editable members still carried; the notes say what that "
				+ "means for the bar, new records and running cases.",
			DynamicCaseActivationOutcomes.Activated => $"'{active}' is now the active version of the case"
				+ (previous is null ? "" : $" (was '{previous}')") + "; the notes say what that means for the bar, new "
				+ "records and running cases.",
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

	private static bool IsSameMember(DynamicCaseVersion first, DynamicCaseVersion second) =>
		first is not null && second is not null
		&& (!string.IsNullOrWhiteSpace(first.UId) && !string.IsNullOrWhiteSpace(second.UId)
			? string.Equals(first.UId, second.UId, StringComparison.OrdinalIgnoreCase)
			: string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase));

	// Some refusals already say it in their own words; repeating it would read as two separate claims.
	private static bool SaysNothingWasWritten(SetActiveDynamicCaseVersionResult result) =>
		result.ErrorMessage?.Contains(NothingWrittenPhrase, StringComparison.OrdinalIgnoreCase) == true;

	private void Report(SetActiveDynamicCaseVersionResult result) {
		string verdict = Verdict(result) + DynamicCaseWarningsHint.For(result.Warnings, beforeAnyWrite: true);
		if (result.Success) {
			logger.WriteInfo(verdict);
		} else {
			logger.WriteError(verdict);
		}
		logger.WriteInfo(JsonSerializer.Serialize(result, ResultJsonOptions));
	}
}
