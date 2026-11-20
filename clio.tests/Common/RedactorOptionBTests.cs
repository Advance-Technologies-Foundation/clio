using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Option B's construction, proven: every entry point runs master's frozen chain first, and what clio adds after it
/// (Extra, Site, ExtraCredentials, ExtraJson) only replaces spans of the chain's output through the one edit function.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class RedactorOptionBTests {

	private static readonly Regex Placeholder = new(@"\[redacted(?:-[a-z]+)?\]|\[untrusted-source-text (?:begin|end)\]",
		RegexOptions.CultureInvariant);

	private static readonly Regex Run = new("[A-Za-z0-9]+", RegexOptions.CultureInvariant);

	// A deterministic sample of the differential corpus, every fixture spelling and every tenth random string.
	private static IEnumerable<string> Sample() =>
		RedactionDifferentialCorpus.Strings().Where((entry, index) => !entry.Id.StartsWith("s:", StringComparison.Ordinal) || index % 10 == 0)
			.Select(entry => entry.Text);

	/// <summary>
	/// Whether <paramref name="output"/> is <paramref name="source"/> with spans replaced by placeholders: its pieces
	/// between placeholders appear in the source in order, the first at its start and the last at its end.
	/// </summary>
	internal static bool IsSpanReplacement(string source, string output) {
		string[] pieces = Placeholder.Split(output);
		int position = 0;
		for (int index = 0; index < pieces.Length; index++) {
			string piece = pieces[index];
			if (piece.Length == 0) {
				continue;
			}
			bool last = index == pieces.Length - 1;
			int found = last ? source.Length - piece.Length : source.IndexOf(piece, position, StringComparison.Ordinal);
			if (found < position || !source.AsSpan(found).StartsWith(piece, StringComparison.Ordinal) || (index == 0 && found != 0)) {
				return false;
			}
			position = found + piece.Length;
		}
		return true;
	}

	#region Condition 4: the one edit function

	[Test]
	[Category("Unit")]
	[Description("PROPERTY, on each stage's REAL input over the whole corpus: Extra runs on MasterChain.Redact(y), Site on "
		+ "Redact(y), ExtraCredentials on MasterChain.RedactCredentials(y) and ExtraJson on "
		+ "MasterChain.RedactJsonCredentialProperties(y). For each, every run of letters or digits in its output outside a "
		+ "placeholder is a substring of its input, and the output is its input with spans replaced by placeholders - a stage "
		+ "can only replace, never write text of its own.")]
	public void EveryStageAfterTheChain_ShouldOnlyReplaceSpans() {
		// Arrange
		(string Name, Func<string, string> Input, Func<string, string> Stage)[] stages = [
			("Extra", SensitiveErrorTextRedactor.MasterChain.Redact, SensitiveErrorTextRedactor.Extra),
			("Site", SensitiveErrorTextRedactor.Redact, SensitiveErrorTextRedactor.Site),
			("ExtraCredentials", SensitiveErrorTextRedactor.MasterChain.RedactCredentials, SensitiveErrorTextRedactor.ExtraCredentials),
			("ExtraJson", SensitiveErrorTextRedactor.MasterChain.RedactJsonCredentialProperties, SensitiveErrorTextRedactor.ExtraJson)
		];
		List<(string Id, string Text)> corpus = RedactionDifferentialCorpus.Strings().ToList();
		System.Collections.Concurrent.ConcurrentBag<string> wrong = [];
		int visited = 0;

		// Act
		System.Threading.Tasks.Parallel.ForEach(corpus, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 }, entry => {
			System.Threading.Interlocked.Increment(ref visited);
			foreach ((string name, Func<string, string> input, Func<string, string> stage) in stages) {
				string x = SensitiveErrorTextRedactor.ExecuteRegex(() => input(entry.Text));
				string output = SensitiveErrorTextRedactor.ExecuteRegex(() => stage(x));
				foreach (Match run in Run.Matches(Placeholder.Replace(output, " "))) {
					if (!x.Contains(run.Value, StringComparison.Ordinal)) {
						wrong.Add($"{name} wrote \"{run.Value}\" into \"{output}\" from \"{x}\"");
					}
				}
				if (!IsSpanReplacement(x, output)) {
					wrong.Add($"{name} is no span replacement: \"{x}\" -> \"{output}\"");
				}
			}
		});

		// Assert
		visited.Should().Be(RedactionDifferentialCorpus.ExpectedCount, because: "every corpus string is checked");
		wrong.Should().BeEmpty(because: $"a stage after the chain may only replace spans; {wrong.Count} found:\n" + string.Join('\n', wrong.Take(20)));
	}

	[Test]
	[Category("Unit")]
	[Description("EVERY PREFILTER IS EXACT: a rule skipped where the text lacks its literal (or its \"=\"/\":\") must match "
		+ "nowhere such a text exists. Over the corpus sample, master's chain output and Redact's output, each as written and "
		+ "upper-cased, every match of every prefiltered rule that has run lies in a text that meets its condition - an "
		+ "IgnoreCase rule's literal is compared ignoring case (\"=[REDACTED]\" matches \"=[redacted]\").")]
	public void EveryPrefilter_ShouldHoldWhereverItsRuleMatches() {
		// Arrange
		List<string> texts = [];
		foreach (string text in Sample()) {
			SensitiveErrorTextRedactor.RedactSiteText(text);
			SensitiveErrorTextRedactor.RedactCredentials(text);
			foreach (string variant in new[] { text, SensitiveErrorTextRedactor.MasterChain.Redact(text),
					SensitiveErrorTextRedactor.MasterChain.RedactCredentials(text), SensitiveErrorTextRedactor.Redact(text) }) {
				texts.Add(variant);
				texts.Add(variant.ToUpperInvariant());
			}
		}
		List<SensitiveErrorTextRedactor.PrefilteredRule> rules = [.. SensitiveErrorTextRedactor.Prefiltered.Values];
		System.Collections.Concurrent.ConcurrentBag<string> wrong = [];

		// Act
		System.Threading.Tasks.Parallel.ForEach(texts, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 }, text => {
			foreach (SensitiveErrorTextRedactor.PrefilteredRule rule in rules) {
				if (!rule.MayMatch(text) && rule.Rule.IsMatch(text)) {
					wrong.Add($"{rule.Rule} matches \"{text}\" although its condition ({rule.Condition}) does not hold");
				}
			}
		});
		TestContext.Out.WriteLine($"{rules.Count} prefiltered rules over {texts.Count} texts");

		// Assert
		rules.Count.Should().Be(34, because: "every prefiltered call site has run and is checked: 41 call sites, 34 distinct "
			+ "rule and condition pairs (Extra and ExtraCredentials share seven)");
		wrong.Should().BeEmpty(because: $"a prefilter may only skip a rule that cannot match; {wrong.Count} found:\n"
			+ string.Join('\n', wrong.Take(10)));
	}

	[TestCase("a [redacted-path] b", 0, 19, "[redacted]", "[redacted-path]", TestName = "Edit_KeepsAPlaceholderInsideTheSpan")]
	[TestCase("x (old) y", 0, 9, "[redacted-path]", "[redacted-path]([redacted-path])[redacted-path]", TestName = "Edit_KeepsEveryBracket")]
	[TestCase("see [redacted-uri]x", 8, 11, "[redacted]", "see [redacted-uri]", TestName = "Edit_WidensToAWholePlaceholder")]
	[TestCase("..[redacted-path] now", 0, 17, "[redacted-path]", "[redacted-path] now", TestName = "Edit_NoNewPlaceholderNextToAKeptOne")]
	[Category("Unit")]
	[Description("The one edit function never deletes a placeholder or a bracket: they stay inside a replaced span, an "
		+ "edit that cuts a placeholder is widened to all of it, and a run next to a kept placeholder needs no new one.")]
	public void ApplyEdits_ShouldNeverDeleteAPlaceholderOrABracket(string text, int start, int length, string placeholder, string expected) {
		// Arrange
		SensitiveErrorTextRedactor.SpanEdit edit = new(start, length, placeholder);

		// Act
		string output = SensitiveErrorTextRedactor.ApplyEdits(text, [edit]);

		// Assert
		output.Should().Be(expected);
	}

	[TestCase("abcdef ghi", 0, 3, 2, 4, "[redacted] ghi", TestName = "Edits_OverlappingAreJoined")]
	[TestCase("abc def ghi", 0, 3, 1, 9, "[redacted]i", TestName = "Edits_ContainedOneIsJoined")]
	[TestCase("abc def ghi", 0, 3, 4, 3, "[redacted] [redacted-uri] ghi", TestName = "Edits_ApartStayApart")]
	[Category("Unit")]
	[Description("Edits that overlap are joined into one span - the earlier edit's placeholder, the union of both spans - "
		+ "instead of the later one being dropped whole, which kept the part of it the earlier one did not reach.")]
	public void ApplyEdits_ShouldJoinOverlappingEdits(string text, int firstStart, int firstLength, int secondStart,
			int secondLength, string expected) {
		// Arrange
		SensitiveErrorTextRedactor.SpanEdit[] edits = [
			new(firstStart, firstLength, "[redacted]"), new(secondStart, secondLength, "[redacted-uri]")
		];

		// Act
		string output = SensitiveErrorTextRedactor.ApplyEdits(text, edits);

		// Assert
		output.Should().Be(expected);
	}

	[Test]
	[Category("Unit")]
	[Description("A collapsing edit - the site cut's - becomes ONE placeholder, brackets and placeholders inside included.")]
	public void ApplyEdits_ShouldCollapseTheCutsTail() {
		// Arrange
		const string text = "Denied.\n<html>(a) [redacted-uri] {b}</html>";

		// Act
		string output = SensitiveErrorTextRedactor.ApplyEdits(text, [new SensitiveErrorTextRedactor.SpanEdit(8, text.Length - 8, "[redacted]", Collapse: true)]);

		// Assert
		output.Should().Be("Denied.\n[redacted]");
	}

	[Test]
	[Category("Unit")]
	[Description("The edit function is linear: 200,000 edits over a text with 100,000 placeholders take well under a "
		+ "second. The loop that compared every edit with every placeholder took minutes here.")]
	public void ApplyEdits_ShouldBeLinear_InTheEditsAndThePlaceholders() {
		// Arrange
		string text = string.Concat(Enumerable.Repeat("[redacted] ab ", 100_000));
		SensitiveErrorTextRedactor.SpanEdit[] edits = [.. Enumerable.Range(0, 100_000)
			.SelectMany(index => new SensitiveErrorTextRedactor.SpanEdit[] { new(index * 14 + 11, 2, "[redacted]"), new(index * 14 + 5, 2, "[redacted]") })];
		System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

		// Act
		string output = SensitiveErrorTextRedactor.ApplyEdits(text, edits);
		watch.Stop();

		// Assert
		output.Should().NotContain("ab");
		watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), because: "the edit function must stay linear");
	}

	#endregion

	#region The shape: Redact = Extra(chain), RedactSiteText = Site(Redact), RedactCredentials = ExtraCredentials(chain)

	[Test]
	[Category("Unit")]
	[Description("COMPOSITION at every entry point over the corpus sample: Redact is Extra applied to master's chain, "
		+ "RedactSiteText is Site applied to Redact, RedactCredentials is ExtraCredentials applied to master's credentials "
		+ "chain and RedactJsonCredentialProperties is ExtraJson applied to master's JSON rule - each a span replacement "
		+ "of the stage before it. The fenced and console renderings carry Redact's or RedactSiteText's output of master's "
		+ "2,400-character cut, flattened and cut at 300.")]
	public void EveryEntryPoint_ShouldBeTheFrozenChainThenSpanReplacements() {
		// Arrange
		List<string> wrong = [];

		// Act
		foreach (string text in Sample()) {
			string chain = SensitiveErrorTextRedactor.MasterChain.Redact(text);
			string redact = SensitiveErrorTextRedactor.Redact(text);
			string site = SensitiveErrorTextRedactor.RedactSiteText(text);
			string credentialsChain = SensitiveErrorTextRedactor.MasterChain.RedactCredentials(text);
			string credentials = SensitiveErrorTextRedactor.RedactCredentials(text);
			string jsonChain = SensitiveErrorTextRedactor.MasterChain.RedactJsonCredentialProperties(text);
			string json = SensitiveErrorTextRedactor.RedactJsonCredentialProperties(text);
			Check(redact == SensitiveErrorTextRedactor.Extra(chain) && IsSpanReplacement(chain, redact), "Redact", text, redact);
			Check(site == SensitiveErrorTextRedactor.Site(redact) && IsSpanReplacement(redact, site), "RedactSiteText", text, site);
			Check(credentials == SensitiveErrorTextRedactor.ExtraCredentials(credentialsChain)
				&& IsSpanReplacement(credentialsChain, credentials), "RedactCredentials", text, credentials);
			Check(json == SensitiveErrorTextRedactor.ExtraJson(jsonChain) && IsSpanReplacement(jsonChain, json),
				"RedactJsonCredentialProperties", text, json);
			string cut = text.Length > 2_400 ? text[..2_400] : text;
			CheckFenced(SensitiveErrorTextRedactor.RedactUntrustedOrNull(text), SensitiveErrorTextRedactor.Redact(cut), "RedactUntrustedOrNull", text);
			CheckFenced(SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text), SensitiveErrorTextRedactor.RedactSiteText(cut),
				"RedactUntrustedSiteTextOrNull", text);
			CheckFenced(SensitiveErrorTextRedactor.RedactForConsoleOrNull(text), SensitiveErrorTextRedactor.Redact(cut), "RedactForConsoleOrNull", text);
			Check(SensitiveErrorTextRedactor.RedactAll([text]).Single() == redact, "RedactAll", text, redact);
		}

		// Assert
		wrong.Should().BeEmpty(because: $"every entry point is the frozen chain then span edits; {wrong.Count} found:\n"
			+ string.Join('\n', wrong.Take(20)));

		void Check(bool holds, string entry, string text, string output) {
			if (!holds) {
				wrong.Add($"{entry}: \"{text}\" -> \"{output}\"");
			}
		}

		void CheckFenced(string fenced, string redacted, string entry, string text) {
			string flattened = SensitiveErrorTextRedactor.FlattenDisplayHostileRuns(redacted);
			if (fenced is null) {
				Check(flattened.Length == 0, entry, text, "<null>");
				return;
			}
			string payload = fenced.Replace("[untrusted-source-text begin] ", "").Replace(" [untrusted-source-text end]", "").TrimEnd('\u2026');
			Check(flattened.StartsWith(payload, StringComparison.Ordinal), entry, text, fenced);
		}
	}

	// The composition at every wired sink is RedactorSinkCompositionTests' (689 strings, every sink through its seam).

	#endregion

	#region Condition 5: every stage fails closed

	private static Regex AlwaysTimingOutRule() => new(@"(a+a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(5));

	private static readonly string CatastrophicText = "Login failed for user 'zq_login', password=hunter2 rejected " + new string('a', 40) + "!";

	private static IEnumerable<TestCaseData> StageCases() {
		foreach (string stage in new[] { SensitiveErrorTextRedactor.RedactFirstStage, SensitiveErrorTextRedactor.MasterLastStage,
				SensitiveErrorTextRedactor.RedactLastStage }) {
			yield return new TestCaseData("Redact", stage).SetName($"Timeout_Redact_{stage}");
			yield return new TestCaseData("RedactSiteText", stage).SetName($"Timeout_RedactSiteText_{stage}");
			yield return new TestCaseData("RedactUntrustedOrNull", stage).SetName($"Timeout_RedactUntrustedOrNull_{stage}");
		}
		foreach (string stage in new[] { SensitiveErrorTextRedactor.SiteFirstStage, SensitiveErrorTextRedactor.SiteLastStage }) {
			yield return new TestCaseData("RedactSiteText", stage).SetName($"Timeout_RedactSiteText_{stage}");
			yield return new TestCaseData("RedactUntrustedSiteTextOrNull", stage).SetName($"Timeout_RedactUntrustedSiteTextOrNull_{stage}");
		}
		foreach (string stage in new[] { SensitiveErrorTextRedactor.CredentialsFirstStage, SensitiveErrorTextRedactor.CredentialsLastStage }) {
			yield return new TestCaseData("RedactCredentials", stage).SetName($"Timeout_RedactCredentials_{stage}");
		}
		yield return new TestCaseData("RedactJsonCredentialProperties", SensitiveErrorTextRedactor.JsonLastStage)
			.SetName("Timeout_RedactJsonCredentialProperties_json:last");
	}

	[TestCaseSource(nameof(StageCases))]
	[Category("Unit")]
	[Description("A rule that times out at any stage - before master's chain, after it, after Extra, before or after Site, "
		+ "in the credentials or the JSON chain - fails the whole entry point closed to the placeholder, never to the raw "
		+ "input and never to a half-redacted text.")]
	public void EveryStage_ShouldFailClosed_WhenARuleTimesOut(string entry, string stage) {
		// Arrange
		using IDisposable injected = SensitiveErrorTextRedactor.InjectRuleForTests(stage, AlwaysTimingOutRule());

		// Act
		string output = entry switch {
			"Redact" => SensitiveErrorTextRedactor.Redact(CatastrophicText),
			"RedactSiteText" => SensitiveErrorTextRedactor.RedactSiteText(CatastrophicText),
			"RedactCredentials" => SensitiveErrorTextRedactor.RedactCredentials(CatastrophicText),
			"RedactJsonCredentialProperties" => SensitiveErrorTextRedactor.RedactJsonCredentialProperties(CatastrophicText),
			"RedactUntrustedOrNull" => SensitiveErrorTextRedactor.RedactUntrustedOrNull(CatastrophicText),
			_ => SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(CatastrophicText)
		};

		// Assert
		output.Should().BeOneOf(["[redacted-timeout]", "[untrusted-source-text begin] [redacted-timeout] [untrusted-source-text end]"],
			because: "a timeout fails the whole text closed, to the timeout placeholder");
	}

	#endregion

	#region The run after master's pair placeholder

	[TestCase("The remote server rejected password=s3cr3t (WebException: ProtocolError (HTTP 401 Unauthorized))",
		"The remote server rejected password=[redacted] (WebException: ProtocolError (HTTP 401 Unauthorized))",
		TestName = "PairThenBracket_WebExceptionEnrichment")]
	[TestCase("password=x (Foo: bar)", "password=[redacted] (Foo: bar)", TestName = "PairThenBracket_Parenthesis")]
	[TestCase("token=abc [Bar] baz", "token=[redacted] [Bar] baz", TestName = "PairThenBracket_SquareBracket")]
	[TestCase("pwd=secret {x}", "pwd=[redacted] {x}", TestName = "PairThenBracket_Brace")]
	[Category("Unit")]
	[Description("A credential pair's value never continues across a space into an opening bracket - '(', '[' or '{'. "
		+ "Master's pair rule took the value and kept the bracketed tail, and so does every entry point here - Redact, "
		+ "RedactSiteText, RedactCredentials and the three fenced and console renderings: "
		+ "GetReadableMessageException's '(WebException: ProtocolError (HTTP 401 Unauthorized))' enrichment, which CI "
		+ "and electron's sign-in trigger read, must survive a credential pair before it.")]
	public void APairValue_ShouldNotContinueAcrossASpaceIntoABracket(string text, string expected) {
		// Arrange
		const string begin = "[untrusted-source-text begin] ";
		const string end = " [untrusted-source-text end]";
		string tail = text[text.IndexOfAny(['(', '[', '{'])..];

		// Act
		string master = SensitiveErrorTextRedactor.ExecuteRegex(() => SensitiveErrorTextRedactor.MasterChain.Redact(text));
		string masterCredentials = SensitiveErrorTextRedactor.ExecuteRegex(() => SensitiveErrorTextRedactor.MasterChain.RedactCredentials(text));
		string redact = SensitiveErrorTextRedactor.Redact(text);
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);
		string credentials = SensitiveErrorTextRedactor.RedactCredentials(text);
		string fenced = SensitiveErrorTextRedactor.RedactUntrustedOrNull(text);
		string fencedSite = SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text);
		string console = SensitiveErrorTextRedactor.RedactForConsoleOrNull(text);

		// Assert
		master.Should().Be(expected, because: "this is master's output");
		redact.Should().Be(expected, because: "Redact keeps the bracketed tail, as master does");
		site.Should().Be(expected, because: "RedactSiteText keeps the bracketed tail too");
		credentials.Should().Be(masterCredentials, because: "RedactCredentials gives master's credentials chain output")
			.And.EndWith(tail, because: "the credentials chain keeps the bracketed tail");
		fenced.Should().Be(begin + expected + end, because: "the fenced rendering fences Redact's output");
		fencedSite.Should().Be(begin + expected + end, because: "the fenced site rendering fences RedactSiteText's output");
		console.Should().Be(expected, because: "the console rendering is Redact's output, flattened");
	}

	[TestCase("token=Basic (dXNlcjpwYXNz)", "token=[redacted] (dXNlcjpwYXNz)", TestName = "Residue_BracketedCredentialAfterASchemeWordAfterACredentialKey_Shows")]
	[Category("Unit")]
	[Description("KNOWN RESIDUE: a credential in brackets after a scheme word after a credential key shows, here and on "
		+ "master. Master's pair rule takes the scheme word as the value, and a run that opens with a bracket is left as "
		+ "master leaves it. Pinned so that a narrower rule for bracket contents shows up here.")]
	public void Residue_BracketedCredentialAfterASchemeWord_ShowsAsOnMaster(string text, string expected) {
		// Act
		string master = SensitiveErrorTextRedactor.ExecuteRegex(() => SensitiveErrorTextRedactor.MasterChain.Redact(text));
		string redact = SensitiveErrorTextRedactor.Redact(text);
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		master.Should().Be(expected, because: "master shows the bracketed credential");
		redact.Should().Be(expected, because: "Redact leaves the bracketed run as master leaves it");
		site.Should().Be(expected, because: "RedactSiteText leaves it too");
	}

	#endregion

	#region Conditions 2 and 3: the cut after the chain, no clamp before it

	[TestCase("password=\"hunter2\n   at X.Y()\" rejected", "hunter2", TestName = "Cut_QuotedValueAcrossAStackFrame")]
	[TestCase("token=abc123def\n   at Clio.Tests.X.Y() in C:\\src\\x.cs:line 4", "abc123def", TestName = "Cut_FrameAfterAPair")]
	[TestCase("Access denied\n   at https://zq.example/x.js:1:2", "zq.example", TestName = "Cut_FrameWhoseNameTheChainReplaced")]
	[TestCase("{\"password\":\"hunter2 zqtail\n   at X.Y()\"}", "zqtail", TestName = "Cut_JsonValueAcrossAStackFrame")]
	[Category("Unit")]
	[Description("Condition 2: the site cut runs after Redact, so master's quoted-value rule still sees a value that "
		+ "crosses the stack frame, and the cut still recognises a frame the chain already rewrote.")]
	public void RedactSiteText_ShouldCutAfterTheChain(string text, string secret) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().NotContain(secret, because: "the chain saw the whole text before the cut");
		site.Should().NotContain(" at ", because: "the frame after the line break is cut");
	}

	[TestCase("escaped-quotes", TestName = "Clamp_PasswordInEscapedQuotes")]
	[TestCase("json-escaped", TestName = "Clamp_JsonEscapedPasswordProperty")]
	[Category("Unit")]
	[Description("Condition 3 as master had it: each sink keeps master's cut where master had it - 2,400 before the fenced "
		+ "rendering's chain, 500 before the non-JSON preview's, none before site-response's, which master capped after - "
		+ "and adds no cut before the chain. A password value of 21,000 characters crosses every one. Master's cut leaves "
		+ "it open, so master's quoted-value rules cannot close it and master keeps the words; Extra's rules for a value a "
		+ "cut left open (after master's key=[redacted], and a JSON credential property) take them. site-response "
		+ "redacts the whole value, then caps.")]
	public void EveryCutSink_ShouldHideALongValue_ThatItsCutLeftOpen(string shape) {
		// Arrange
		string words = string.Join(' ', Enumerable.Repeat("zqword", 3000)) + " zqsecret";
		string quote = "\\" + "u0022";
		string text = shape == "escaped-quotes"
			? "Data Source=srv;Password=\\\"" + words + "\\\" end"
			: "{" + quote + "password" + quote + ":" + quote + words + quote + "}";
		string json = JsonSerializer.Serialize(new { errorInfo = new { message = text } });

		// Act
		string message = SiteResponse.FromRefusal(new SiteHttpResponse(500, json)).Message;
		string preview = CreatioResponseError.DescribeNonJsonResponse(text);
		string fenced = SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text);
		string plainFenced = SensitiveErrorTextRedactor.RedactUntrustedOrNull(text);

		// Assert
		new[] { message, preview, fenced, plainFenced }.Should().AllSatisfy(sink => {
			sink.Should().NotContain("zqword", because: "no word of the long value may survive a cut");
			sink.Should().NotContain("zqsecret");
		});
	}

	[Test]
	[Category("Unit")]
	[Description("Condition 3: the cut runs where master ran it. The non-JSON preview cuts the body at 500 BEFORE the "
		+ "redaction, so a 400-character URL that the redaction would shrink cannot move a token from beyond master's cut "
		+ "into the preview; the fenced rendering cuts at 2,400 before it, so the same holds there.")]
	public void CutsBeforeTheChain_ShouldStayWhereMasterHadThem() {
		// Arrange
		string url = "https://zq.example/" + new string('a', 381);
		string body = url + " " + new string('x', 500 - url.Length - 1) + "TAIL zqbeyond";
		string fencedText = url + " " + new string('x', 2_400 - url.Length - 1) + "TAIL zqbeyond";

		// Act
		string preview = CreatioResponseError.DescribeNonJsonResponse(body);
		string fenced = SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(fencedText);

		// Assert
		preview.Should().NotContain("TAIL").And.NotContain("zqbeyond", because: "master cut the body at 500 before redacting");
		fenced.Should().NotContain("zqbeyond", because: "master cut the fenced input at 2,400 before redacting");
	}

	[Test]
	[Category("Unit")]
	[Description("The work budget: a call that runs past it fails closed to [redacted-timeout] - not to the bare [redacted] "
		+ "a value gets - whatever stage it is in, plain C# in the edit function included, which no regex timeout stops. The "
		+ "fail-closed counter counts it, and it is logged once with the entry point and the input length, never the text.")]
	public void EveryEntryPoint_ShouldFailClosedToTheTimeoutPlaceholder_WhenTheCallRunsPastItsWorkBudget() {
		// Arrange
		string text = string.Concat(Enumerable.Repeat("password=Basic X7q2 rejected; ", 20_000));
		List<string> logged = [];
		long failedBefore = SensitiveErrorTextRedactor.FailedClosed;

		// Act
		string redact;
		string site;
		string fenced;
		using (SensitiveErrorTextRedactor.InjectFailedClosedLogForTests(logged.Add))
		using (SensitiveErrorTextRedactor.InjectWorkBudgetForTests(1)) {
			redact = SensitiveErrorTextRedactor.Redact(text);
			site = SensitiveErrorTextRedactor.RedactSiteText(text);
			fenced = SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text);
		}
		long failed = SensitiveErrorTextRedactor.FailedClosed - failedBefore;
		string unbudgeted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redact.Should().Be("[redacted-timeout]", because: "past its budget the call fails closed, visibly");
		site.Should().Be("[redacted-timeout]");
		fenced.Should().Be("[untrusted-source-text begin] [redacted-timeout] [untrusted-source-text end]");
		failed.Should().Be(3, because: "each fail-closed call is counted");
		logged.Should().HaveCount(3).And.AllSatisfy(line => line.Should().NotContain("X7q2", because: "the log never carries the text"));
		logged[0].Should().Be($"clio redacted a text whole: Redact on {text.Length} characters stopped on its work budget.");
		unbudgeted.Should().NotContain("[redacted-timeout]", because: "the injected budget ends with its scope");
	}

	#endregion
}
