using System;
using System.IO;
using Clio.Common;

namespace Clio.Command.McpServer;

/// <summary>
/// ENG-95885. The ONE way an MCP server emitter reaches a human: through the injected logger AND,
/// on the stdio transport, through standard error.
/// </summary>
/// <remarks>
/// <para>
/// Extracted because two emitters had independently grown the same six-step dance — redact, bound the
/// length, log, check the transport, mirror to stderr, swallow a dead sink — and a third copy was one
/// review round away. <c>McpServerCommand.WarnDuringStartup</c> (issue #1100) and
/// <c>McpToolErrorFilter.ReportArgumentShape</c> (ENG-95885 round 3) now both route through here.
/// </para>
/// <para>
/// The rule the duplication encoded, and the reason this helper exists at all: <b>stdout is the
/// JSON-RPC channel</b>. A stray line there corrupts the protocol, which is why
/// <see cref="ConsoleLogger"/> suppresses every console write in MCP server mode — and why an operator
/// otherwise gets no diagnostic whatsoever. Standard error is outside the transport and is the channel
/// MCP hosts capture, so it is the only console sink an MCP-mode emitter may use. The logger call is
/// kept as well so a configured file sink still receives the line.
/// </para>
/// <para>
/// <paramref name="isMcpServerMode"/> and <paramref name="writeStandardError"/> are parameters rather
/// than reads of <c>Program.IsMcpServerMode</c> and <see cref="Console.Error"/> for one reason: the
/// stderr mirror is the load-bearing half of the delivery and was previously unreachable from a test,
/// because an in-process test is never in MCP server mode. Passing them in makes both branches
/// executable without a mutable static seam or a spawned child process.
/// </para>
/// </remarks>
internal static class McpAdvisoryLog {

	/// <summary>Longest advisory line emitted; a caller-supplied fragment can be arbitrarily long.</summary>
	private const int MaxMessageLength = 1_000;

	/// <summary>
	/// Redacts and length-bounds <paramref name="message"/>, writes it to <paramref name="logger"/>, and
	/// — only when <paramref name="isMcpServerMode"/> — mirrors it to standard error.
	/// </summary>
	/// <param name="logger">The host logger. <c>null</c> is normal (a static seam that could not locate
	/// one) and must stay silent rather than throw.</param>
	/// <param name="message">The advisory text. Redaction and length-bounding happen HERE so no caller
	/// can forget them.</param>
	/// <param name="isWarning"><c>true</c> emits at warning level and tags the mirror <c>[WAR]</c>;
	/// <c>false</c> emits at info level and tags it <c>[INF]</c>.</param>
	/// <param name="isMcpServerMode">Whether the process is serving the MCP stdio transport. Pass
	/// <c>Program.IsMcpServerMode</c> in production.</param>
	/// <param name="writeStandardError">Standard-error writer; defaults to
	/// <see cref="Console.Error"/>. Overridden by tests to observe the mirror.</param>
	/// <returns>The redacted, length-bounded text that was emitted, so a caller can assert on or reuse
	/// exactly what a reader will see.</returns>
	internal static string Emit(
		ILogger? logger,
		string message,
		bool isWarning,
		bool isMcpServerMode,
		Action<string>? writeStandardError = null) {
		string safeMessage = TextUtilities.SanitizeForDisplay(
			SensitiveErrorTextRedactor.Redact(message ?? string.Empty),
			maxLength: MaxMessageLength);

		if (isWarning) {
			logger?.WriteWarning(safeMessage);
		} else {
			logger?.WriteInfo(safeMessage);
		}

		if (!isMcpServerMode) {
			return safeMessage;
		}

		try {
			(writeStandardError ?? Console.Error.WriteLine)(
				$"[{(isWarning ? "WAR" : "INF")}] {safeMessage}");
		}
		catch (IOException) {
			// Stderr is an advisory host channel and may be closed by a detached launcher.
		}
		catch (ObjectDisposedException) {
			// Losing the advisory sink must never fail the operation it describes.
		}

		return safeMessage;
	}
}
