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
/// </remarks>
internal static partial class PayloadDumpReader {
	// The negative lookahead matters: on a failed write the message reads
	// Payload=(dump failed: <reason>) PayloadExcerpt="...", and without it the lazy group stops at
	// " PayloadExcerpt=" and hands back "(dump failed: ...)" as though it were a path.
	[GeneratedRegex(@"Payload=(?!\(dump failed:)(?<path>\S.*?)(?=\s(?:[A-Z]\w*=)|$)",
		RegexOptions.CultureInvariant)]
	private static partial Regex DumpPathRegex();

	/// <summary>
	/// Extracts the dump path a message names.
	/// </summary>
	/// <param name="message">The parse-failure message.</param>
	/// <returns>The path, or <c>null</c> when the message names no dump.</returns>
	public static string? ExtractPath(string message) {
		Match match = DumpPathRegex().Match(message);
		return match.Success ? match.Groups["path"].Value.TrimEnd() : null;
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
	/// </remarks>
	/// <param name="message">The parse-failure message.</param>
	public static void DeleteIfPresent(string message) {
		string? path = ExtractPath(message);
		if (path is not null && File.Exists(path)) {
			File.Delete(path);
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
		File.Delete(path);
		return content;
	}
}
