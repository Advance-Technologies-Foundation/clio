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
/// <item>either version unreadable - a warning, the same "cannot decide, so warn and allow" answer
/// convergence gives.</item>
/// </list>
/// The installed version is read only after an unknown key was found, so a clean call costs no round trip.
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

	/// <summary>At most this many keys are listed; the rest are counted.</summary>
	internal const int MaxListedKeys = 20;

	/// <inheritdoc />
	public IReadOnlyList<string> Enforce(JsonNode payload, ProcessWritePayload kind) {
		IReadOnlyList<UnknownDescriptorKey> unknown = validator.FindUnknownKeys(payload, kind);
		if (unknown.Count == 0) {
			return [];
		}
		PackageVersion installed = packageChecker.GetInstalledVersion(BundledPackages.ProcessBuilderPackageName);
		bool bundledKnown = bundledPackageCatalog.TryGetVersion(BundledPackages.ProcessBuilderPackageName,
			out PackageVersion bundled, out _);
		if (installed is not null && bundledKnown && !(installed > bundled)) {
			throw new InvalidOperationException(BuildRefusal(unknown, kind, bundled));
		}
		string reason = installed is not null && bundledKnown
			? $"the environment runs CrtProcessBuilder {installed}, newer than the {bundled} this clio bundles, "
				+ "and may accept it"
			: "clio could not compare the environment's CrtProcessBuilder with the one it bundles";
		return unknown.Take(MaxListedKeys)
			.Select(key => $"'{key.Path}' is not a key this clio's CrtProcessBuilder contract declares; {reason}, "
				+ $"so it was sent. If it is a mistake the server drops it in silence - {key.Hint}.")
			.Concat(unknown.Count > MaxListedKeys
				? [$"{unknown.Count - MaxListedKeys} more unknown key(s) were sent unchecked."]
				: [])
			.ToList();
	}

	private static string BuildRefusal(IReadOnlyList<UnknownDescriptorKey> unknown, ProcessWritePayload kind,
			PackageVersion bundled) {
		string what = kind == ProcessWritePayload.CreateDescriptor ? "The descriptor carries" : "The operations carry";
		IEnumerable<string> lines = unknown.Take(MaxListedKeys).Select(key => $"- {key.Path}: {key.Hint}");
		if (unknown.Count > MaxListedKeys) {
			lines = lines.Append($"- and {unknown.Count - MaxListedKeys} more");
		}
		return $"{what} {unknown.Count} key(s) CrtProcessBuilder {bundled} does not accept. The server would "
			+ "drop them in silence and still report success, so nothing was sent:" + Environment.NewLine
			+ string.Join(Environment.NewLine, lines) + Environment.NewLine
			+ "Fix the keys and call again.";
	}
}
