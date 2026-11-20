using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Tests.Common;

/// <summary>
/// The deterministic corpus the redaction differential runs over: the site-text fixture's inputs in their three
/// spellings (decoded, and JSON-escaped by the default and the relaxed encoder), then seeded random mixes of path shapes,
/// JSON escapes, quotes, brackets, versions, times, sign-in phrases, server instances, hosts and Bearer tokens, and the
/// edge strings: long quoted values that cross every clamp (300, 500, 2,000, 2,400, and the write paths' 200 and 400),
/// and values and paths that cross a stack-frame or page cut.
/// </summary>
/// <remarks>
/// <see cref="Random"/> with a seed is deterministic across runs and machines, and the JSON spellings come from
/// <see cref="JsonSerializer"/>, so the same seeds always give the same strings. The recorded master baseline stores a
/// hash of every input, so a change to this generator fails the differential instead of comparing unrelated strings.
/// </remarks>
internal static class RedactionDifferentialCorpus {

	/// <summary>The seeds of the random part, in order.</summary>
	internal static readonly int[] Seeds = [20261120, 1990, 7];

	/// <summary>How many random strings each seed adds.</summary>
	internal const int StringsPerSeed = 4_000;

	/// <summary>How many strings the corpus holds: 350 fixture rows x 3 spellings, 127 edge strings, 420 shrinking-prefix
	/// strings and 3 x 4,000 seeded ones.</summary>
	internal const int ExpectedCount = 350 * 3 + 127 + 420 + 3 * StringsPerSeed;

	private static readonly string[] Fragments = [
		@"..\x\y.config", @".\conf\x.config", @"..\..\Terrasoft.Configuration\Web.config", @"../..\Terrasoft.Configuration/Web.config",
		@".\conf/x.config", @"\\SRV01\share\Web.config", @"C:foo\bar.config", @"C:Web.config", @"C:\inetpub\wwwroot\x.config",
		@"D:/data/x.mdf", "../conf/x.config", "~/creatio/x.config", "./Terrasoft.WebApp/x", @"..\temp\Web.config", @".\nlog.config",
		@"..\jdoe(old)\secrets\db.config", @"(..\x\y)", @"[..\x]", @"'..\x\y.config'", "\"..\\x\"", @".\SQLEXPRESS", @"SQL01\SQLEXPRESS",
		@"CORP\jdoe", "Login failed for user 'zq_login'", "Login failed for user \"zq_user\"", "Authorization: Bearer x7Kq2mVb9LpQ3",
		"Bearer abcdefgh.ijklmnop", "Password=hunter2", "Data Source=.\\SQLEXPRESS;Initial Catalog=ZqDb", "8.1.0.5", "Version=10.0.0.0",
		"v2.0.1", "10:30", "T10:30:00Z", "Step C: retry", "Range A:B", "x:Item.Value", "p:Item.name", "db01.internal:1433",
		"redis:6379", "10.0.3.17", "https://zq.example/x?y=1", "zq@example.com", "Creatio 8.1.0.5 detected", "...", "..", ".", ",",
		";", ":", "(", ")", "[", "]", "{", "}", "'", "\"", "\\\"", "\\\\", "\\n", "\\r\\n", "\\t", "\\" + "u0022", "\\" + "u0027",
		"\\" + "u00e9", "\n", "\r\n", "\t", "See", "now", "the", "record", "failed.", "Next", "next", "step", "is", "refused", "Order",
		"UsrStatus", "FK_Contact_Account", "C:", "c:", "$", "^", "*", "/", "\\", "-", "_"
	];

	private static readonly string[] Joins = [" ", " ", " ", "", ", ", ". ", "\\n", "\n", "\\\"", "'", "(", ")"];

	// The clamp limits a value may cross: the fenced rendering (300), the non-JSON preview (500), site-response.message
	// (2,000), master's raw cut before its fenced rendering (2,400), and the write paths' quotes: the guard's and the
	// template catalog's previews (200), the dynamic-case detail and the process-design server text (400).
	private static readonly int[] ClampLimits = [300, 500, 2_000, 2_400, 200, 400];

