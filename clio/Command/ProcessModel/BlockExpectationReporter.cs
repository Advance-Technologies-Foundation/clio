using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Command.ProcessModel;

/// <summary>
/// The warning-EMISSION half of the post-operation block guards, shared by <c>create-business-process</c> and
/// <c>modify-business-process</c>.
/// <para><see cref="BlockExpectationJson"/> keeps the payload-reading mechanics; this keeps the reporting. Both
/// commands read the saved process back once and then report the SAME four outcomes in the same words — an
/// element the read-back cannot resolve, a missing record filter, a dropped accessRights block and a dropped
/// email block — and both need the same "could not verify" wording when the read-back itself fails. Only how
/// each command IDENTIFIES the process differs, and that stays in the command.</para>
/// <para>Sharing it is not only de-duplication: these messages are the sole automated evidence that a grant or
/// revoke landed, so two copies drifting apart would mean the same state described two different ways depending
/// on which command the caller happened to use.</para>
/// </summary>
internal static class BlockExpectationReporter {

	/// <summary>
	/// Emits every warning a successful read-back justifies, in the order a caller should read them: what could
	/// not be checked, then what was configured too widely, then what was dropped outright.
	/// </summary>
	internal static void ReportDescribed(ILogger logger, DescribeProcessResult described,
			IReadOnlyList<string> expectedRights, IReadOnlyList<string> expectedEmail,
			IReadOnlyList<string>? filterTouched = null) {
		// An element the batch only RE-FILTERED is just as unverifiable when the read-back cannot find it, so it
		// must be reported - but in its OWN words. It sent no block, and an element the read-back could not
		// resolve is precisely one whose type is unknown, so the accessRights wording would be a false
		// accusation for the readData/changeData elements that share the clearFilter operation.
		IReadOnlyList<string> filterOnly =
			[.. (filterTouched ?? []).Except(expectedRights, StringComparer.OrdinalIgnoreCase)];
		const string unresolved = "the saved process does not report an element with that name or UId";
		Warn(logger, BuildUnverifiedWarning(
			AccessRightsBlockExpectation.Unresolved(described, expectedRights), unresolved));
		Warn(logger, BuildUnverifiedFilterWarning(
			AccessRightsBlockExpectation.Unresolved(described, filterOnly), unresolved));
		// A re-filtered element reaches no other check here: it is deliberately excluded from Missing() and from
		// the lossy-read check, both of which speak for blocks the caller SENT. So when the read-back resolves it
		// but reports no accessRights block at all - which is every environment whose CrtProcessBuilder predates
		// the element, admitted today because the rebundle is deferred - it would otherwise pass in silence.
		Warn(logger, AccessRightsBlockExpectation.BuildUnreportableFilterWarning(described, filterTouched));
		Warn(logger, AccessRightsBlockExpectation.BuildLossyReadWarning(described, expectedRights));
		// The filter-state check covers elements this batch RE-FILTERED as well as those it configured: a
		// setFilter/clearFilter carries no block, so every other check here skips it, yet clearing a filter is
		// what moves an element from narrowing to acting on every record. Only this check gets the wider list -
		// the others would accuse a payload that never sent a block.
		Warn(logger, AccessRightsBlockExpectation.BuildNoFilterWarning(described,
			filterTouched is null or { Count: 0 }
				? expectedRights
				: [.. expectedRights.Concat(filterTouched).Distinct(StringComparer.OrdinalIgnoreCase)]));
		Warn(logger, AccessRightsBlockExpectation.BuildWarning(
			AccessRightsBlockExpectation.Missing(described, expectedRights)));
		Warn(logger, EmailBlockExpectation.BuildWarning(
			EmailBlockExpectation.Missing(described, expectedEmail)));
	}

	/// <summary>
	/// The access-rights guard must not report "could not check" the same way it reports "verified": it is the
	/// only automated evidence that a grant or revoke landed, on an element with no output parameters. The
	/// command still succeeds — an unreadable description is not evidence of a drop — but the caller is told the
	/// verification did not happen, so an unapplied revoke cannot pass as an applied one.
	/// </summary>
	internal static void WarnAccessRightsUnverified(ILogger logger, IReadOnlyList<string> expectedRights,
			string reason, IReadOnlyList<string>? filterTouched = null) {
		Warn(logger, BuildUnverifiedWarning(expectedRights, reason));
		Warn(logger, BuildUnverifiedFilterWarning(
			[.. (filterTouched ?? []).Except(expectedRights, StringComparer.OrdinalIgnoreCase)], reason));
	}

	// An element this batch only RE-FILTERED gets its own wording. It sent no block, and with the read-back
	// unavailable the command cannot know its element type - clearFilter is equally legal on readData and
	// changeData. Calling that "the 'accessRights' configuration" would assert something false about the
	// commonest case; dropping it would lose the one case that matters. So: report the filter, and let the
	// consequence be conditional on what the element turns out to be.
	private static string? BuildUnverifiedFilterWarning(IReadOnlyList<string> names, string reason) {
		if (names.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", names);
		string subject = names.Count == 1 ? "element" : "elements";
		return $"Could not read back the record filter this edit changed on the {subject} '{elements}': "
			+ $"{reason}. The operation itself succeeded. If any of those is a Change access rights element, "
			+ "note that CLEARING its record filter makes it apply the permission change to EVERY record of "
			+ "its object — re-read the process with describe-business-process before reporting the change as "
			+ "applied.";
	}

	// One wording for every "the check did not happen" outcome, so they cannot drift apart.
	private static string? BuildUnverifiedWarning(IReadOnlyList<string> expectedRights, string reason) {
		if (expectedRights.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", expectedRights);
		string subject = expectedRights.Count == 1 ? "element" : "elements";
		return $"Could not verify that the 'accessRights' configuration for the {subject} '{elements}' landed: "
			+ $"{reason}. The operation itself succeeded, but this check is the only signal that the permissions "
			+ "were actually written — the element has no output parameters. Re-read the process with "
			+ "describe-business-process before reporting a grant or revoke as applied.";
	}

	private static void Warn(ILogger logger, string? warning) {
		if (warning is not null) {
			logger.WriteWarning(warning);
		}
	}
}
