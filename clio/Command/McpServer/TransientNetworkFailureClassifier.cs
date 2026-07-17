using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Clio.Command.McpServer;

/// <summary>
/// Classifies a failure as a <b>transient network-level</b> fault that is worth retrying (a DNS flap,
/// a reset/refused connection, a timeout, or a gateway hiccup) versus a durable failure that must not
/// be retried (server-side validation, compilation, business, or authorization errors).
/// </summary>
/// <remarks>
/// The MCP schema commands (<see cref="CreateEntitySchemaCommand"/>, <c>UpdateEntitySchemaCommand</c>,
/// <c>CreateDataBindingDbCommand</c>) each catch their own exceptions and return a non-zero exit code
/// with the exception message written to the logger. Because the typed exception is therefore already
/// lost by the time <c>sync-schemas</c> inspects the outcome, classification runs on <b>two</b> inputs:
/// <list type="bullet">
/// <item><see cref="IsTransient(Exception)"/> — when a typed exception is still available (e.g. a
/// throwing lookup-registration step, or a defensive catch in the tool itself).</item>
/// <item><see cref="IsTransientErrorMessage(string)"/> — a bounded, documented pattern set applied to
/// the last error message a command logged.</item>
/// </list>
/// Message matching is deliberately best-effort: exception text is OS-locale dependent, so on a
/// non-English server OS an unmatched message degrades gracefully to today's fail-fast behavior
/// (no worse than the status quo). An exception that <see cref="McpExceptionPolicy.IsUnrecoverable"/>
/// classifies as fatal is never transient.
/// </remarks>
internal static class TransientNetworkFailureClassifier {

	/// <summary>
	/// Case-insensitive substrings that identify a transient network-level failure inside a logged
	/// error message. Kept as a documented constant so the set is auditable and stable across the
	/// classifier and its tests. Covers DNS resolution failures, connection reset/refused, timeouts,
	/// generic transport failures, and gateway-class HTTP responses (502/503/504).
	/// </summary>
	internal static readonly string[] TransientMessageMarkers = [
		// DNS resolution failures (Windows / Linux / macOS phrasings).
		"no such host is known",
		"name or service not known",
		"nodename nor servname provided",
		"temporary failure in name resolution",
		"the remote name could not be resolved",
		// Connection reset / refused / aborted.
		"connection reset",
		"connection refused",
		"actively refused",
		"forcibly closed",
		"a connection attempt failed",
		"an existing connection was forcibly closed",
		"broken pipe",
		// Timeouts.
		"timed out",
		"the operation has timed out",
		"a task was canceled",
		// Generic transport failure surfaced by HttpClient.
		"an error occurred while sending the request",
		"the ssl connection could not be established",
		"unable to read data from the transport connection",
		// Gateway-class HTTP responses (infrastructure, not business errors). Only the descriptive
		// phrases are matched — bare "502"/"503"/"504" digit runs are deliberately NOT markers because
		// they collide with incidental digits in durable errors (C# codes like CS0503, line/column
		// numbers, record counts, schema names, GUID hex fragments), which would defeat fail-fast.
		"bad gateway",
		"service unavailable",
		"gateway timeout"
	];

	/// <summary>
	/// Returns <see langword="true"/> when the exception (walking its inner and aggregated exceptions)
	/// is a transient network-level fault. Fatal exceptions (see
	/// <see cref="McpExceptionPolicy.IsUnrecoverable"/>) are never transient.
	/// </summary>
	/// <param name="exception">The caught exception, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the failure is transient and worth retrying.</returns>
	public static bool IsTransient(Exception? exception) {
		if (exception is null || McpExceptionPolicy.IsUnrecoverable(exception)) {
			return false;
		}
		if (exception is AggregateException aggregate) {
			return aggregate.Flatten().InnerExceptions.Any(IsTransientCore);
		}
		for (Exception? current = exception; current is not null; current = current.InnerException) {
			if (IsTransientCore(current)) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Returns <see langword="true"/> when the given logged error message matches a known transient
	/// network-level failure marker. Matching is case-insensitive and best-effort.
	/// </summary>
	/// <param name="errorMessage">The last error message a command logged, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the message indicates a transient failure.</returns>
	public static bool IsTransientErrorMessage(string? errorMessage) {
		if (string.IsNullOrWhiteSpace(errorMessage)) {
			return false;
		}
		return TransientMessageMarkers.Any(
			marker => errorMessage.Contains(marker, StringComparison.OrdinalIgnoreCase));
	}

	// Classifies a single (already-unwrapped) exception node. TaskCanceledException maps to a timeout
	// here because no CancellationToken reaches the sync-schemas execution path today, so a cancellation
	// on this path is always an HttpClient timeout rather than a genuine caller cancellation; the bounded
	// attempt budget caps the damage even if that assumption ever changes.
	private static bool IsTransientCore(Exception exception) =>
		exception is HttpRequestException
			or SocketException
			or WebException
			or TimeoutException
			or TaskCanceledException
			or IOException
		|| IsTransientErrorMessage(exception.Message);
}
