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
	[Description("PROPERTY: for every corpus input y and every stage E that runs after master's chain (Extra, Site, "
		+ "ExtraCredentials, ExtraJson), every run of letters or digits in E(y) outside a placeholder is a substring of "
		+ "y - a stage can only replace, never write text of its own.")]
	public void EveryStageAfterTheChain_ShouldOnlyReplaceSpans() {
		// Arrange
		(string Name, Func<string, string> Stage)[] stages = [
			("Extra", SensitiveErrorTextRedactor.Extra), ("Site", SensitiveErrorTextRedactor.Site),
			("ExtraCredentials", SensitiveErrorTextRedactor.ExtraCredentials), ("ExtraJson", SensitiveErrorTextRedactor.ExtraJson)
		];
		List<string> wrong = [];

		// Act
		foreach ((string _, string y) in RedactionDifferentialCorpus.Strings()) {
			foreach ((string name, Func<string, string> stage) in stages) {
				string output = SensitiveErrorTextRedactor.ExecuteRegex(() => stage(y));
				foreach (Match run in Run.Matches(Placeholder.Replace(output, " "))) {
					if (!y.Contains(run.Value, StringComparison.Ordinal)) {
						wrong.Add($"{name} wrote \"{run.Value}\" into \"{output}\" from \"{y}\"");
					}
				}
				if (!IsSpanReplacement(y, output)) {
					wrong.Add($"{name} is no span replacement: \"{y}\" -> \"{output}\"");
				}
			}
		}

		// Assert
		wrong.Should().BeEmpty(because: $"a stage after the chain may only replace spans; {wrong.Count} found:\n" + string.Join('\n', wrong.Take(20)));
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

	#endregion

	#region The shape: Redact = Extra(chain), RedactSiteText = Site(Redact), RedactCredentials = ExtraCredentials(chain)

	[Test]
	[Category("Unit")]
	[Description("COMPOSITION at every entry point over the corpus sample: Redact is Extra applied to master's chain, "
		+ "RedactSiteText is Site applied to Redact, RedactCredentials is ExtraCredentials applied to master's credentials "
		+ "chain and RedactJsonCredentialProperties is ExtraJson applied to master's JSON rule - each a span replacement "
		+ "of the stage before it. The fenced and console renderings carry Redact's or RedactSiteText's output, flattened "
		+ "and cut.")]
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
			CheckFenced(SensitiveErrorTextRedactor.RedactUntrustedOrNull(text), redact, "RedactUntrustedOrNull", text);
			CheckFenced(SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text), site, "RedactUntrustedSiteTextOrNull", text);
			CheckFenced(SensitiveErrorTextRedactor.RedactForConsoleOrNull(text), redact, "RedactForConsoleOrNull", text);
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

	private static readonly string[] CallSiteSamples = [
		@"Insert refused. See ..\..\Terrasoft.Configuration\Web.config for the cause.",
		"Login failed for user 'zq_login'. password=\"hunter2 x\" at db01.internal:1433 via redis:6379",
		"Access denied\n   at Terrasoft.Core.Entities.Entity.Save() in C:\\src\\x.cs:line 4",
		"Authorization: Bearer x7Kq2mVb9LpQ3 rejected; token: Basic X7q2Q; ConnectionPassword=hunter2",
		"password=\"" + new string('a', 1990) + " hunter2\" end",
	];

	[TestCaseSource(nameof(CallSiteSamples))]
	[Category("Unit")]
	[Description("COMPOSITION at every wired call site (SiteTextWiringTests' 'wired:' readers and the fenced item paths): "
		+ "each carries RedactSiteText's output of the site's text - redacted first, cut after - and that output is Site "
		+ "applied to Redact, which is Extra applied to master's chain.")]
	public void EveryWiredCallSite_ShouldCarryTheComposedSiteText(string text) {
		// Arrange
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);
		string chain = SensitiveErrorTextRedactor.MasterChain.Redact(text);
		string fenced = SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text);
		string json = JsonSerializer.Serialize(new { success = false, errorInfo = new { message = text, errorCode = "x" } });

		// Act
		string message = SiteResponse.FromRefusal(new SiteHttpResponse(500, json)).Message;
		string preview = CreatioResponseError.DescribeNonJsonResponse(text);
		string keyed = ODataKeyedWrite.ValidateWriteResponse(JsonSerializer.Serialize(new { error = new { code = "", message = text } }));
		string saved = PageSchemaMetadataHelper.ParseSaveErrorMessage(JObject.Parse(json), "Failed to save page schema");
		Action service = () => DataServiceResponse.ThrowIfUnsuccessful(json, "Write");
		string diagnostic = DataWriteDiagnostic.Create("insert", "Account", null, true, true, false, text).Message;
		string inner = RecordDeletionService.ReadInnerMessage(JsonSerializer.Serialize(new {
			error = new { message = "An error has occurred.", innererror = new { message = text } }
		}));

		// Assert
		IsSpanReplacement(chain, SensitiveErrorTextRedactor.Redact(text)).Should().BeTrue(because: "Redact replaces spans of master's chain");
		IsSpanReplacement(SensitiveErrorTextRedactor.Redact(text), site).Should().BeTrue(because: "Site replaces spans of Redact");
		site.Should().StartWith(message.TrimEnd('\u2026'), because: "site-response.message is RedactSiteText's output, capped after");
		preview.Should().Contain(" Response: " + (site.Length > 500 ? site[..500] + "..." : site),
			because: "the non-JSON preview is RedactSiteText's output, cut after");
		keyed.Should().StartWith(site, because: "the keyed write carries RedactSiteText's output of the OData message");
		saved.Should().Be(site, because: "the designer's save error is RedactSiteText's output");
		service.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("Write failed: " + site);
		diagnostic.Should().Be(fenced, because: "DataWriteDiagnostic.message is the fenced site rendering");
		inner.Should().Be(SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(ProcessRunMonitor.FirstLine(text)),
			because: "the refused delete's inner message is the fenced site rendering of its first line");
	}

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
		output.Should().BeOneOf(["[redacted]", "[untrusted-source-text begin] [redacted] [untrusted-source-text end]"],
			because: "a timeout fails the whole text closed");
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
	[Description("Condition 3: nothing is clamped before master's chain. A password value of 21,000 characters crosses "
		+ "every clamp #1990 had put before the chain - 16,000 at site-response.message, 4,000 at the non-JSON preview, "
		+ "2,400 at the fenced rendering - and ClampAtBoundary's 512-character window after an opening quote, so a clamp "
		+ "cuts it at an inner space. Redacted whole, it goes. Clamped first, master's quoted-value rules lose the closing "
		+ "quote: the pair rule takes only the backslash of \\\" and the words stay.")]
	public void EveryClampedSink_ShouldRedactBeforeItClamps(string shape) {
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

		// Assert
		message.Should().NotContain("zqword", because: "site-response.message redacts the whole value before its cap");
		preview.Should().NotContain("zqword", because: "the preview redacts the whole body before its cut");
		fenced.Should().NotContain("zqword", because: "the fenced rendering redacts the whole text before its cut");
		new[] { message, preview, fenced }.Should().AllSatisfy(sink => sink.Should().NotContain("zqsecret"));
	}

	#endregion
}