	/// <summary>The edge strings: long quoted values near each clamp, and the cut cases.</summary>
	internal static IEnumerable<string> EdgeStrings() {
		string escapedQuote = "\\\"";
		foreach (int limit in ClampLimits) {
			foreach (int offset in new[] { -12, -3, 0, 3, 12 }) {
				string filler = string.Join(' ', Enumerable.Repeat("zqfill", Math.Max(1, (limit + offset - 40) / 7)));
				yield return "Login failed: password=\"" + filler + " zqsecret\" end";
				yield return "Login failed: Password=" + escapedQuote + filler + " zqsecret" + escapedQuote + " end";
				yield return "Login failed for user 'zq " + filler + " login' end";
				yield return filler + " Authorization: Bearer zqtoken1234567 at db01.internal:1433";
			}
		}
		yield return "password=\"hunter2\n   at X.Y()\" rejected";
		yield return "token=abc123def\n   at Clio.Tests.X.Y() in C:\\src\\x.cs:line 4";
		yield return "Access denied\n   at https://zq.example/x.js:1:2";
		yield return "Value cannot be null.\\r\\nParameter name: password\n   at Terrasoft.Core.X.Y()";
		yield return "Error\n<!DOCTYPE html><html><body>password=hunter2 at db01.internal:1433</body></html>";
		yield return @"Insert refused. See ..\..\Terrasoft.Configuration\Web.config\n   at Terrasoft.Core.X.Y()";
		yield return "Login failed for user 'zq_login'.\r\n   at System.Data.SqlClient.X.Y() --- End of stack trace";
	}

	// Tokens a prefix that shrinks under redaction (a URL, a credential pair, a JSON-escaped password, a path) can move
	// across a cut: master cut BEFORE its chain at 500 and 2,400 (and at the write paths' 200 and 400), and the fenced
	// rendering cuts AFTER it at 300 (and the guard preview at 200).
	private static readonly string[] CrossingTokens = [
		"user@foo-bar.example", "Login failed for user 'zq_login'", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ6cSJ9.c2lnbmF0dXJl",
		"db01.internal:1433", "password=hunter2", "Bearer x7Kq2mVb9LpQ3", "\\\\" + "SRV01" + "\\" + "share" + "\\" + "Web.config"
	];

	private static readonly int[] CrossedCuts = [300, 500, 2_400, 200, 400];

	private static string Filled(string head, char filler, int length) => head + new string(filler, Math.Max(1, length - head.Length));

	// A prefix of the given length that redaction shortens.
	private static string ShrinkingPrefix(string kind, int length) {
		string quote = "\\" + "u0022";
		return kind switch {
			"url" => Filled("https://zq.example/", 'a', length),
			"pair" => Filled("password=", 'p', length),
			"json" => "{" + quote + "password" + quote + ":" + quote + new string('q', Math.Max(1, length - 35)) + quote + "}",
			_ => Filled("C:" + "\\" + "data" + "\\", 'd', length)
		};
	}

	/// <summary>The edge strings whose prefix shrinks under redaction, with a token straddling each cut.</summary>
	internal static IEnumerable<string> ShrinkingPrefixStrings() {
		foreach (int cut in CrossedCuts) {
			foreach (string kind in new[] { "url", "pair", "json", "path" }) {
				foreach (string token in CrossingTokens) {
					foreach (int offset in new[] { 1, token.Length / 2, token.Length - 2 }) {
						yield return ShrinkingPrefix(kind, cut - 1 - offset) + " " + token + " tail zqtail";
					}
				}
			}
		}
	}

