using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// The regex audit of round B4: the one exponential evaluator (DottedTypeNameRegex, option B's own, never on master) runs
/// on the NonBacktracking engine with its pattern unchanged, proven the same language by a differential against the
/// backtracking original; every regex run is bounded by the call's deadline (<see cref="SensitiveErrorTextRedactor.Bounded"/>);
/// and the rules a long input gets are the generated ones, pattern and options.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class RedactorRegexAuditTests {

	private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

	/// <summary>DottedTypeNameRegex as option B wrote it on 88231c4a8: the same pattern, on the backtracking engine.</summary>
	internal const string DottedTypeNameBacktrackingPattern =
		@"^(?:[A-Z][a-z]+(?:[A-Z]+[a-z]*)*|[A-Z]{2})(?:\.(?:[A-Z][a-z]+(?:[A-Z]+[a-z]*)*|[A-Z]{2}))+$";

	private static Regex Own(string name) => (Regex)typeof(SensitiveErrorTextRedactor).GetMethod(name, AnyStatic)!.Invoke(null, null)!;

	/// <summary>The generated strings of the differential: random mixes of capitals, lower case, dots, digits and "_",
	/// 1-64 characters, from a fixed seed.</summary>
	internal static IEnumerable<string> GeneratedStrings(int count, int seed = 20261120) {
		const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._";
		Random random = new(seed);
		for (int index = 0; index < count; index++) {
			int length = random.Next(1, 65);
			StringBuilder text = new(length);
			// Weighted toward the classes the pattern cares about, so the strings hit capitals, lower case and dots often.
			for (int position = 0; position < length; position++) {
				int pick = random.Next(10);
				text.Append(pick switch {
					< 4 => alphabet[random.Next(26)],
					< 7 => alphabet[26 + random.Next(26)],
					< 9 => '.',
					_ => alphabet[52 + random.Next(alphabet.Length - 52)]
				});
			}
			yield return text.ToString();
		}
	}

	/// <summary>Dotted names built from segments of the shapes the pattern tells apart - a capital and lower case then any
	/// letters, two capitals, one capital, lower case, digits, "_" - so a good share of them match, and each is sometimes
	/// broken by one character.</summary>
	internal static IEnumerable<string> StructuredStrings(int count, int seed = 1990) {
		const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
		const string lower = "abcdefghijklmnopqrstuvwxyz";
		Random random = new(seed);
		string Run(string letters, int min, int max) =>
			new([.. Enumerable.Range(0, random.Next(min, max + 1)).Select(_ => letters[random.Next(letters.Length)])]);
		for (int index = 0; index < count; index++) {
			int segments = random.Next(1, 5);
			StringBuilder text = new();
			for (int segment = 0; segment < segments; segment++) {
				if (segment > 0) {
					text.Append('.');
				}
				text.Append(random.Next(7) switch {
					0 or 1 or 2 => Run(upper, 1, 1) + Run(lower, 1, 4) + Run(upper + lower, 0, 6),
					3 => Run(upper, 2, 2),
					4 => Run(upper, 1, 8),
					5 => Run(lower, 1, 5),
					_ => Run("0123456789_", 1, 3)
				});
			}
			if (random.Next(5) == 0 && text.Length > 0) {
				text[random.Next(text.Length)] = "._0aA"[random.Next(5)];
			}
			yield return text.ToString();
		}
	}

	// The corpus strings, their whitespace-separated pieces and those pieces' trimmed forms: where the rule is asked.
	private static IEnumerable<string> CorpusPieces() =>
		RedactionDifferentialCorpus.Strings().SelectMany(entry => entry.Text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
			.Prepend(entry.Text)).SelectMany(piece => new[] { piece, piece.Trim('.', ',', ';', '(', ')', '[', ']', '\'', '"') }).Distinct();

	/// <summary>The differential of the old regex against the new one: (strings compared, differences, old timeouts).</summary>
	internal static (int Compared, int Differences, int OldTimeouts, int Matched, List<string> Examples) Differential(IEnumerable<string> strings) {
		Regex old = new(DottedTypeNameBacktrackingPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
		Regex now = Own("DottedTypeNameRegex");
		int compared = 0;
		int differences = 0;
		int timeouts = 0;
		int matched = 0;
		List<string> examples = [];
		foreach (string text in strings) {
			compared++;
			bool expected;
			try {
				expected = old.IsMatch(text);
			}
			catch (RegexMatchTimeoutException) {
				timeouts++;
				continue;
			}
			matched += expected ? 1 : 0;
			if (expected != now.IsMatch(text)) {
				differences++;
				if (examples.Count < 10) {
					examples.Add($"\"{text}\": old {expected}");
				}
			}
		}
		return (compared, differences, timeouts, matched, examples);
	}

	[Test]
	[Category("Unit")]
	[Description("M1, SAME LANGUAGE: DottedTypeNameRegex keeps option B's pattern and runs on the NonBacktracking engine, "
		+ "which accepts the same strings. The differential against the backtracking original - over the corpus, its pieces "
		+ "5,000 generated strings of capitals, lower case, dots, digits and '_' (1-64 characters, fixed seed) and 5,000 dotted "
		+ "names built from the segment shapes the pattern tells apart - shows "
		+ "no difference. The rule's IsMatch is all its callers ask, so there are no groups to compare. The full run "
		+ "(CLIO_REGEX_DIFF_COUNT, 1,000,000 strings) is recorded in the PR body.")]
	public void DottedTypeNameRegex_ShouldAcceptTheSameStrings_AsTheBacktrackingOriginal() {
		// Arrange
		int generated = int.TryParse(Environment.GetEnvironmentVariable("CLIO_REGEX_DIFF_COUNT"), out int count) ? count : 5_000;
		List<string> strings = [.. CorpusPieces(), .. GeneratedStrings(generated), .. StructuredStrings(generated)];

		// Act
		(int compared, int differences, int timeouts, int matched, List<string> examples) = Differential(strings);
		TestContext.Out.WriteLine($"DottedTypeNameRegex differential: {compared} strings ({generated} random, {generated} "
			+ $"structured), {matched} matched by the original, {differences} differences, {timeouts} old-regex timeouts");

		// Assert
		Own("DottedTypeNameRegex").Options.Should().HaveFlag(RegexOptions.NonBacktracking);
		Own("DottedTypeNameRegex").ToString().Should().Be(DottedTypeNameBacktrackingPattern, because: "the pattern is option B's own");
		differences.Should().Be(0, because: "the engine changes, not the language: " + string.Join("; ", examples));
		timeouts.Should().Be(0, because: "every generated string is decided by the original too");
	}

	[Test]
	[Category("Unit")]
	[Description("M1, THE ADVERSARIAL TOKEN: 'Aa', forty capitals and '.x' - 45 characters that took the backtracking "
		+ "original past any timeout - is decided at once, and a site text of forty such tokens after a path placeholder "
		+ "(where TokenRunAfterPathPlaceholderRegex asks the rule) is redacted, not failed closed, within its work budget.")]
	public void DottedTypeNameRegex_ShouldDecideTheExponentialToken_AtOnce() {
		// Arrange
		string token = "Aa" + new string('B', 40) + ".x";
		string text = string.Concat(Enumerable.Repeat("[redacted-path] " + token + " ", 40));
		Regex bounded = new(Own("DottedTypeNameRegex").ToString(), Own("DottedTypeNameRegex").Options, TimeSpan.FromMilliseconds(250));
		long failed = SensitiveErrorTextRedactor.FailedClosed;
		long work = SensitiveErrorTextRedactor.Work;

		// Act
		Func<bool> decide = () => bounded.IsMatch(token) || bounded.IsMatch("Aa" + new string('B', 1995) + ".x");
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);
		work = SensitiveErrorTextRedactor.Work - work;

		// Assert
		decide.Should().NotThrow(because: "the NonBacktracking engine decides an exponential token in linear time");
		SensitiveErrorTextRedactor.FailedClosed.Should().Be(failed, because: "forty such tokens are redacted, not failed closed");
		site.Should().NotContain(SensitiveErrorTextRedactor.RedactedTimeout);
		work.Should().BeLessThan(SensitiveErrorTextRedactor.WorkPerCharacter * text.Length);
	}

	[Test]
	[Category("Unit")]
	[Description("M2, THE PER-CALL DEADLINE: with a 1 s backstop, an input master's own host:port rule backtracks on "
		+ "('a.' repeated, 1 MB; every regex step there would take its scaled 4 s) fails closed to [redacted-timeout] "
		+ "within the backstop plus one small step - every regex run gets the time the call has left, not its own timeout.")]
	public void EveryEntryPoint_ShouldEndWithinTheBackstop_WhenARegexBacktracks() {
		// Arrange
		string text = "Password=hunter2 " + string.Concat(Enumerable.Repeat("a.", 500_000));
		Stopwatch watch;

		// Act
		string redact;
		string site;
		TimeSpan redactTime;
		TimeSpan siteTime;
		using (SensitiveErrorTextRedactor.InjectBackstopForTests(TimeSpan.FromSeconds(1))) {
			watch = Stopwatch.StartNew();
			redact = SensitiveErrorTextRedactor.Redact(text);
			redactTime = watch.Elapsed;
			watch.Restart();
			site = SensitiveErrorTextRedactor.RedactSiteText(text);
			siteTime = watch.Elapsed;
		}
		TestContext.Out.WriteLine($"1 s backstop: Redact {redactTime.TotalMilliseconds:0} ms, RedactSiteText {siteTime.TotalMilliseconds:0} ms");

		// Assert
		redact.Should().Be(SensitiveErrorTextRedactor.RedactedTimeout);
		site.Should().Be(SensitiveErrorTextRedactor.RedactedTimeout);
		redactTime.Should().BeLessThan(TimeSpan.FromSeconds(2), because: "Redact ends within the 1 s backstop plus one small step");
		siteTime.Should().BeLessThan(TimeSpan.FromSeconds(2), because: "RedactSiteText ends within the 1 s backstop plus one small step");
	}

	// The prefixes put in front of two quadratic patterns (round B4; the cut's corrected in B5), and FROZEN copies of the
	// patterns as they were before (aebbcb79b), which the differentials compare against.
	internal const string CutPrefix = @"(?=\r|\n|\\[rn])(?<!(?:\r\n|\n|\r|\\r\\n|\\n|\\r)(?:[^\S\r\n]|\\t)*)";

	internal const string CutOriginalPattern =
		@"(?:\r\n|\n|\r|\\r\\n|\\n|\\r)(?=(?:\s|\\[rnt])*(?:at[ \t]+(?:[A-Za-z_][\w`]*(?:\.[\w`<>]+)+|\[redacted(?:-[a-z]+)?\])|---[ \t]*End[ \t]+of\b|<!DOCTYPE\b|<html\b))";

	internal const string RunAfterPlaceholderPrefix = @"(?=\S)";

	internal const string RunAfterPlaceholderOriginalPattern =
		@"(?<=(?<![\w.\-])[\w.\-]*(?:(?:(?:user|db|new|old|my|admin|app|smtp|proxy|current|connection)[_-]?)?(?:password|passphrase|passcode|passwd|pass|pwd|pw)(?:[_-]?(?:confirmation|confirm|repeat|new|old|backup|retry))?|secret|(?:access|refresh|id|auth|bearer|api|client|session|[xc]srf)?[_-]?token|api[_-]?key|client[_-]?secret|client[_-]?id|private[_-]?key|access[_-]?key|connection ?string|authorization|auth|bearer|set-cookie|cookie|asp\.net_sessionid|aspxauth|bpmcsrf|jsessionid|phpsessid|session[_-]?id|[xc]srf[_-]?token|credentials?|data ?source|server|hostname|host|initial ?catalog|database|uid|user ?id)=\[redacted\][ \t]*)(?<q>\\u0022|\\u0027|\\""|&quot;|[""'])?(?<cred>(?:[^\s,;""'\\&]|\\\\|\\(?!u00(?:22|27)|[""'])|&amp;|&(?!quot;))+)(?<close>\k<q>)?";

	// Strings built from the pieces each pattern reads, 1-12 pieces, from a fixed seed.
	internal static IEnumerable<string> PieceStrings(string[] pieces, int count, int seed) {
		Random random = new(seed);
		for (int index = 0; index < count; index++) {
			int length = random.Next(1, 13);
			StringBuilder text = new();
			for (int piece = 0; piece < length; piece++) {
				text.Append(pieces[random.Next(pieces.Length)]);
			}
			yield return text.ToString();
		}
	}

	private static readonly string[] CutPieces = ["\r\n", "\n", "\r", "\\r\\n", "\\n", "\\r", "\\t", "\\R", "\\N", "\\T",
		"\\R\\N", "\\t\\t\\t", " ", "  ", "\t",
		"\u00a0", "\u2028", "at X.Y()", "at [redacted]", "at [redacted-path]", "AT Clio.Tests", "--- End of stack", "<!DOCTYPE html>",
		"<html>", "x", "\\", "\\\\", "A", "at", "at x", "Error."];

	private static readonly string[] RunPieces = ["password=[redacted]", "token=[redacted]", "Data Source=[redacted]", "x-key=[REDACTED]",
		" ", "  ", "\t", "\"", "'", "\\u0022", "\\u0027", "\\\"", "&quot;", "&amp;", "x7Kq", "Basic", "a", "=", ",", ";", "\\",
		"\\\\", "[redacted]", "[redacted-uri]", "rejected", "realm=x"];

	private static int GeneratedCount() =>
		int.TryParse(Environment.GetEnvironmentVariable("CLIO_REGEX_DIFF_COUNT"), out int count) ? count : 5_000;

	// The corpus as each rule sees it: the strings, and master's chain output of them, where the placeholders are.
	private static IEnumerable<string> CorpusAsTheRulesSeeIt() =>
		RedactionDifferentialCorpus.Strings().SelectMany(entry => new[] { entry.Text,
			SensitiveErrorTextRedactor.ExecuteRegex(() => SensitiveErrorTextRedactor.MasterChain.Redact(entry.Text)) });

	[Test]
	[Category("Unit")]
	[Description("SAME FIRST MATCH, linear: the cut's pattern tries only the first break of a run of breaks and blanks. Over "
		+ "the corpus, master's chain output of it, and 5,000 strings built from breaks, written breaks, blanks, frames, page "
		+ "starts and backslashes (CLIO_REGEX_DIFF_COUNT for the full run), its first match - success, index and length, all "
		+ "the cut reads - equals the FROZEN original's (aebbcb79b). On 20,000 characters of written breaks and spaces, and "
		+ "on 80,000 of written tabs (which the B4 look reread from every backslash), it reads the run once.")]
	public void StackOrPageBreakRegex_ShouldFindTheSameFirstBreak_AsTheQuadraticOriginal() {
		// Arrange
		Regex now = Own("StackOrPageBreakRegex");
		now.ToString().Should().StartWith(CutPrefix);
		now.ToString().Should().Be(CutPrefix + CutOriginalPattern, because: "only the prefix is new");
		Regex old = new(CutOriginalPattern, now.Options, TimeSpan.FromSeconds(10));
		List<string> strings = [.. CorpusAsTheRulesSeeIt(), .. PieceStrings(CutPieces, GeneratedCount(), 20261120)];
		string adversarial = string.Concat(Enumerable.Repeat("\\r\\n ", 4_000)) + "!";
		Regex bounded = new(now.ToString(), now.Options, TimeSpan.FromMilliseconds(250));

		// Act
		int matched = 0;
		List<string> differs = [];
		foreach (string text in strings) {
			Match expected = old.Match(text);
			Match actual = now.Match(text);
			matched += expected.Success ? 1 : 0;
			if (expected.Success != actual.Success || expected.Index != actual.Index || expected.Length != actual.Length) {
				differs.Add($"\"{text}\": old {expected.Success}@{expected.Index}+{expected.Length}, new {actual.Success}@{actual.Index}+{actual.Length}");
			}
		}
		TestContext.Out.WriteLine($"StackOrPageBreakRegex differential: {strings.Count} strings, {matched} matched by the original, {differs.Count} differences");
		Action linear = () => bounded.Match(adversarial);

		// Assert
		differs.Should().BeEmpty(because: "only the engine's work changes: " + string.Join("; ", differs.Take(5)));
		linear.Should().NotThrow(because: "20,000 characters of written breaks and spaces are read once");
	}

	[Test]
	[Category("Unit")]
	[Description("The cut reads a run of written tabs once: its first look admits a break only (a real CR or LF, or a "
		+ "written \\r or \\n), so a written \\t - which the B4 look admitted at every backslash, rereading the run of "
		+ "blanks from each (1.2 s on 20,000 characters, 18 s on 80,000) - is skipped at once. 80,000 characters of written "
		+ "tabs are matched within 250 ms, and a site text with 8,000 of them is redacted, not failed closed.")]
	public void StackOrPageBreakRegex_ShouldReadWrittenTabsOnce() {
		// Arrange
		Regex now = Own("StackOrPageBreakRegex");
		Regex bounded = new(now.ToString(), now.Options, TimeSpan.FromMilliseconds(250));
		string writtenTabs = "Login failed. Error " + string.Concat(Enumerable.Repeat("\\t", 40_000)) + " end";
		long failed = SensitiveErrorTextRedactor.FailedClosed;

		// Act
		Action tabs = () => bounded.Match(writtenTabs);
		string site = SensitiveErrorTextRedactor.RedactSiteText("Error " + string.Concat(Enumerable.Repeat("\\t", 8_000)) + " end");

		// Assert
		tabs.Should().NotThrow(because: "80,000 characters of written tabs are read once: a written \\t starts no break");
		SensitiveErrorTextRedactor.FailedClosed.Should().Be(failed, because: "16,000 characters of written tabs are redacted, not failed closed");
		site.Should().EndWith(" end");
	}

	[Test]
	[Category("Unit")]
	[Description("SAME MATCHES AND GROUPS, linear: the run after master's key=[redacted] starts with a look at the next "
		+ "character before its look back. Over the corpus, master's chain output of it, and 5,000 strings built from pairs "
		+ "with placeholders, blanks, quotes in every spelling, runs and backslashes (CLIO_REGEX_DIFF_COUNT for the full "
		+ "run), every match - index, length and the q, cred and close groups the edit reads - equals the original's. On "
		+ "20,000 spaces it no longer reads the blanks back from every blank.")]
	public void CredentialRunAfterPairPlaceholderRegex_ShouldFindTheSameRuns_AsTheQuadraticOriginal() {
		// Arrange
		Regex now = Own("CredentialRunAfterPairPlaceholderRegex");
		now.ToString().Should().StartWith(RunAfterPlaceholderPrefix);
		now.ToString().Should().Be(RunAfterPlaceholderPrefix + RunAfterPlaceholderOriginalPattern, because: "only the prefix is new");
		Regex old = new(RunAfterPlaceholderOriginalPattern, now.Options, TimeSpan.FromSeconds(10));
		List<string> strings = [.. CorpusAsTheRulesSeeIt(), .. PieceStrings(RunPieces, GeneratedCount(), 1990)];
		string adversarial = "password=[redacted]" + new string(' ', 20_000) + "!";
		Regex bounded = new(now.ToString(), now.Options, TimeSpan.FromMilliseconds(250));

		// Act
		int matched = 0;
		List<string> differs = [];
		foreach (string text in strings) {
			string expected = string.Join('|', old.Matches(text).Select(Describe));
			string actual = string.Join('|', now.Matches(text).Select(Describe));
			matched += expected.Length > 0 ? 1 : 0;
			if (expected != actual) {
				differs.Add($"\"{text}\": old {expected}, new {actual}");
			}
		}
		TestContext.Out.WriteLine($"CredentialRunAfterPairPlaceholderRegex differential: {strings.Count} strings, {matched} with a match, {differs.Count} differences");
		Action linear = () => bounded.Matches(adversarial).Count.ToString();

		// Assert
		differs.Should().BeEmpty(because: "only the engine's work changes: " + string.Join("; ", differs.Take(5)));
		linear.Should().NotThrow(because: "20,000 spaces are read once");

		static string Describe(Match match) =>
			$"{match.Index}+{match.Length} q={match.Groups["q"].Index}:{match.Groups["q"].Value} cred={match.Groups["cred"].Index}:{match.Groups["cred"].Value} close={match.Groups["close"].Value}";
	}

	[Test]
	[Category("Unit")]
	[Description("L1, EACH MATCH GETS THE TIME LEFT: a rule with many matches, each slow (an injected rule whose first "
		+ "alternative backtracks over twenty-one letters, about 300 ms, before the second one matches), cannot run past the backstop, "
		+ "because every match is searched under the time left when it starts - Regex.Matches would restart one timeout "
		+ "per match and let the last one take a whole step.")]
	public void EveryMatch_ShouldGetOnlyTheTimeLeft_WhenARuleHasManySlowMatches() {
		// Arrange
		// Each match takes about 300 ms here, under the 500 ms step a 1 s backstop gives: a run bounded once would let
		// the match that starts just before the deadline run its whole 300 ms past it. A ";" between two units keeps the
		// first alternative's backtracking inside one unit.
		Regex slow = new(@"(?:(\w+\s?)*!|a{21}y)", RegexOptions.None, TimeSpan.FromSeconds(30));
		string text = string.Concat(Enumerable.Repeat(new string('a', 21) + "y;", 10)) + "Password=hunter2";
		Stopwatch watch;
		string redacted;
		TimeSpan elapsed;
		List<string> logged = [];

		// Act
		using (SensitiveErrorTextRedactor.InjectFailedClosedLogForTests(logged.Add))
		using (SensitiveErrorTextRedactor.InjectRuleForTests(SensitiveErrorTextRedactor.RedactFirstStage, slow))
		using (SensitiveErrorTextRedactor.InjectBackstopForTests(TimeSpan.FromSeconds(1))) {
			watch = Stopwatch.StartNew();
			redacted = SensitiveErrorTextRedactor.Redact(text);
			elapsed = watch.Elapsed;
		}
		TestContext.Out.WriteLine($"10 slow matches under a 1 s backstop: {elapsed.TotalMilliseconds:0} ms, {redacted}, {string.Join(" | ", logged)}");

		// Assert
		redacted.Should().Be(SensitiveErrorTextRedactor.RedactedTimeout, because: "ten slow matches outlast a 1 s backstop");
		logged.Should().ContainSingle();
		// 1,500 ms (B6): it measured 0.87-0.9 s here, and the slack keeps it clear of a loaded CI agent; the code before
		// B5 took about 3 s on it.
		elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(1_500), because: "no match runs past the time the call has left");
	}

	[Test]
	[Category("Unit")]
	[Description("L2: CredentialShapeMasker.Mask runs its five rules (master's patterns) under the same per-call deadline. "
		+ "Its pair rule reads '.password' repeated 2,000 times (18 KB) in super-linear time; under a 200 ms backstop the "
		+ "call fails closed within the backstop, not after its rule's own 1 s.")]
	public void CredentialShapeMasker_ShouldRunUnderTheCallsDeadline() {
		// Arrange
		string text = string.Concat(Enumerable.Repeat(".password", 2_000));
		Stopwatch watch;
		string masked;
		TimeSpan elapsed;

		// Act
		using (SensitiveErrorTextRedactor.InjectBackstopForTests(TimeSpan.FromMilliseconds(200))) {
			watch = Stopwatch.StartNew();
			masked = CredentialShapeMasker.Mask(text);
			elapsed = watch.Elapsed;
		}
		TestContext.Out.WriteLine($"Mask of 18 KB '.password' under a 200 ms backstop: {elapsed.TotalMilliseconds:0} ms");

		// Assert
		masked.Should().Be(SensitiveErrorTextRedactor.RedactedTimeout);
		elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(700), because: "the masker's rules get the time the call has left");
	}

	private static IEnumerable<(string Name, Regex Rule)> AllRules() =>
		typeof(SensitiveErrorTextRedactor).GetMethods(AnyStatic).Where(method => method.ReturnType == typeof(Regex) && method.GetParameters().Length == 0)
			.Select(method => ("own " + method.Name, (Regex)method.Invoke(null, null)!))
			.Concat(typeof(SensitiveErrorTextRedactor.MasterChain).GetMethods(AnyStatic)
				.Where(method => method.ReturnType == typeof(Regex) && method.GetParameters().Length == 0)
				.Select(method => ("master " + method.Name, (Regex)method.Invoke(null, null)!)))
			.Concat(typeof(CredentialShapeMasker).GetMethods(AnyStatic)
				.Where(method => method.ReturnType == typeof(Regex) && method.GetParameters().Length == 0)
				.Select(method => ("masker " + method.Name, (Regex)method.Invoke(null, null)!)));

	[Test]
	[Category("Unit")]
	[Description("L-a: every rule - clio's, master's chain's and the credential-shape masker's - is left to right, and "
		+ "none but ListContinuationRegex has \\G; that one is only asked IsMatch(text, start), one match at a start "
		+ "position, which is exact. So BoundedMatches gives exactly Regex.Matches' matches, and a rule with either that "
		+ "were run for many matches would make it throw at its first run instead of giving other matches.")]
	public void EveryRule_ShouldBeOneABoundedRunCanServe() {
		// Arrange
		List<(string Name, Regex Rule)> rules = [.. AllRules()];
		Regex anchored = new(@"\Ga", RegexOptions.None, TimeSpan.FromSeconds(1));
		Regex rightToLeft = new("a", RegexOptions.RightToLeft, TimeSpan.FromSeconds(1));
		Regex escaped = new(@"\\G", RegexOptions.None, TimeSpan.FromSeconds(1));

		// Act
		List<string> wrong = [.. rules.Where(rule => !SensitiveErrorTextRedactor.CanBeBounded(rule.Rule)).Select(rule => rule.Name)];
		Action runAnchored = () => SensitiveErrorTextRedactor.BoundedMatches(anchored, "aaa");
		Action runRightToLeft = () => SensitiveErrorTextRedactor.BoundedMatches(rightToLeft, "aaa");
		Action singleAnchored = () => SensitiveErrorTextRedactor.Bounded(anchored, 10).IsMatch("xa", 1);

		// Assert
		rules.Count(rule => rule.Name.StartsWith("masker ", StringComparison.Ordinal)).Should().Be(6,
			because: "the masker's five Mask rules and SecretNameRegex, which names of at most 128 characters go through");
		wrong.Should().Equal(["own ListContinuationRegex"], because: "only the list rule has \\G, and it is never run for many matches");
		runAnchored.Should().Throw<InvalidOperationException>(because: "a pattern with \\G fails loudly at its first run of many matches");
		runRightToLeft.Should().Throw<InvalidOperationException>(because: "a right-to-left rule fails loudly at its first run of many matches");
		singleAnchored.Should().NotThrow(because: "one match at a start position is exact");
		SensitiveErrorTextRedactor.CanBeBounded(escaped).Should().BeTrue(because: "an escaped backslash before a G is no anchor");
	}

	[Test]
	[Category("Unit")]
	[Description("L-b: a rule with an infinite match timeout never runs unbounded: it is read as master's 1 s, scaled and "
		+ "under the call's deadline. An injected rule with no timeout that backtracks exponentially on twenty-four letters "
		+ "(seconds on its own) fails closed within a 1 s backstop.")]
	public void ARuleWithAnInfiniteTimeout_ShouldRunUnderTheDeadline() {
		// Arrange
		Regex unbounded = new(@"(\w+\s?)*!", RegexOptions.None, Regex.InfiniteMatchTimeout);
		string text = new('a', 24);
		Stopwatch watch;
		string redacted;
		TimeSpan elapsed;
		List<string> logged = [];

		// Act
		using (SensitiveErrorTextRedactor.InjectFailedClosedLogForTests(logged.Add))
		using (SensitiveErrorTextRedactor.InjectRuleForTests(SensitiveErrorTextRedactor.RedactFirstStage, unbounded))
		using (SensitiveErrorTextRedactor.InjectBackstopForTests(TimeSpan.FromSeconds(1))) {
			watch = Stopwatch.StartNew();
			redacted = SensitiveErrorTextRedactor.Redact(text);
			elapsed = watch.Elapsed;
		}
		TestContext.Out.WriteLine($"an infinite-timeout rule under a 1 s backstop: {elapsed.TotalMilliseconds:0} ms");

		// Assert
		SensitiveErrorTextRedactor.Bounded(unbounded, 10).Should().NotBeSameAs(unbounded, because: "it always runs rebuilt, with a timeout");
		SensitiveErrorTextRedactor.Bounded(unbounded, 10).MatchTimeout.Should().Be(TimeSpan.FromSeconds(1), because: "outside a call it gets master's 1 s");
		redacted.Should().Be(SensitiveErrorTextRedactor.RedactedTimeout);
		elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(1_500), because: "no rule runs past the call's deadline");
	}

	[Test]
	[Category("Unit")]
	[Description("L1, deterministic: the time left is read again before EVERY match of a run. A reader that spends 300 ms "
		+ "after each of ten fast matches, under a 1 s backstop, sees the run fail closed after about three of them - a run "
		+ "bounded once, as Regex.Matches is, would hand it all ten.")]
	public void EveryMatch_ShouldBeBoundedByTheTimeLeftWhenItStarts() {
		// Arrange
		Regex fast = new("a", RegexOptions.None, TimeSpan.FromSeconds(1));
		string text = new('a', 10);
		int seen = 0;
		Stopwatch watch;
		string result;
		TimeSpan elapsed;

		// Act
		using (SensitiveErrorTextRedactor.InjectFailedClosedLogForTests(_ => { }))
		using (SensitiveErrorTextRedactor.InjectBackstopForTests(TimeSpan.FromSeconds(1))) {
			watch = Stopwatch.StartNew();
			result = SensitiveErrorTextRedactor.ExecuteRegex("a test run", text.Length, () => {
				foreach (Match _ in SensitiveErrorTextRedactor.BoundedMatches(fast, text)) {
					seen++;
					System.Threading.Thread.Sleep(300);
				}
				return "all matches";
			});
			elapsed = watch.Elapsed;
		}

		// Assert
		result.Should().Be(SensitiveErrorTextRedactor.RedactedTimeout, because: "the matches after the deadline are refused");
		seen.Should().BeLessThan(10);
		elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(1_500));
	}

	[Test]
	[Category("Unit")]
	[Description("The rules a long input gets (above 250,000 characters, outside a call: the scaled timeout alone) are the "
		+ "generated ones - the same pattern and the same options, Compiled aside - for every rule of clio's and of master's "
		+ "chain, and a short input gets the generated rule itself.")]
	public void Bounded_ShouldRebuildTheSamePatternAndOptions_ForALongInput() {
		// Arrange
		List<(string Name, Regex Rule)> rules = [.. AllRules()];

		// Act
		List<string> wrong = [];
		foreach ((string name, Regex rule) in rules) {
			Regex longInput = SensitiveErrorTextRedactor.Bounded(rule, 1_000_000);
			Regex shortInput = SensitiveErrorTextRedactor.Bounded(rule, 1_000);
			if (longInput.ToString() != rule.ToString() || (longInput.Options & ~RegexOptions.Compiled) != (rule.Options & ~RegexOptions.Compiled)
					|| longInput.MatchTimeout != TimeSpan.FromSeconds(4) || !ReferenceEquals(shortInput, rule)) {
				wrong.Add(name);
			}
		}

		// Assert
		rules.Count.Should().BeGreaterThan(60);
		wrong.Should().BeEmpty(because: "a long input runs the same rule, with a longer timeout only");
	}
}
