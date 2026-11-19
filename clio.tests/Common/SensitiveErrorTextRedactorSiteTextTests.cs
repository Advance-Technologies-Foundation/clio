using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// The two entry points of <see cref="SensitiveErrorTextRedactor"/> after the site-text plan: what Redact gained,
/// what only RedactSiteText does, that both fail closed, and that a serialized envelope keeps its JSON.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class SensitiveErrorTextRedactorSiteTextTests {

	private const string TimeoutSentinel = "[redacted]";

	// A pattern that backtracks exponentially on the run of "a" CatastrophicTail holds (there is no "b" to end it),
	// with a budget far below what that takes.
	private static Regex AlwaysTimingOutRule() =>
		new(@"(a+a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(5));

	// Long enough for AlwaysTimingOutRule to need minutes, and it survives every rule untouched.
	private static readonly string CatastrophicTail = new string('a', 40) + "!";

	#region Fail closed (requirement a)

	[TestCase(SensitiveErrorTextRedactor.RedactFirstStage)]
	[TestCase(SensitiveErrorTextRedactor.RedactLastStage)]
	[TestCase(SensitiveErrorTextRedactor.SiteFirstStage)]
	[TestCase(SensitiveErrorTextRedactor.SiteLastStage)]
	[Category("Unit")]
	[Description("A rule that times out anywhere in RedactSiteText - before the cut, inside the Redact chain it calls, "
		+ "or after the last site rule - replaces the whole text with the placeholder: never the input, never a "
		+ "half-redacted text.")]
	public void RedactSiteText_ShouldReturnOnlyThePlaceholder_WhenAnyRuleTimesOut(string stage) {
		// Arrange
		string text = "Login failed for user 'invented_login' with Password=hunter2 " + CatastrophicTail;
		using IDisposable injected = SensitiveErrorTextRedactor.InjectRuleForTests(stage, AlwaysTimingOutRule());

		// Act
		string result = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		result.Should().Be(TimeoutSentinel,
			because: "a timeout must fail the WHOLE site text closed, whatever stage the rule that timed out ran in");
	}

	[TestCase(SensitiveErrorTextRedactor.RedactFirstStage)]
	[TestCase(SensitiveErrorTextRedactor.RedactLastStage)]
	[Category("Unit")]
	[Description("A rule that times out inside the Redact chain gives Redact the placeholder alone.")]
	public void Redact_ShouldReturnOnlyThePlaceholder_WhenAnyRuleTimesOut(string stage) {
		// Arrange
		string text = "Password=hunter2 at db01.internal:1433 " + CatastrophicTail;
		using IDisposable injected = SensitiveErrorTextRedactor.InjectRuleForTests(stage, AlwaysTimingOutRule());

		// Act
		string result = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		result.Should().Be(TimeoutSentinel, because: "Redact keeps failing closed with the new rules in its chain");
	}

	[Test]
	[Category("Unit")]
	[Description("A REAL input that the chain cannot finish within its 1 s per-rule budget - a million-character dotted "
		+ "run, on which the host and address rules backtrack at every start - fails both entry points closed, with no "
		+ "injected rule. Measured on 19 Nov: 200,000 characters already exceed the budget, so a million does on any "
		+ "machine.")]
	public void BothEntryPoints_ShouldReturnOnlyThePlaceholder_WhenARealInputExhaustsTheBudget() {
		// Arrange
		string text = "Password=hunter2 for user 'invented_login' " + string.Concat(Enumerable.Repeat("a.", 500_000));

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);
		string redact = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		site.Should().Be(TimeoutSentinel, because: "a real timeout fails the whole site text closed");
		redact.Should().Be(TimeoutSentinel, because: "a real timeout fails Redact closed as well");
	}

	[Test]
	[Category("Unit")]
	[Description("The fenced site rendering fails closed too: its payload is only the placeholder.")]
	public void RedactUntrustedSiteTextOrNull_ShouldFenceOnlyThePlaceholder_WhenARuleTimesOut() {
		// Arrange
		string text = "Login failed for user 'invented_login'. " + CatastrophicTail;
		using IDisposable injected = SensitiveErrorTextRedactor.InjectRuleForTests(
			SensitiveErrorTextRedactor.SiteLastStage, AlwaysTimingOutRule());

		// Act
		string result = SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text);

		// Assert
		result.Should().Be("[untrusted-source-text begin] [redacted] [untrusted-source-text end]",
			because: "the neutralized form must not carry any of the text the timed-out chain did not finish");
	}

	[Test]
	[Category("Unit")]
	[Description("The injected rule is gone once its scope ends, so the seam cannot leak into another test.")]
	public void InjectRuleForTests_ShouldStopApplying_WhenTheScopeIsDisposed() {
		// Arrange
		string text = "Login failed for user 'invented_login'. " + CatastrophicTail;
		using (SensitiveErrorTextRedactor.InjectRuleForTests(SensitiveErrorTextRedactor.SiteFirstStage, AlwaysTimingOutRule())) {
		}

		// Act
		string result = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		result.Should().Be("Login failed for user '[redacted]'. " + CatastrophicTail,
			because: "after the scope the chain runs its own rules only");
	}

	#endregion

	#region Serialized envelopes through Redact

	[Test]
	[Category("Unit")]
	[Description("Redact over a serialized envelope keeps valid JSON, version numbers, clio's own relative paths and "
		+ "routes, and clio's own placeholders, while the new Redact rules still take a scheme credential, a host and "
		+ "a private address.")]
	public void Redact_ShouldKeepJsonVersionsAndClioPaths_WhenRedactingASerializedEnvelope() {
		// Arrange
		const string error = "cliogate 2.0.0.32 is required (Creatio 8.1.0.5 detected); File ./MyPkg/descriptor.json "
			+ "not found; endpoint /DataService/json/SyncReply/SelectQuery returned error:404 at line:42; already "
			+ "[redacted-path] and [redacted-uri]\nAuthorization: Bearer x7Kq2mVb9LpQ3 via redis:6379 and 10.0.3.17";
		string envelope = JsonSerializer.Serialize(new { success = false, error, path = "C:\\temp\\x\\" });

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(envelope);

		// Assert
		using JsonDocument document = JsonDocument.Parse(redacted);
		string result = document.RootElement.GetProperty("error").GetString();
		result.Should().Contain("cliogate 2.0.0.32").And.Contain("Creatio 8.1.0.5",
			because: "a four-part version is not an address (the PR #1374 decision)");
		result.Should().Contain("./MyPkg/descriptor.json", because: "the relative-path rule is site-only");
		result.Should().Contain("/DataService/json/SyncReply/SelectQuery").And.Contain("error:404").And.Contain("line:42",
			because: "a route and a name:number are not a path or a host");
		result.Should().Contain("already [redacted-path] and [redacted-uri]\nAuthorization: Bearer [redacted]",
			because: "clio's own placeholders and the line break survive, and the scheme word stays");
		result.Should().NotContain("x7Kq2mVb9LpQ3").And.NotContain("redis:6379").And.NotContain("10.0.3.17",
			because: "the scheme credential, the single-label host and the private address go");
		document.RootElement.GetProperty("path").GetString().Should().Be("[redacted-path]",
			because: "a drive path ending in a backslash goes whole and leaves the JSON string closed");
	}

	#endregion

	#region Over-redactions fixed, each with a real credential beside it

	[TestCase("The bearer token expired; Authorization: Bearer x7Kq2mVb9LpQ3",
		"The bearer token expired", "x7Kq2mVb9LpQ3", TestName = "Fix_ProseAfterBearer")]
	[TestCase("Bearer token-based access denied; retry with Bearer abcdefgh.ijklmnop-qrstuv",
		"Bearer token-based access denied", "abcdefgh.ijklmnop-qrstuv", TestName = "Fix_HyphenatedProseAfterBearer")]
	[TestCase("Invalid Bearer token. Authorization: Basic U3VwZXJ2aXNvcjpTdXBlcnZpc29y",
		"Invalid Bearer token.", "U3VwZXJ2aXNvcjpTdXBlcnZpc29y", TestName = "Fix_BearerTokenSentence")]
	[TestCase("WWW-Authenticate: Bearer realm=\"api\", error=\"invalid_token\"; sent Authorization: Bearer x7Kq2mVb9LpQ3",
		"WWW-Authenticate: Bearer realm=\"api\", error=\"invalid_token\"", "x7Kq2mVb9LpQ3", TestName = "Fix_Challenge")]
	[TestCase("Error in UsrOrderPage.js:42:13 while calling db01.internal:1433",
		"Error in UsrOrderPage.js:42:13", "db01.internal:1433", TestName = "Fix_FileLineColumn")]
	[TestCase("Key: UsrStatus; token: expired; token: x7Kq2mVb9Lp",
		"Key: UsrStatus; token: expired;", "x7Kq2mVb9Lp", TestName = "Fix_IdentifierAfterTokenColon")]
	[TestCase("client_secret=s3cr3t&api_key=abc&key=v",
		"&api_key=[redacted]&key=[redacted]", "s3cr3t", TestName = "Fix_PairValueAcrossAmpersand")]
	[TestCase("see (/var/log/x) or {C:\\x\\y.dll} with Password=hunter2",
		"see ([redacted-path]) or {[redacted-path]} with Password=", "hunter2", TestName = "Fix_PathEatsClosingBracket")]
	[TestCase("Secret key: m4tQz8Rv!k; duplicate key: FK_Contact_Account",
		"duplicate key: FK_Contact_Account", "m4tQz8Rv!k", TestName = "Fix_ConstraintNameAfterKeyColon")]
	[Category("Unit")]
	[Description("Each over-redaction the plan lists is fixed in Redact, and a real credential written next to it in "
		+ "the same text still goes: the relaxation is a guard, not a hole.")]
	public void Redact_ShouldKeepTheFixedText_AndStillRemoveTheCredentialBesideIt(string text, string kept, string secret) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Contain(kept, because: "the site's words that clio used to take must now stay");
		redacted.Should().NotContain(secret, because: "the credential beside them must still go");
	}

	#endregion

	#region The two entry points stay apart

	[TestCase("Login failed for user 'invented_login'.", "invented_login", TestName = "SiteOnly_SignInPhrase")]
	[TestCase("The conflict occurred in database \"Invented_Db\", table \"dbo.Contact\".", "Invented_Db",
		TestName = "SiteOnly_DatabaseName")]
	[TestCase("Cannot read ./Terrasoft.WebApp/ConnectionStrings.config", "./Terrasoft.WebApp",
		TestName = "SiteOnly_RelativePath")]
	[TestCase("Instance (SQL01\\SQLEXPRESS) is down", "SQL01\\SQLEXPRESS", TestName = "SiteOnly_ServerInstance")]
	[TestCase("Access denied\n   at Terrasoft.Core.Entities.Entity.Save()", "Terrasoft.Core", TestName = "SiteOnly_StackCut")]
	[TestCase("Invalid refresh token q8Zr_3kLmN0pQ7sT_vW2xY4zA6bC8dE0fG1hI3jK5lM", "q8Zr_3kLmN0pQ7sT",
		TestName = "SiteOnly_KeyLikeRun")]
	[Category("Unit")]
	[Description("The site-only rules run only in RedactSiteText: Redact - which also runs over clio's own CLI and MCP "
		+ "text and over serialized envelopes - leaves these shapes alone.")]
	public void SiteOnlyRules_ShouldApplyInRedactSiteTextOnly(string text, string fragment) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		redacted.Should().Contain(fragment, because: "Redact must not take a shape only a site's text may lose");
		site.Should().NotContain(fragment, because: "RedactSiteText must take it");
	}

	[Test]
	[Category("Unit")]
	[Description("RedactSiteText cuts at the stack frame, not at the first line break: a multi-line validation message "
		+ "and the parameter name after an escaped line break are the reason and stay.")]
	public void RedactSiteText_ShouldKeepLaterLines_WhenNoStackFrameOrPageFollows() {
		// Arrange
		const string text = "Value cannot be null.\\r\\nParameter name: password\nField \"Owner\" is required."
			+ "\n   at Terrasoft.Core.Entities.Entity.Save()";

		// Act
		string result = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		result.Should().Be("Value cannot be null.\\r\\nParameter name: password\nField \"Owner\" is required.",
			because: "only the stack trace after the reason goes");
	}

	[Test]
	[Category("Unit")]
	[Description("A null or empty site text gives an empty string, like Redact.")]
	public void RedactSiteText_ShouldReturnEmpty_WhenTextIsNullOrEmpty() {
		// Arrange

		// Act
		string fromNull = SensitiveErrorTextRedactor.RedactSiteText(null);
		string fromEmpty = SensitiveErrorTextRedactor.RedactSiteText(string.Empty);

		// Assert
		fromNull.Should().BeEmpty(because: "null degrades to a safe-to-concatenate string");
		fromEmpty.Should().BeEmpty(because: "there is nothing to redact");
	}

	#endregion
}