	/// <summary>Every corpus string with its stable id: "f:case-NNN:d|e|r" for a fixture row, "e:N" for an edge string, "x:N" for a shrinking-prefix string,
	/// "s:SEED:INDEX" for a random one.</summary>
	internal static IEnumerable<(string Id, string Text)> Strings() {
		foreach (SiteTextRedactionCase row in SiteTextRedactionFixtureTests.LoadRows()) {
			yield return ($"f:{row.Id}:d", row.Input);
			yield return ($"f:{row.Id}:e", Escape(row.Input, relaxed: false));
			yield return ($"f:{row.Id}:r", Escape(row.Input, relaxed: true));
		}
		int edge = 0;
		foreach (string text in EdgeStrings()) {
			yield return ($"e:{edge++}", text);
		}
		int crossing = 0;
		foreach (string text in ShrinkingPrefixStrings()) {
			yield return ($"x:{crossing++}", text);
		}
		foreach (int seed in Seeds) {
			Random random = new(seed);
			for (int index = 0; index < StringsPerSeed; index++) {
				yield return ($"s:{seed}:{index}", RandomString(random));
			}
		}
	}

	private static string RandomString(Random random) {
		int count = random.Next(2, 10);
		StringBuilder text = new();
		for (int index = 0; index < count; index++) {
			if (index > 0) {
				text.Append(Joins[random.Next(Joins.Length)]);
			}
			text.Append(random.Next(3) == 0 ? RandomPath(random) : Fragments[random.Next(Fragments.Length)]);
		}
		int form = random.Next(4);
		return form switch {
			0 => Escape(text.ToString(), relaxed: false),
			1 => Escape(text.ToString(), relaxed: true),
			_ => text.ToString()
		};
	}

	private static string RandomSegment(Random random) {
		const string letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
		int length = random.Next(2, 9);
		return new string(Enumerable.Range(0, length).Select(_ => letters[random.Next(letters.Length)]).ToArray());
	}

	private static string RandomPath(Random random) {
		string[] starts = [@"..\", @".\", @"..\..\", "../", "./", @"\\", "C:", "C:\\", "C:/", "~/", @"..\\", @".\\", @"..\\..\\"];
		string[] separators = [@"\", @"\\", "/", @"\\"];
		StringBuilder path = new(starts[random.Next(starts.Length)]);
		int segments = random.Next(1, 5);
		string separator = separators[random.Next(separators.Length)];
		for (int index = 0; index < segments; index++) {
			if (index > 0) {
				path.Append(random.Next(6) == 0 ? separators[random.Next(separators.Length)] : separator);
			}
			path.Append(RandomSegment(random));
			if (random.Next(4) == 0) {
				path.Append('.').Append(random.Next(2) == 0 ? "config" : RandomSegment(random));
			}
		}
		return path.ToString();
	}

	/// <summary>The text as the inside of a JSON string, as System.Text.Json writes it.</summary>
	internal static string Escape(string text, bool relaxed) {
		JsonSerializerOptions options = relaxed
			? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
			: new JsonSerializerOptions();
		string json = JsonSerializer.Serialize(text, options);
		return json[1..^1];
	}

	/// <summary>The first eight hex digits of the SHA-256 of the text's UTF-8 bytes.</summary>
	internal static string Hash(string text) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();

	// clio's placeholders and fence words, and JSON escapes, are blanked to spaces of the same length before tokens are
	// read, so a token's position in the blanked text is its position in the original.
	private static readonly Regex Blanked = new(
		@"\[redacted(?:-[a-z]+)?\]|\[untrusted-source-text (?:begin|end)\]|\(fence removed\)|\u2026|\\u[0-9A-Fa-f]{4}|\\[nrtbf""\\/]",
		RegexOptions.CultureInvariant);

	private static readonly Regex Token = new("[A-Za-z0-9]{2,}", RegexOptions.CultureInvariant);

	/// <summary>The tokens of two or more letters or digits in the text, with their positions.</summary>
	internal static IEnumerable<(string Value, int Index)> TokensWithPositions(string text) {
		string blanked = Blanked.Replace(text ?? string.Empty, match => new string(' ', match.Length));
		return Token.Matches(blanked).Select(match => (match.Value, match.Index));
	}

	/// <summary>How often each token occurs in the text.</summary>
	internal static Dictionary<string, int> Tokens(string text) =>
		TokensWithPositions(text).GroupBy(token => token.Value, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}
