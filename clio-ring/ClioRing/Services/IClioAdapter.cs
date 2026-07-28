using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ClioRing.Services;

/// <summary>
/// Runs the <c>clio</c> CLI as a child process without blocking the caller's thread,
/// streaming stdout/stderr line-by-line and supporting cancellation that terminates the
/// entire child process tree.
/// </summary>
public interface IClioAdapter {
	/// <summary>Raised (off the UI thread) for every stdout/stderr line of any active run.</summary>
	event EventHandler<ClioOutputLine>? OutputReceived;

	/// <summary>
	/// Enumerates the clio environments registered on this machine by invoking
	/// <c>clio show-web-app-list</c> and parsing its JSON (name + url + .NET flavour only — never
	/// credentials). Returns an empty list on any failure.
	/// </summary>
	/// <param name="cancellationToken">Cancels the underlying process.</param>
	Task<IReadOnlyList<ClioEnvironment>> ListEnvironmentsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs a clio invocation asynchronously. Each output line is delivered to
	/// <paramref name="onOutput"/> (and the <see cref="OutputReceived"/> event) as it arrives.
	/// Cancelling <paramref name="cancellationToken"/> kills the whole child process tree.
	/// Raw stdout/stderr and the exit code are always returned.
	/// </summary>
	/// <param name="invocation">The verb, arguments and optional environment to run.</param>
	/// <param name="onOutput">Optional per-line callback invoked as output streams in.</param>
	/// <param name="cancellationToken">Cancellation that terminates the process tree.</param>
	Task<ClioRunResult> RunAsync(
		ClioInvocation invocation,
		Action<ClioOutputLine>? onOutput = null,
		CancellationToken cancellationToken = default);
}
