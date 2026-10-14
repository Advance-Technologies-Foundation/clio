using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Common;
using ErrorOr;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Writes the post-write digest of the process-designer write tools (ENG-103023): the <c>summary</c> of the
/// version the write produced, the same shape <c>describe-business-process mode=summary</c> returns, plus what
/// the write changed - so the caller can confirm a write without reading the process back.
/// </summary>
/// <remarks>
/// The digest is reported AFTER the write landed and must never change the outcome the caller sees: every
/// failure here becomes a warning, never an exit code (the same rule the configuration guards follow).
/// </remarks>
internal static class ProcessWriteDigest {

	/// <summary>
	/// The culture every digest read uses: none, so the server resolves captions and flow labels in the profile
	/// culture - the one the write itself stored them in. The configuration guards compare flow labels on this
	/// SAME read, and an explicit en-US read of a label written under another profile culture comes back empty,
	/// which the label guard would report as a dropped label. The checksum still matches a later describe in another
	/// culture for the culture-resolved members clio reports (captions, labels, display values, result captions,
	/// page and email texts), because it leaves them out; a localizable constant inside an element parameter's
	/// <c>value</c> is hashed as read (see <see cref="ProcessDigest.Checksum"/>).
	/// </summary>
	internal const string DigestCulture = null;

	/// <summary>
	/// Leads a digest's <c>versionReadWarning</c>: the version standing was not read within the best-effort budget,
	/// and the rest of the digest still confirms the write.
	/// </summary>
	internal const string VersionStandingNotRead =
		"version standing not read; the summary and changes still confirm the write - no re-describe is needed";

	/// <summary>
	/// The reply line of a create or modify whose post-write read-back was skipped because the written process calls
	/// another process and no configuration guard needed the read (ENG-103165).
	/// </summary>
	internal const string CallerNotReadBack =
		"No summary is returned for a process that calls another process: reading it back now would keep its "
		+ "sub-process elements on the called processes' current parameters until it is saved again. Read it with "
		+ "describe-business-process once the processes it calls are final.";

	/// <summary>
	/// The reply line of a modify whose post-write read-back was skipped because the read before the edit failed,
	/// so whether the process calls another process is unknown, and no configuration guard needed the read
	/// (ENG-103165): a lost summary costs less than a caller pinned to its callees' current parameters.
	/// </summary>
	internal const string PossibleCallerNotReadBack =
		"No summary is returned: the read before the edit failed, so whether this process calls another process is "
		+ "unknown, and reading a process that does back now would keep its sub-process elements on the called "
		+ "processes' current parameters until it is saved again. Read it with describe-business-process to confirm "
		+ "the edit once the processes it calls are final.";

	private const string SubProcessBlockKey = "subProcess";
	private const string SubProcessBuildType = "subprocess";
	private const string RemoveElementOp = "removeElement";

	/// <summary>
	/// Whether a created process calls another process, so its post-write read may be skipped (ENG-103165).
	/// <para>A describe returns the schema instance the server's process manager caches, and the server builds that
	/// instance on the first read: a sub-process element copies the called process's parameters at that moment, and
	/// the instance is dropped only when the process itself is saved again. A read-back right after the write
	/// therefore pins the CALLER to its callees' current parameters, so a parameter a callee gains afterwards is
	/// missing from every later describe of the caller (its element reports <c>inSync: false</c>), where a caller
	/// first read after that change would have been built with it. Before ENG-103023 an unguarded write took no
	/// read; this keeps that for a caller only, and every other write keeps its digest.</para>
	/// </summary>
	/// <param name="descriptorJson">The build descriptor.</param>
	/// <returns><c>true</c> when the descriptor carries a sub-process element.</returns>
	internal static bool DescriptorCallsAnotherProcess(string descriptorJson) =>
		BlockExpectationJson.ElementsCarrying(descriptorJson, SubProcessBlockKey).Count > 0;

	/// <summary>
	/// Whether an edited process may call another process after the edit, so its post-write read may be skipped
	/// (ENG-103165, see <see cref="DescriptorCallsAnotherProcess"/>): the read before the edit shows a sub-process
	/// element the edit does not remove, or the edit configures one through <c>addElement</c> or <c>setElement</c>.
	/// </summary>
	/// <param name="operationsJson">The operations array.</param>
	/// <param name="before">The read taken before the edit, or <c>null</c> when it failed.</param>
	/// <returns><c>true</c> when the edited process may carry a sub-process element.</returns>
	internal static bool EditedProcessCallsAnotherProcess(string operationsJson, DescribeProcessResult before) {
		if (BlockExpectationJson.AddElementTargets(operationsJson, SubProcessBlockKey).Count > 0
				|| BlockExpectationJson.SetElementTargets(operationsJson, SubProcessBlockKey).Count > 0) {
			return true;
		}
		if (before?.Elements is not { Count: > 0 } elements) {
			return false;
		}
		// removeElement accepts the element's name or its UId, resolved the way the guards resolve a target.
		HashSet<DescribedElement> removed = [
			.. BlockExpectationJson.OperationTargets(operationsJson, RemoveElementOp)
				.Select(target => BlockExpectationJson.ResolveElement(before, target))
				.Where(element => element is not null)
		];
		return elements.Exists(element => element is not null && !removed.Contains(element)
			&& (element.SubProcess is not null
				|| string.Equals(element.BuildType, SubProcessBuildType, StringComparison.OrdinalIgnoreCase)));
	}

