using System;
using System.Collections.Generic;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// The one place an MCP page-write tool asks for the resource keys already persisted on a target
/// schema. Both write tools reach the read the same way — resolve <see cref="PageUpdateCommand"/> for
/// the target environment, run its read, and turn any resolution failure into a recorded warning
/// rather than an exception — and two hand-written copies of that had already started to differ in
/// which exception types they let through.
/// </summary>
internal static class McpPersistedResourceKeyGate {

	/// <summary>
	/// Reads (or replays, inside the current scope) the keys persisted on the schema
	/// <paramref name="options"/> targets.
	/// </summary>
	/// <param name="reader">The owning module; it decides whether an actual read is needed.</param>
	/// <param name="logger">Diagnostic trail for a failed read; may be <see langword="null"/>.</param>
	/// <param name="options">The pending write request identifying the environment and schema.</param>
	/// <param name="resolveCommand">
	/// Resolves the environment-scoped <see cref="PageUpdateCommand"/>. Only this can fail here — the
	/// read itself reports its own reason inside the command.
	/// </param>
	/// <returns>The persisted keys, or an EMPTY set when the read failed.</returns>
	/// <remarks>
	/// Failing closed leaves the stricter verdict standing, which is the safe direction; but the reason
	/// must stay observable rather than surfacing as a misleading "resource is not registered". A
	/// cancellation is rethrown: it is the caller giving up, not a failed read.
	/// </remarks>
	public static IReadOnlySet<string> ReadKeys(
		IPersistedResourceKeyReader reader,
		ILogger logger,
		PageUpdateOptions options,
		Func<PageUpdateCommand> resolveCommand) =>
		reader.Read(options, () => {
			try {
				return resolveCommand().ReadPersistedResourceKeys(options);
			} catch (OperationCanceledException) {
				throw;
			} catch (Exception ex) {
				PersistedResourceKeyRead failure = PersistedResourceKeyRead.Failure(ex.Message);
				logger?.WriteWarning(failure.FailureWarning);
				return failure;
			}
		}).Keys;

	/// <summary>
	/// Builds the lazy provider the label-resource validators take. The delegate is invoked ONLY for an
	/// unresolved label-resource rejection, so a clean body never pays the round trip.
	/// </summary>
	/// <returns>A provider yielding the persisted keys, or an empty set when the read failed.</returns>
	public static Func<IReadOnlySet<string>> BuildProvider(
		IPersistedResourceKeyReader reader,
		ILogger logger,
		PageUpdateOptions options,
		Func<PageUpdateCommand> resolveCommand) =>
		() => ReadKeys(reader, logger, options, resolveCommand);
}
