using System;
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

	/// <summary>The culture every digest read uses: the one describe-business-process defaults to.</summary>
	internal const string DigestCulture = "en-US";

	/// <summary>Reads a process for the digest, as a best-effort read that never throws.</summary>
	/// <param name="describer">The describer.</param>
	/// <param name="identity">The process to read.</param>
	/// <param name="includeVersionFacts">Whether the read also establishes the version standing.</param>
	/// <returns>The read, or the reason it could not be taken.</returns>
	internal static ErrorOr<DescribeProcessResult> Read(IProcessDescriber describer, ProcessIdentity identity,
		bool includeVersionFacts = false) {
		try {
			// en-US, describe-business-process's own default, so the digest checksum equals a later describe's.
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
	internal static void Report(ILogger logger, ErrorOr<DescribeProcessResult> after,
		ErrorOr<DescribeProcessResult>? before = null, bool versionFactsRead = false, string changesAgainst = null) {
		try {
			if (after.IsError) {
				logger.WriteWarning(
					$"The write landed, but reading the process back for its summary failed: {after.FirstError.Description}. "
					+ "describe-business-process mode=summary confirms the saved state.");
				return;
			}
			// Folded the way describe folds them, so this checksum equals the one a later describe reports.
			DescribeProcessResult saved = DescribeProcessCommand.OmitDecodedFilterPayloads(after.Value);
			if (!versionFactsRead) {
				saved.VersionReadWarning = null;
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
