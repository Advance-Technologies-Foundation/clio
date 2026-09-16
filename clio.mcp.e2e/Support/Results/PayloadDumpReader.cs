using System.Text.RegularExpressions;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Reads back the payload dump a parse-failure message points at (GitHub issue #1537).
/// </summary>
/// <remarks>
/// Test-only. A parser's failure message now names a file instead of quoting the payload, so a test that
/// wants to assert on what the tool actually returned has to follow the pointer the reader would follow.
/// The dump is deleted once read: these tests exercise the real sink, and leaving files behind would let
/// one run's artifacts be mistaken for the next run's evidence.
/// <para>
/// Two properties make recovering a path out of prose safe, and both are load-bearing. The path is
/// SELF-DELIMITING — <c>Payload="&lt;path&gt;"</c>, matched to its closing quote — because the message is
/// not always the last thing a caller writes: <c>ApplicationToolE2ETests</c> re-throws parse failures as
/// <c>{message} Raw result: {description}</c>, and a terminator that ran to the next
/// <c>&lt;Word&gt;=</c> token or to end-of-string swallowed that trailing JSON and found no file. And a
/// recovered path is CONFINED to <see cref="TestResultsPayloadDumpSink.DumpDirectory"/>, because the same
/// message also carries server-supplied text (the redacted JSON error, and the excerpt emitted when the
/// write failed) in which a hostile result can plant its own <c>Payload="…"</c> token; redaction
/// neutralizes absolute paths and URIs but not a relative one such as
/// <c>Payload="obj/project.assets.json"</c>. Confinement means the worst a planted token can name is
/// another dump, never a file the suite depends on.
/// </para>
/// </remarks>
internal static partial class PayloadDumpReader {
	// Quoted and non-greedy to the closing quote: the path the sink produces is a GUID-suffixed file name
	// under a directory this process resolved, so it never contains a quote of its own. The failed-write
	// form is Payload=(dump failed: <reason>) — unquoted on purpose, so it cannot match here at all.
	[GeneratedRegex(@"Payload=""(?<path>[^""]*)""", RegexOptions.CultureInvariant)]
	private static partial Regex DumpPathRegex();

	/// <summary>
	/// Extracts the dump path a message names.
	/// </summary>
	/// <param name="message">The parse-failure message.</param>
	/// <returns>The path, or <c>null</c> when the message names no dump this sink wrote.</returns>
	public static string? ExtractPath(string message) {
		foreach (Match match in DumpPathRegex().Matches(message)) {
			string candidate = match.Groups["path"].Value;
			if (IsInsideDumpDirectory(candidate)) {
				return candidate;
			}
		}

		return null;
	}

	/// <summary>
	/// Deletes the dump a message names, if it produced one.
	/// </summary>
	/// <remarks>
	/// For a test that provokes a parse failure without asserting on the payload. Every such test still
	/// writes a real dump, and leaving them behind would fill the published <c>TestResults</c> artifact
	/// with files from tests that PASSED — which is exactly what makes the one dump belonging to a real
	/// failure hard to find. Each test deletes its OWN dump by path rather than sweeping the directory,
	/// because fixtures in this assembly can run in parallel and a sweep would delete another fixture's
	/// evidence.
	/// <para>
	/// BEST-EFFORT, and deliberately so. Every call site sits inside a <c>catch</c> block whose job is to
	/// swallow a parse failure on a PASSING test's lenient path, so an <c>IOException</c> from a transient
	/// lock (an antivirus or indexer touching a just-written file on the Windows agent) or from the file
	/// vanishing between the probe and the delete would replace a passing outcome with an unrelated IO
	/// error. A leftover dump is harmless; a hijacked test result is not.
	/// </para>
	/// </remarks>
	/// <param name="message">The parse-failure message.</param>
	public static void DeleteIfPresent(string message) {
		string? path = ExtractPath(message);
		if (path is null) {
			return;
		}

		try {
			File.Delete(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// Intentionally ignored — see the best-effort note above.
		}
	}

	/// <summary>
	/// Reads the dump a message names and deletes it.
	/// </summary>
	/// <param name="message">The parse-failure message.</param>
	/// <returns>The dump's full content.</returns>
	/// <exception cref="InvalidOperationException">The message named no dump, or the file is absent.</exception>
	public static string ReadAndDelete(string message) {
		string path = ExtractPath(message)
			?? throw new InvalidOperationException(
				$"The failure message names no payload dump, so there is nothing to read back: {message}");

		if (!File.Exists(path)) {
			throw new InvalidOperationException(
				$"The failure message names a payload dump at '{path}', but no file is there.");
		}

		string content = File.ReadAllText(path);
		DeleteIfPresent(message);
		return content;
	}

	/// <summary>
	/// Whether a recovered path resolves inside the sink's own dump directory.
	/// </summary>
	/// <remarks>
	/// Compares full paths so <c>..</c> segments cannot escape, and rejects rather than throws on a path
	/// the OS refuses outright — this runs on a diagnostic path that must not raise.
	/// </remarks>
	private static bool IsInsideDumpDirectory(string candidate) {
		if (string.IsNullOrWhiteSpace(candidate)) {
			return false;
		}

		try {
			string directory = Path.TrimEndingDirectorySeparator(
				Path.GetFullPath(TestResultsPayloadDumpSink.DumpDirectory));
			string full = Path.GetFullPath(candidate);
			return full.Length > directory.Length
				&& full.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
				&& (full[directory.Length] == Path.DirectorySeparatorChar
					|| full[directory.Length] == Path.AltDirectorySeparatorChar);
		}
		catch (Exception) {
			return false;
		}
	}
}
