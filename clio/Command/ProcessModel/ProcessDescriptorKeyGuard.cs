using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Common;
using Clio.Project.NuGet;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Refuses a process write that carries a key CrtProcessBuilder would drop in silence, before anything is sent.
/// </summary>
/// <remarks>
/// The refusal is conditional on the environment's package, because the key set is the BUNDLED package's:
/// <list type="bullet">
/// <item>environment at or below the bundled version - refused (below is refused earlier, by convergence);</item>
/// <item>environment NEWER than the bundle (a developer build pushed from a package branch) - the key may be
/// one this clio does not know yet, so it is a WARNING and the payload is sent unchanged;</item>
/// <item>a version that cannot be compared - no installed version, an unreadable bundled one, or a bundled one
/// carrying a suffix - a warning, the same "cannot decide, so warn and allow" answer convergence gives.</item>
/// </list>
/// The installed version is read only after an unknown key was found, so a clean call costs no round trip; a
/// failure to reach the environment for it propagates, since the POST that follows would fail the same way.
/// See <c>spec/adr/adr-descriptor-strict-keys.md</c>.
/// </remarks>
public interface IProcessDescriptorKeyGuard {

	/// <summary>
	/// Checks the payload. Throws when the call must be refused; otherwise returns the warning lines to write,
	/// which are empty for a clean payload.
	/// </summary>
	/// <param name="payload">The parsed descriptor object or operations array, exactly as it will be posted.</param>
	/// <param name="kind">Which payload it is.</param>
	/// <returns>Warning lines for unknown keys the environment may accept; empty when there are none.</returns>
	/// <exception cref="InvalidOperationException">An unknown key on an environment not newer than the bundle.</exception>
	IReadOnlyList<string> Enforce(JsonNode payload, ProcessWritePayload kind);
}

/// <inheritdoc cref="IProcessDescriptorKeyGuard" />
public sealed class ProcessDescriptorKeyGuard(
	IProcessDescriptorKeyValidator validator,
	IRequiredPackageChecker packageChecker,
	IBundledPackageCatalog bundledPackageCatalog) : IProcessDescriptorKeyGuard {

	/// <inheritdoc />
	public IReadOnlyList<string> Enforce(JsonNode payload, ProcessWritePayload kind) {
		DescriptorKeyReport report = validator.FindUnknownKeys(payload, kind);
		if (report.Total == 0) {
			return [];
		}
		PackageVersion installed = packageChecker.GetInstalledVersion(BundledPackages.ProcessBuilderPackageName);
		PackageVersion bundled = null;
		bool comparable = installed is not null
			&& bundledPackageCatalog.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out bundled, out _)
			&& bundled is not null
			// A suffixed bundle cannot be ranked honestly: PackageVersion puts an empty suffix BELOW any
			// non-empty one, so the GA the environment records would read as older. Convergence treats the
			// same state as undecidable, and so does this.
			&& string.IsNullOrWhiteSpace(bundled.Suffix);
		if (comparable && !(installed > bundled)) {
			throw new InvalidOperationException(BuildRefusal(report, kind, bundled));
		}
		// Both versions are quoted through SanitizeVersionForDisplay, as every other site does: a version's
		// suffix is free text chosen by whoever installed the package, and it would otherwise reach the agent.
		string reason = comparable
			? $"the environment runs CrtProcessBuilder {TextUtilities.SanitizeVersionForDisplay(installed)}, newer "
				+ $"than the {TextUtilities.SanitizeVersionForDisplay(bundled)} this clio bundles, and may accept it"
			: "clio could not compare the environment's CrtProcessBuilder with the one it bundles";
		IEnumerable<string> lines = report.Listed
			.Select(key => $"'{key.Path}' is not a key this clio's CrtProcessBuilder contract declares; {reason}, "
				+ $"so it will be sent as written. If it is a mistake the server drops it in silence - {key.Hint}.");
		if (report.Total > report.Listed.Count) {
			lines = lines.Append($"{report.Total - report.Listed.Count} more unknown key(s) not listed.");
		}
		return lines.ToList();
	}

	private static string BuildRefusal(DescriptorKeyReport report, ProcessWritePayload kind, PackageVersion bundled) {
		string what = kind == ProcessWritePayload.CreateDescriptor ? "The descriptor carries" : "The operations carry";
		IEnumerable<string> lines = report.Listed.Select(key => $"- {key.Path}: {key.Hint}");
		if (report.Total > report.Listed.Count) {
			lines = lines.Append($"- and {report.Total - report.Listed.Count} more");
		}
		return $"{what} {report.Total} key(s) CrtProcessBuilder {TextUtilities.SanitizeVersionForDisplay(bundled)} "
			+ "does not accept. The server would drop them in silence and still report success, so nothing was "
			+ "sent:" + Environment.NewLine
			+ string.Join(Environment.NewLine, lines) + Environment.NewLine
			+ "Fix the keys and call again.";
	}
}
