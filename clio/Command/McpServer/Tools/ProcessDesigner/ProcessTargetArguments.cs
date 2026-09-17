using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// The argument preamble shared by the two tools that apply an operation array to an EXISTING process
/// (<c>modify-business-process</c> and <c>modify-business-process-as-new-version</c>): the unknown-key
/// guard, the environment-name requirement, and the process-name / process-uid exclusivity rule.
/// </summary>
/// <remarks>
/// <para>
/// Extracted under ENG-98566. The two call sites had drifted into a byte-identical 28-line run once the
/// unknown-argument guard was added to both, which SonarCloud's duplication detector counts (it includes
/// the comment block). The detector is the messenger, not the reason: the two tools really do share one
/// rule about how a process is addressed, and it had been stated twice.
/// </para>
/// <para>
/// This returns a fully-built <see cref="CommandExecutionResult"/> rather than a message, so each check
/// keeps the EXACT exit code it had before extraction - <c>FromValidationError</c> (1) for the
/// unknown-argument refusal, <c>FromError</c> (-1) for the two older checks. Those two are arguably
/// miscategorised, since both are ordinary argument validation and -1 means "clio itself broke"; that is a
/// deliberate non-change here, because correcting it alters behaviour pinned by existing fixtures and is
/// wider than the ticket that prompted this extraction. It is tracked as ENG-99100, which also records that
/// <c>describe-business-process</c> already answers 1 for the same check - so the family is split until that
/// ticket lands, and the split is known rather than accidental.
/// </para>
/// <para>
/// The null-<c>args</c> check deliberately stays at each call site rather than moving here: it has to
/// happen before the caller can read any field to pass in, and keeping it inline is what lets the
/// analyser prove no field read is reached with a null argument object (SonarCloud
/// <c>csharpsquid:S2259</c>).
/// </para>
/// </remarks>
internal static class ProcessTargetArguments {

	/// <summary>The message for a call naming neither way of addressing the process.</summary>
	internal const string MissingIdentityError = "one of process-name or process-uid is required.";

	/// <summary>The message for a call naming both ways of addressing the process.</summary>
	internal const string AmbiguousIdentityError = "Provide only one of process-name or process-uid, not both.";

	/// <summary>The message for a call that names no environment.</summary>
	internal const string MissingEnvironmentError = "environment-name is required and cannot be empty.";

	/// <summary>
	/// Validates the shared preamble and returns the refusal to hand straight back to the caller, or
	/// <see langword="null"/> when nothing is wrong - the same "null means fine" convention
	/// <see cref="McpToolArgumentSupport.BuildLegacyAliasError"/> uses.
	/// </summary>
	/// <param name="extensionData">The args record's <c>[JsonExtensionData]</c> overflow bag.</param>
	/// <param name="validArgsHint">The calling tool's canonical field list, echoed on an unknown key.</param>
	/// <param name="environmentName">The requested environment.</param>
	/// <param name="processName">The process schema code, when the caller addressed it by name.</param>
	/// <param name="processUid">The process schema UId, when the caller addressed it by uid.</param>
	internal static CommandExecutionResult Validate(
			IReadOnlyDictionary<string, JsonElement> extensionData,
			string validArgsHint,
			string environmentName,
			string processName,
			string processUid) {
		// The only unknown-key defence these two tools have; the helper's docs say why. ENG-98566.
		string argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(
			extensionData, validArgsHint);
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return CommandExecutionResult.FromValidationError(argumentError);
		}

		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError(MissingEnvironmentError);
		}

		bool hasName = !string.IsNullOrWhiteSpace(processName);
		bool hasUid = !string.IsNullOrWhiteSpace(processUid);
		if (hasName == hasUid) {
			return CommandExecutionResult.FromError(hasName ? AmbiguousIdentityError : MissingIdentityError);
		}

		return null;
	}
}