	/// <summary>Reads a process for the digest, as a best-effort read that never throws.</summary>
	/// <param name="describer">The describer.</param>
	/// <param name="identity">The process to read.</param>
	/// <param name="includeVersionFacts">Whether the read also establishes the version standing.</param>
	/// <returns>The read, or the reason it could not be taken.</returns>
	internal static ErrorOr<DescribeProcessResult> Read(IProcessDescriber describer, ProcessIdentity identity,
		bool includeVersionFacts = false) {
		try {
			// The profile culture, as the guards' read-back always used: see DigestCulture.
			ErrorOr<DescribeProcessResult> read = describer.Describe(identity, DigestCulture, includeVersionFacts,
				bestEffort: true);
			if (read.IsError) {
				return read;
			}
			return read.Value is null ? Error.Failure(description: "the read returned no process") : read;
		} catch (Exception exception) {
			return Error.Failure(description: exception.Message);
		}
	}

	/// <summary>Reads only the version standing of a process, as a best-effort read that never throws.</summary>
	/// <param name="describer">The describer.</param>
	/// <param name="identity">The process to read.</param>
	/// <returns>The version facts, or the reason they could not be read.</returns>
	internal static ErrorOr<DescribeProcessResult> ReadVersionStanding(IProcessDescriber describer,
		ProcessIdentity identity) {
		try {
			ErrorOr<DescribeProcessResult> read = describer.ReadVersionFacts(identity);
			if (read.IsError) {
				return read;
			}
			return read.Value is null ? Error.Failure(description: "the read returned no version standing") : read;
		} catch (Exception exception) {
			return Error.Failure(description: exception.Message);
		}
	}

	/// <summary>
	/// Writes the digest line: <c>{"summary":{...},"changes":{...}}</c>. <paramref name="before"/> null leaves
	/// <c>changes</c> out (a create has nothing to compare with); a failed before-read says so in
	/// <c>changesUnavailable</c> instead of reporting an empty change list.
	/// </summary>
	/// <param name="logger">Where the digest goes.</param>
	/// <param name="after">The read taken after the write.</param>
	/// <param name="before">The read taken before it, or <c>null</c> when there is nothing to compare with.</param>
	/// <param name="versionFactsRead">
	/// Whether <paramref name="after"/> was asked for the version standing. A read that was not carries a
	/// "not requested" warning that says nothing about the process, so the summary leaves it out.
	/// </param>
	/// <param name="changesAgainst">
	/// What <paramref name="before"/> is when it is not the same schema before the edit - the source of a new
	/// version, or the previously active version - so the change list says what it is relative to.
	/// </param>
	/// <param name="readFailureReported">
	/// Whether a configuration guard has already warned that this same read-back failed; the digest then adds no
	/// second warning about it.
	/// </param>
	internal static void Report(ILogger logger, ErrorOr<DescribeProcessResult> after,
		ErrorOr<DescribeProcessResult>? before = null, bool versionFactsRead = false, string changesAgainst = null,
		bool readFailureReported = false) {
		try {
			if (after.IsError) {
				if (readFailureReported) {
					return;
				}
				logger.WriteWarning(
					$"The write landed, but reading the process back for its summary failed: {after.FirstError.Description}. "
					+ "describe-business-process mode=summary confirms the saved state.");
				return;
			}
			// Folded the way describe folds them, so this checksum equals the one a later describe reports.
			DescribeProcessResult saved = DescribeProcessCommand.OmitDecodedFilterPayloads(after.Value);
			if (!versionFactsRead) {
				saved.VersionReadWarning = null;
			} else if (!string.IsNullOrWhiteSpace(saved.VersionReadWarning)) {
				// A best-effort read on a slow stand can miss its short budget. Said so that it does not read as a
				// reason to describe the process again: only the version standing is missing, not the write.
				saved.VersionReadWarning = $"{VersionStandingNotRead} ({saved.VersionReadWarning})";
			}
			JsonObject digest = new() {
				["summary"] = ProcessDigest.BuildSummary(saved, ProcessDigest.Checksum(saved))
			};
			if (before is { } previous) {
				if (!string.IsNullOrWhiteSpace(changesAgainst)) {
					digest["changesAgainst"] = changesAgainst;
				}
				if (previous.IsError) {
					digest["changesUnavailable"] = $"the read before the write failed: {previous.FirstError.Description}";
				} else {
					digest["changes"] = ProcessDigest.BuildChanges(
						DescribeProcessCommand.OmitDecodedFilterPayloads(previous.Value), saved);
				}
			}
			logger.WriteInfo(ProcessDigest.Write(digest));
		} catch (Exception exception) {
			logger.WriteWarning(
				$"The write landed, but its summary could not be built: {exception.Message}. "
				+ "describe-business-process mode=summary confirms the saved state.");
		}
	}
}
