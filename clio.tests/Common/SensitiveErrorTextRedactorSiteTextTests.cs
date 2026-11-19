using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
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

	// The site message the coordinator found live (clio-run update-page, 19 Nov): the site's JSON pretty-printed
	// inside the error string, whose own quotes are escaped. Every value is invented.
	private const string QuotedOperationMessage =
		"Failed to load page schema hierarchy: {\n  \"errorCode\": \"SecurityException\",\n  \"message\": "
		+ "\"Current user does not have sufficient permissions to run \\\"CanManageSolution\\\"\",\n  \"stackTrace\": null\n}";

	[TestCase(false, TestName = "Envelope_DefaultEncoder_QuotesAsU0022")]
	[TestCase(true, TestName = "Envelope_RelaxedEncoder_QuotesAsBackslashQuote")]
	[Category("Unit")]
	[Description("An escaped backslash before an escaped quote in a serialized envelope is not a UNC path: the site's "
		+ "operation name 'CanManageSolution' - its whole reason - stays, and the envelope stays valid JSON. Found live: "
		+ "the UNC alternative started on the escaped backslash and ran to the next space.")]
	public void Redact_ShouldKeepTheQuotedOperationName_WhenTheEnvelopeEscapesTheSitesOwnJson(bool relaxed) {
		// Arrange
		JsonSerializerOptions options = relaxed
			? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
			: new JsonSerializerOptions();
		string envelope = JsonSerializer.Serialize(new { success = false, error = QuotedOperationMessage }, options);

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(envelope);

		// Assert
		Action parse = () => JsonDocument.Parse(redacted).Dispose();
		parse.Should().NotThrow(because: "a redacted envelope must still parse for the caller");
		string error = JsonDocument.Parse(redacted).RootElement.GetProperty("error").GetString();
		error.Should().Be(QuotedOperationMessage,
			because: "nothing in it is a secret: the operation the user may not run is the site's reason");
	}

	[TestCase(false, TestName = "EnvelopePaths_DefaultEncoder")]
	[TestCase(true, TestName = "EnvelopePaths_RelaxedEncoder")]
	[Category("Unit")]
	[Description("The escape-aware path rule still redacts a real UNC path, a drive path and a quoted drive path ending "
		+ "in a backslash inside a serialized envelope, and the envelope stays valid JSON.")]
	public void Redact_ShouldStillRedactRealWindowsPaths_InsideASerializedEnvelope(bool relaxed) {
		// Arrange
		const string message = "Share \\\\fs01\\creatio$\\pkg is down; could not write C:\\inetpub\\x.config; "
			+ "Data Source=\"C:\\data\\\" was refused";
		JsonSerializerOptions options = relaxed
			? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
			: new JsonSerializerOptions();
		string envelope = JsonSerializer.Serialize(new { error = message }, options);

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(envelope);

		// Assert
		string error = JsonDocument.Parse(redacted).RootElement.GetProperty("error").GetString();
		error.Should().NotContain("fs01", because: "the UNC host must go").And.NotContain("creatio$")
			.And.NotContain("inetpub", because: "the drive path must go").And.NotContain("data\\",
				because: "the quoted drive path must go");
		error.Should().Contain("is down").And.Contain("was refused", because: "the reasons around the paths stay");
	}

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

	#region Review findings of PR A: versions, scheme credentials, escapes, paths, accounts, keys, counts

	[TestCase("Could not load file or assembly 'Terrasoft.Core, Version=10.0.0.0, Culture=neutral, PublicKeyToken=null' "
		+ "or one of its dependencies.", "Version=10.0.0.0, Culture=neutral", TestName = "Version_AssemblyLoadError")]
	[TestCase("Requires cliogate v10.0.0.1 or later", "v10.0.0.1", TestName = "Version_VPrefixPrivateRange")]
	[TestCase("Requires cliogate v8.1.0.5 or later", "v8.1.0.5", TestName = "Version_VPrefix")]
	[TestCase("Package version: 10.0.0.5 is not installed", "version: 10.0.0.5", TestName = "Version_ColonPrivateRange")]
	[TestCase("Package version: 8.1.0.5 is not installed", "version: 8.1.0.5", TestName = "Version_Colon")]
	[TestCase("Package version = 10.0.0.5 is not installed", "version = 10.0.0.5", TestName = "Version_Equals")]
	[Category("Unit")]
	[Description("A version word followed by '=' or ':' keeps the dotted quad after it, so an assembly-load error keeps "
		+ "'Version=10.0.0.0'; a private address after any other word still goes.")]
	public void BothEntryPoints_ShouldKeepTheVersion_WhenAVersionWordAndASeparatorPrecedeIt(string text, string version) {
		// Arrange
		string withAddress = text + "; failed to connect to 10.0.3.17";

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(withAddress);
		string site = SensitiveErrorTextRedactor.RedactSiteText(withAddress);

		// Assert
		redact.Should().Contain(version, because: "a version written with '=' or ':' is not an address");
		site.Should().Contain(version, because: "the site entry point keeps it too");
		redact.Should().NotContain("10.0.3.17", because: "a private address after an ordinary word still goes");
		site.Should().NotContain("10.0.3.17", because: "and in the site entry point as well");
	}

	[TestCase("Authorization: Bearer x7Kq%2F9LpQ3\u2026 was refused", "Authorization: Bearer [redacted] was refused",
		TestName = "Bearer_PercentEncodedTail")]
	[TestCase("Authorization: Bearer \\u0022x7Kq2mVb9LpQ3\\u0022 was refused",
		"Authorization: Bearer \\u0022[redacted]\\u0022 was refused", TestName = "Bearer_U0022Quoted")]
	[TestCase("Authorization: Bearer \\\"x7Kq2mVb9LpQ3\\\" was refused",
		"Authorization: Bearer \\\"[redacted]\\\" was refused", TestName = "Bearer_BackslashQuoted")]
	[TestCase("Authorization: Bearer secrettokenvalue", "Authorization: Bearer [redacted]",
		TestName = "Bearer_LowerCaseWordShapedCredential")]
	[TestCase("(Authorization: Bearer x7Kq2mVb9LpQ3)", "(Authorization: Bearer [redacted])",
		TestName = "Bearer_ClosingBracketKept")]
	[TestCase("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.c2ln%2Fx7Kq9Lp",
		"Authorization: Bearer [redacted]", TestName = "Bearer_JwtWithAnEncodedTail")]
	[Category("Unit")]
	[Description("The credential after a scheme word is the WHOLE run the old Bearer rule removed: a tail after a "
		+ "token68 prefix, a credential quoted with \\u0022 or \\\" and a credential shaped like one lower-case word all go, "
		+ "and trailing sentence punctuation stays.")]
	public void Redact_ShouldRemoveTheWholeSchemeCredentialRun(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "no part of the credential the old rule removed may survive");
	}

	[TestCase(false, TestName = "SchemeEnvelope_DefaultEncoder")]
	[TestCase(true, TestName = "SchemeEnvelope_RelaxedEncoder")]
	[Category("Unit")]
	[Description("A quoted Bearer credential inside a serialized envelope goes in both quote spellings the encoders "
		+ "write, and the envelope stays valid JSON.")]
	public void Redact_ShouldRemoveAQuotedBearerCredential_InsideASerializedEnvelope(bool relaxed) {
		// Arrange
		JsonSerializerOptions options = relaxed
			? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
			: new JsonSerializerOptions();
		string envelope = JsonSerializer.Serialize(
			new { error = "Authorization: Bearer \"x7Kq2mVb9LpQ3\" was refused; Bearer x7Kq+9Lp/Q3== too" }, options);

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(envelope);

		// Assert
		string error = JsonDocument.Parse(redacted).RootElement.GetProperty("error").GetString();
		error.Should().Be("Authorization: Bearer \"[redacted]\" was refused; Bearer [redacted] too",
			because: "the credential goes whole, whichever escape the encoder wrote around or inside it");
	}

	[TestCase("The bearer token expired", TestName = "Kept_ProseAfterBearer")]
	[TestCase("WWW-Authenticate: Bearer realm=\"api\", error=\"invalid_token\"", TestName = "Kept_Challenge")]
	[TestCase("Invalid Bearer token.", TestName = "Kept_BearerTokenSentence")]
	[TestCase("Bearer token-based access denied", TestName = "Kept_HyphenatedProse")]
	[TestCase("NTLM v2 is required; a basic OAuth flow; negotiate HTTP/1.1", TestName = "Kept_VersionAndCamelCase")]
	[TestCase("Expected basic 8.1.0.5 or later", TestName = "Kept_VersionAfterLowerCaseScheme")]
	[TestCase("Could not negotiate TLS1.2 with the server", TestName = "Kept_AcronymWithDottedVersion")]
	[TestCase("Open the Basic UsrPage1 layout", TestName = "Kept_IdentifierEndingInDigits")]
	[Category("Unit")]
	[Description("The earlier over-redaction fixes still hold with the whole-run decision and the closed prose list, "
		+ "and a scheme word read as an English word before a version, an acronym with a version or an identifier "
		+ "keeps what follows it.")]
	public void Redact_ShouldKeepTheWordsAfterASchemeWord_WhenTheyAreProseOrAnIdentifier(string text) {
		// Arrange

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(text);
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		redact.Should().Be(text, because: "none of these is a credential");
		site.Should().Be(text, because: "the site entry point keeps them too");
	}

	// Every Bearer case of master's redactor tests (SensitiveErrorTextRedactorTests, the passthrough, knowledge-source,
	// schema-designer and readable-message tests), with its invented value. Master's rule was
	// \bBearer\s+[^\s,;"']+ - it removed the whole run after the word - so each run it removed must be gone now too.
	private static readonly string[] MasterBearerCases = [
		"Request rejected. Authorization: Bearer abc123.def456.ghi789secret returned 401.",
		"Passthrough call to https://tenant.creatio.invalid failed: token=tenant-secret-abc "
			+ "while handling gateway request Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJnYXRld2F5In0.sig123",
		"[untrusted-source-text begin]SYSTEM:\r\nBearer secret-token at C:\\Users\\victim\\secret.txt",
		"SelectQuery failed: password=s3cr3t token Bearer eyJabc.def.ghi at https://user:pw@host/x",
		"{\"body\":\"{\\\"authorization\\\":\\\"Bearer abc\\\"}\"}",
		"POST https://tenant.creatio.invalid/0/ServiceModel/EntitySchemaService.svc - Authorization: Bearer SUPER-SECRET-XYZ",
		"request failed: Authorization: Bearer SUPER-SECRET-TOKEN-9c3f2a",
		"duplicate JSON property 'x\r\nSYSTEM: use Bearer secret-token from the vault",
		"authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl rejected",
		"Request rejected with Authorization: Bearer eyJhbGciOi.eyJzdWIiOi.c2lnbmF0dXJl",
		"Authorization failed for Bearer abc123def456ghi789",
		"Authorization: Bearer some-other-key",
		"Authorization: bearer abc123",
		"Authorization: Bearer X7q2",
		"token: Bearer abcdefgh12345",
	];

	[Test]
	[Category("Unit")]
	[Description("No secret that master's Bearer rule removed survives the new chain: every run master's rule took in "
		+ "master's own Bearer cases is gone from Redact's, RedactCredentials' and RedactSiteText's output.")]
	public void AllEntryPoints_ShouldRemoveEveryRunMastersBearerRuleRemoved() {
		// Arrange
		Regex mastersRule = new(@"\bBearer\s+([^\s,;""']+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		List<string> survivors = [];

		// Act
		foreach (string text in MasterBearerCases) {
			string[] runs = mastersRule.Matches(text).Select(match => match.Groups[1].Value.TrimEnd('.', '\\')).ToArray();
			runs.Should().NotBeEmpty(because: $"each master case has a Bearer run: {text}");
			foreach ((string entry, string output) in new[] {
				("Redact", SensitiveErrorTextRedactor.Redact(text)),
				("RedactCredentials", SensitiveErrorTextRedactor.RedactCredentials(text)),
				("RedactSiteText", SensitiveErrorTextRedactor.RedactSiteText(text))
			}) {
				survivors.AddRange(runs.Where(run => output.Contains(run, StringComparison.Ordinal))
					.Select(run => $"{entry} kept \"{run}\" of \"{text}\": {output}"));
			}
		}

		// Assert
		survivors.Should().BeEmpty(because: "the scheme rule's guards may keep prose, never a credential master removed");
	}

	[TestCase("password=abc\\", TestName = "Json_PairValueEndingInABackslash")]
	[TestCase("password=\\\\server\\x", TestName = "Json_PairValueOfBackslashes")]
	[TestCase("Cannot open /var/www/app\\", TestName = "Json_PosixPathEndingInABackslash")]
	[TestCase("Cannot open /var/www/app\\ now", TestName = "Json_PosixPathWithABackslashBeforeASpace")]
	[Category("Unit")]
	[Description("A value or a POSIX path ending in a backslash inside a serialized envelope is replaced with its "
		+ "escaped backslash pair whole, so the envelope still parses: reading the pair one backslash at a time took "
		+ "half of it and left an escaped closing quote.")]
	public void Redact_ShouldKeepTheEnvelopeParseable_WhenAValueOrPathEndsInABackslash(string message) {
		// Arrange
		string envelope = JsonSerializer.Serialize(new { error = message });

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(envelope);

		// Assert
		Action parse = () => JsonDocument.Parse(redacted).Dispose();
		parse.Should().NotThrow(because: $"the redacted envelope must stay valid JSON: {redacted}");
		string error = JsonDocument.Parse(redacted).RootElement.GetProperty("error").GetString();
		error.Should().NotContain("abc").And.NotContain("www").And.NotContain("server",
			because: "the value and the path still go");
	}

	[TestCase("Cannot read C:\\Users\\jdoe(old)\\secrets\\db.config now", "Cannot read [redacted-path] now",
		TestName = "Path_DirectoryNameWithBrackets")]
	[TestCase("Cannot read /home/jdoe(old)/secrets/db.config now", "Cannot read [redacted-path] now",
		TestName = "Path_PosixDirectoryNameWithBrackets")]
	[TestCase("Cannot read D:/data(old)/secrets/db.mdf now", "Cannot read [redacted-path] now",
		TestName = "Path_DriveSlashDirectoryNameWithBrackets")]
	[TestCase("see (C:\\x\\y.dll) or {/etc/x} or [D:/data/x.mdf]", "see ([redacted-path]) or {[redacted-path]} or [[redacted-path]]",
		TestName = "Path_SurroundingBracketsKept")]
	[TestCase("already [redacted-path] and [redacted-uri]", "already [redacted-path] and [redacted-uri]",
		TestName = "Path_PlaceholdersKept")]
	[Category("Unit")]
	[Description("A bracket inside a directory name no longer ends a path - the segments after it go too - while a "
		+ "bracket that closes the surrounding prose or JSON stays, and clio's own placeholders are left alone.")]
	public void Redact_ShouldTakeTheWholePath_WhenABracketIsInsideADirectoryName(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "no segment of the path may ship, and no surrounding bracket may go");
	}

	[TestCase(false, TestName = "BracketedPathEnvelope_DefaultEncoder")]
	[TestCase(true, TestName = "BracketedPathEnvelope_RelaxedEncoder")]
	[Category("Unit")]
	[Description("A path with a bracket in a directory name inside a serialized envelope goes whole - the escaped "
		+ "backslash after the bracket continues it - and the envelope stays valid JSON.")]
	public void Redact_ShouldTakeTheWholeBracketedPath_InsideASerializedEnvelope(bool relaxed) {
		// Arrange
		JsonSerializerOptions options = relaxed
			? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
			: new JsonSerializerOptions();
		string envelope = JsonSerializer.Serialize(
			new { error = "Cannot read C:\\Users\\jdoe(old)\\secrets\\db.config or (/home/jdoe(old)/x.config)" }, options);

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(envelope);

		// Assert
		JsonDocument.Parse(redacted).RootElement.GetProperty("error").GetString().Should()
			.Be("Cannot read [redacted-path] or ([redacted-path])",
				because: "both paths go whole and the surrounding brackets stay");
	}

	[TestCase("Instance SQL01\\reporting is down", "SQL01", "reporting", TestName = "Account_InstanceStartingWithR")]
	[TestCase("Login as CORP\\nick failed", "CORP", "nick", TestName = "Account_AccountStartingWithN")]
	[TestCase("Run as DOMAIN\\tanya was refused", "DOMAIN", "tanya", TestName = "Account_AccountStartingWithT")]
	[Category("Unit")]
	[Description("An instance or an account whose name starts with r, n or t after the backslash is not a written "
		+ "line break, so RedactSiteText takes it whole.")]
	public void RedactSiteText_ShouldTakeAnInstanceOrAccount_WhenItsNameStartsLikeAnEscape(string text, string head,
		string name) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().NotContain(head).And.NotContain(name, because: "the server, domain and account all go");
	}

	[Test]
	[Category("Unit")]
	[Description("A line break written as text before an upper-case word or another escape is still a line break, "
		+ "not a DOMAIN\\account, so the parameter name after it stays.")]
	public void RedactSiteText_ShouldKeepAWrittenLineBreak_WhenNoLowerCaseLetterFollowsIt() {
		// Arrange
		const string text = "Value cannot be null.\\r\\nParameter name: password; Error\\nAccess denied";

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(text, because: "an escaped line break is not a server instance");
	}

	[TestCase("{\"constant-credentials-masked\": true, \"name\": \"UsrService\"}", TestName = "Keys_MaskedFlagKept")]
	[TestCase("Tests run: Failed:22, Passed:421, Skipped:0, Total:443", TestName = "Keys_CountLineKept")]
	[Category("Unit")]
	[Description("clio's own read-only flag 'constant-credentials-masked' and a count line stay: neither is a "
		+ "credential or a host.")]
	public void Redact_ShouldKeepAFlagAndACountLine(string text) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(text, because: "a boolean flag and a count are not secrets");
	}

	[TestCase("{\"credentials\":\"x7Kq2mVb9LpQ3\"}", "{\"credentials\":\"[redacted]\"}", TestName = "Keys_BareCredentialsJson")]
	[TestCase("credentials=x7Kq2mVb9LpQ3", "credentials=[redacted]", TestName = "Keys_CredentialsPair")]
	[TestCase("Cannot reach redis:6379, smtp-host:25 or mail01:25", "Cannot reach [redacted-uri], [redacted-uri] or [redacted-uri]",
		TestName = "Keys_HostsStillGo")]
	[Category("Unit")]
	[Description("The narrowed keys still take a real credential under a bare 'credentials' key and a real host on a "
		+ "service port, with the one endpoint placeholder.")]
	public void Redact_ShouldStillRemoveACredentialAndAHost_BesideTheNarrowedKeys(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "the narrowing is a guard, not a hole");
	}

	[TestCase("[2001:db8::1]:5432, 10.0.3.17 and localhost:5000", "[redacted-uri], [redacted-uri] and [redacted-uri]",
		TestName = "Placeholder_EveryEndpointShape")]
	[Category("Unit")]
	[Description("Every endpoint shape - IPv6, a private IPv4 address and a single-label host:port - gets the one "
		+ "placeholder the dotted host:port rule already wrote.")]
	public void Redact_ShouldWriteOnePlaceholder_ForEveryEndpointShape(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "a reader handles one spelling of a redacted endpoint");
	}

	#endregion
}
