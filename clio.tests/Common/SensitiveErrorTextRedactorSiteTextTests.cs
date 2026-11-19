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
	[Category("Unit")]
	[Description("The earlier over-redaction fixes still hold with the whole-run decision and the closed prose list, "
		+ "and a scheme word read as an English word before a version, an acronym with a version or a one-hump "
		+ "CamelCase word keeps what follows it.")]
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
		// The shapes the round-2 delta review found leaking.
		"Authorization: Bearer MySecretToken2024 was refused",
		"Authorization: Bearer AdminApiKey was refused",
		"Authorization: Bearer 1700000000.123456789 was refused",
		"Authorization: Bearer ABCDE was refused",
		"Authorization: Bearer\u00A0x7Kq9LpQ3 was refused",
		"Authorization: Bearer x7Kq\\9Lp was refused",
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

	#region Round 2: the Bearer floor, scheme words in prose, escapes, clamps, versions, counts, keys

	// MASTER'S SCHEME RULE, EXACTLY as origin/master's SensitiveErrorTextRedactor.BearerTokenRegex declares it - the
	// pattern and its options, IgnoreCase included. Whatever run it removes is the floor: the new chain may not let
	// any of it through, except the documented trades below.
	private static readonly Regex MastersBearerRule = new(@"\bBearer\s+[^\s,;""']+",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	// The run master's rule removed, after "Bearer" and its gap.
	private static readonly Regex MastersBearerRun = new(@"^\S+\s+(?<run>.+)$", RegexOptions.Singleline);

	// The runs master removed that clio keeps ON PURPOSE, each with its reason. Nothing else may stay.
	private static readonly (string Name, Regex Run, string Reason)[] DocumentedBearerTrades = [
		("the word token", new Regex(@"^(?i:tokens?)(?:-(?:based|enabled|disabled|only|protected|secured|style|like|type|related|specific|level|scoped|bound|aware))?[.)\]}:!?]*$"),
			"'The bearer token expired', 'Invalid Bearer token.' and 'Bearer token-based access denied' (fixture C rows "
			+ "case-016, case-240, case-078) name the kind of credential, not the credential"),
		("a challenge parameter", new Regex(@"^(?i:realm|scope|error|error_description|error_uri|charset)="),
			"'WWW-Authenticate: Bearer realm=…' (case-255) is what the site asks for, not what a client sent"),
		("a placeholder", new Regex(@"^\[redacted(?:-[a-z]+)?\][.)\]}:!?]*$"),
			"the run is already one of clio's own placeholders"),
	];

	// Token shapes a Bearer credential takes, the ones the review found leaking first.
	private static readonly string[] BearerTokenCorpus = [
		"Bearer MySecretToken2024 was refused", "Bearer AdminApiKey was refused", "Bearer SecretToken was refused",
		"Bearer 1700000000.123456789 was refused", "Bearer ABCDE was refused", "Bearer ABCD", "bearer ABCD",
		"Bearer\u00A0x7Kq9LpQ3 was refused", "Bearer\u2003x7Kq9LpQ3 was refused", "Bearer x7Kq\\9Lp was refused",
		"Bearer secrettokenvalue was refused", "Bearer credentials was refused", "Bearer access was refused",
		"Bearer login was refused", "Bearer user was refused", "Bearer value was refused", "Bearer session was refused",
		"Bearer authentication failed", "Bearer x7Kq%2F9LpQ3 was refused", "Bearer \\\"x7Kq2mVb9LpQ3\\\" was refused",
		"Bearer \\u0022x7Kq2mVb9LpQ3\\u0022 was refused", "Bearer \"x7Kq2mVb9LpQ3\" was refused",
		"Bearer\tx7Kq9LpQ3 tab gap", "Bearer  x7Kq9LpQ3 double space", "Bearer\nx7Kq9LpQ3 on the next line",
		"Bearer x7Kq9LpQ3", "Bearer x7Kq9LpQ3...", "Bearer x7Kq9Lp&amp;Q3 end", "Bearer &quot;x7Kq9LpQ3&quot; end",
		"Bearer x7Kq9LpQ3\u00E9\u00E9 tail", "Bearer x7Kq!9LpQ3 tail", "Bearer x7Kq9LpQ3: next", "Bearer 2FA",
		"Bearer TLSv1.2x", "Bearer v2.0.0.1-secret", "Bearer OAuth", "Bearer UsrPage1", "Bearer 8.1.0.5",
		"Bearer TLS1.2", "Bearer v2", "BEARER X7Q2", "(Authorization: Bearer x7Kq9LpQ3)", "x-Bearer abc123def",
		"Bearer x7Kq\\u002B9Lp/Q3==", "Bearer x7Kq\\\\9Lp\\\" end", "Authorization: Bearer token", "Bearer tokens-secret",
		"Bearer token-based", "WWW-Authenticate: Bearer realm=\"api\", error=\"invalid_token\"",
		"Authorization: Bearer [redacted] was refused",
	];

	private static IEnumerable<string> FloorTexts() =>
		SiteTextRedactionFixtureTests.LoadRows().Select(row => row.Input).Concat(BearerTokenCorpus).Concat(MasterBearerCases);

	// The secret content of a run: its alphanumeric pieces of two or more characters, once JSON escapes and HTML
	// entities are taken out (those are the envelope's own syntax, not the credential).
	private static IEnumerable<string> RunPieces(string run) =>
		Regex.Split(Regex.Replace(run, @"\\u[0-9A-Fa-f]{4}|\\[nrtbf""\\/]|&(?:amp|quot|apos|lt|gt|#\d+);", " "), @"[^\p{L}\p{N}]+")
			.Where(piece => piece.Length >= 2);

	[Test]
	[Category("Unit")]
	[Description("THE BEARER FLOOR, as a property: master's exact scheme rule is applied to every fixture input, a "
		+ "corpus of token shapes and master's own Bearer cases, and no piece of any run it removes survives Redact, "
		+ "RedactCredentials, RedactSiteText or either fenced rendering - except the named, documented trades.")]
	public void AllEntryPoints_ShouldLetNothingMastersBearerRuleRemovedSurvive_ExceptTheNamedTrades() {
		// Arrange
		List<string> survivors = [];
		HashSet<string> tradesUsed = [];

		// Act
		foreach (string text in FloorTexts().Distinct(StringComparer.Ordinal)) {
			(string Entry, string Output)[] outputs = [
				("Redact", SensitiveErrorTextRedactor.Redact(text)),
				("RedactCredentials", SensitiveErrorTextRedactor.RedactCredentials(text)),
				("RedactSiteText", SensitiveErrorTextRedactor.RedactSiteText(text)),
				("RedactUntrustedOrNull", SensitiveErrorTextRedactor.RedactUntrustedOrNull(text) ?? string.Empty),
				("RedactUntrustedSiteTextOrNull", SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text) ?? string.Empty)
			];
			foreach (Match removed in MastersBearerRule.Matches(text)) {
				string run = MastersBearerRun.Match(removed.Value).Groups["run"].Value;
				(string Name, Regex Run, string Reason) trade = DocumentedBearerTrades.FirstOrDefault(entry => entry.Run.IsMatch(run));
				if (trade.Name is not null) {
					tradesUsed.Add(trade.Name);
					continue;
				}
				string rest = text.Remove(removed.Index, removed.Length);
				string core = run.TrimEnd('.', ')', ']', '}', ':', '!', '?');
				string[] pieces = RunPieces(run).Append(core).Where(piece => piece.Length >= 2)
					.Where(piece => !rest.Contains(piece, StringComparison.Ordinal)).Distinct().ToArray();
				foreach ((string entry, string output) in outputs) {
					string withoutPlaceholders = Regex.Replace(output, @"\[redacted(?:-[a-z]+)?\]", " ");
					survivors.AddRange(pieces.Where(piece => withoutPlaceholders.Contains(piece, StringComparison.Ordinal))
						.Select(piece => $"{entry} kept \"{piece}\" of the run \"{run}\" in \"{text}\": {output}"));
				}
			}
		}

		// Assert
		survivors.Should().BeEmpty(because: "nothing master's scheme rule removed may reach a caller now, except the "
			+ $"documented trades; {survivors.Count} survivors:\n{string.Join('\n', survivors)}\n");
		tradesUsed.Should().BeEquivalentTo(DocumentedBearerTrades.Select(trade => trade.Name),
			because: "every documented trade is one the fixture or the corpus actually exercises");
	}

	[TestCase("Could not negotiate protocol version")]
	[TestCase("Unable to negotiate connection with the server")]
	[TestCase("Negotiate handshake failed")]
	[TestCase("NTLM handshake failed")]
	[TestCase("Basic configuration is missing")]
	[TestCase("Open the Basic Information tab")]
	[TestCase("Visual Basic compiler error")]
	[TestCase("basic price list is empty")]
	[TestCase("Failed to negotiate SSL/TLS")]
	[TestCase("Could not negotiate TLS 1.2 with the server")]
	[TestCase("Basic authentication is disabled; NTLM negotiation failed; Digest authentication is off")]
	[TestCase("Basic auth-enabled endpoint")]
	[TestCase("WWW-Authenticate: Basic realm=\"Creatio\"")]
	[TestCase("WWW-Authenticate: Negotiate")]
	[TestCase("The client could not negotiate HTTP/2")]
	[TestCase("SVG icons must use static basic shapes without styles, animation, event handlers or external references.")]
	[TestCase("Emergency fallback only: login paired with 'uri' (basic auth).")]
	[TestCase("when the token endpoint refuses a Basic header, set Send client credentials in body")]
	[TestCase("IdentityService did not return a usable bearer token. Check the OAuth client credentials.")]
	[Category("Unit")]
	[Description("English sentences and clio's own console lines with a scheme word in them come out unchanged from "
		+ "Redact, RedactCredentials (clio's console lines) and RedactSiteText: outside a header position a prose "
		+ "word after Basic, NTLM or Negotiate stays, and 'bearer token' is a documented trade.")]
	public void SchemeWordsInProse_ShouldComeOutUnchanged(string text) {
		// Arrange

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(text);
		string console = SensitiveErrorTextRedactor.RedactCredentials(text);
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		redact.Should().Be(text, because: "Redact must not take an English word after a scheme word");
		console.Should().Be(text, because: "clio's own console line must stay readable");
		site.Should().Be(text, because: "the site's sentence is its reason");
	}

	[TestCase("Authorization: Basic configuration", "Authorization: Basic [redacted]", TestName = "Header_ProseWordAfterAuthorization")]
	[TestCase("Proxy-Authorization: Negotiate handshake", "Proxy-Authorization: Negotiate [redacted]",
		TestName = "Header_ProseWordAfterProxyAuthorization")]
	[TestCase("Authorization: NTLM abcdefghijkl", "Authorization: NTLM [redacted]", TestName = "Header_LowerCaseRun")]
	[TestCase("Open the Basic UsrPage1 layout", "Open the Basic [redacted] layout", TestName = "Prose_IdentifierWithDigits")]
	[TestCase("Open the Basic AdminApiKey layout", "Open the Basic [redacted] layout", TestName = "Prose_TwoInnerCapitals")]
	[TestCase("Expected NTLM 1700000000.123456789 here", "Expected NTLM [redacted] here", TestName = "Prose_LongDottedRun")]
	[TestCase("Expected Negotiate v2.0.0.1-secret here", "Expected Negotiate [redacted] here", TestName = "Prose_VersionWithATail")]
	[TestCase("Rejected NTLM dXNlcjpwYXNz now", "Rejected NTLM [redacted] now", TestName = "Prose_Base64OfLettersOnly")]
	[Category("Unit")]
	[Description("After Basic, NTLM or Negotiate in a header position only a challenge parameter or a word of the "
		+ "closed list stays; in prose a token with an inner capital and a digit, two inner capitals, a long dotted run "
		+ "or a version with a tail goes, and so does base64 made of letters only.")]
	public void SchemeCredential_ShouldGo_InHeaderPositionOrWhenItIsNotAProseWord(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "a credential-shaped run after a scheme word is a credential");
	}

	[TestCase("Message: line one\\nanother line", TestName = "Escape_LineBreakBeforeALowerCaseWord")]
	[TestCase("Error\\nancestor failed at step\\nrestart", TestName = "Escape_TwoLineBreaks")]
	[TestCase("Value cannot be null.\\r\\nParameter name: password", TestName = "Escape_CarriageReturnLineFeed")]
	[Category("Unit")]
	[Description("A written line break before a lower-case word is a line break, not DOMAIN\\account, when the word "
		+ "before it is not a server or domain name (capitals and digits).")]
	public void RedactSiteText_ShouldKeepAWrittenLineBreak_WhenTheWordBeforeIsNoServer(string text) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(text, because: "nothing here is a server instance or an account");
	}

	[TestCase("Login as CORP\\nick failed", "Login as [redacted] failed", TestName = "Account_DomainAndAnAccountStartingWithN")]
	[TestCase("Server SQL01\\nexus01 down", "Server [redacted] down", TestName = "Account_ServerAndAnInstanceStartingWithN")]
	[TestCase("Instance SQL01\\reporting is down", "Instance [redacted] is down", TestName = "Account_ServerAndAnInstanceStartingWithR")]
	[Category("Unit")]
	[Description("A server or domain name in capitals and digits before \\n, \\r or \\t and a lower-case word is still "
		+ "read as SERVER\\instance or DOMAIN\\account and goes whole.")]
	public void RedactSiteText_ShouldTakeTheAccount_WhenTheWordBeforeIsAServer(string text, string expected) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(expected, because: "the server, the domain and the account all go");
	}

	[TestCase("see (C:\\x\\y.dll)\\nNext line", "see ([redacted-path])\\nNext line", TestName = "PathEscape_LineBreakAfterBracket")]
	[TestCase("Could not load (C:\\app\\x.dll)\\r\\nParameter name: path", "Could not load ([redacted-path])\\r\\nParameter name: path",
		TestName = "PathEscape_CrLfAfterBracket")]
	[TestCase("see [C:\\x\\y.dll]\\tnext", "see [[redacted-path]]\\tnext", TestName = "PathEscape_TabAfterBracket")]
	[Category("Unit")]
	[Description("A closing bracket followed by a written \\n, \\r, \\t or \\u escape ends the path: only a backslash "
		+ "that starts no escape continues it into the next segment.")]
	public void Redact_ShouldEndThePathAtABracket_WhenAnEscapeFollowsIt(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "the escape and the text after it are not part of the path");
	}

	[Test]
	[Category("Unit")]
	[Description("Inside an envelope written with the default encoder, a bracket followed by the \\u0022 of the closing "
		+ "quote ends the path, so the quote stays.")]
	public void Redact_ShouldKeepTheQuoteAfterABracketedPath_InsideASerializedEnvelope() {
		// Arrange
		string envelope = JsonSerializer.Serialize(new { error = "Error: \"(C:\\app\\x.dll)\" failed" });

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(envelope);

		// Assert
		JsonDocument.Parse(redacted).RootElement.GetProperty("error").GetString().Should()
			.Be("Error: \"([redacted-path])\" failed", because: "the escaped quote after the bracket is not the path");
	}

	// A secret that crosses a clamp: a long URI that redaction shortens, then a quoted sign-in name across the clamp.
	private static string AcrossTheClamp(int clamp) =>
		"https://x.example/" + new string('a', clamp - 18 - 30) + " Login failed for user 'zq_invented_login'.";

	[Test]
	[Category("Unit")]
	[Description("The non-JSON preview's clamp before redaction cuts back to a word boundary, so a sign-in name that "
		+ "crosses the 4,000-character clamp goes whole instead of leaving its first letters.")]
	public void NonJsonPreview_ShouldNotSplitASecret_AtTheClampBeforeRedaction() {
		// Arrange
		string body = AcrossTheClamp(4_000);

		// Act
		string error = CreatioResponseError.DescribeNonJsonResponse(body);

		// Assert
		error.Should().NotContain("zq_", because: "the half of the name before the clamp must not survive");
	}

	[Test]
	[Category("Unit")]
	[Description("The fenced site rendering's 2,400-character clamp before redaction cuts back to a word boundary too.")]
	public void FencedSiteText_ShouldNotSplitASecret_AtTheClampBeforeRedaction() {
		// Arrange
		string text = AcrossTheClamp(2_400);

		// Act
		string fenced = SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text);

		// Assert
		fenced.Should().NotContain("zq_", because: "the half of the name before the clamp must not survive");
	}

	[Test]
	[Category("Unit")]
	[Description("site-response.message's clamp before redaction (eight times its 2,000-character cap) cuts back to a "
		+ "word boundary too.")]
	public void SiteResponseMessage_ShouldNotSplitASecret_AtTheClampBeforeRedaction() {
		// Arrange
		string body = JsonSerializer.Serialize(new { errorInfo = new { message = AcrossTheClamp(16_000) } });

		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(500, body));

		// Assert
		response.Message.Should().NotContain("zq_", because: "the half of the name before the clamp must not survive");
	}

	[TestCase("Cannot connect to Creatio: 10.0.3.17 refused", TestName = "Ipv4_AfterCreatioColon")]
	[TestCase("release: 10.0.3.17 is down", TestName = "Ipv4_AfterReleaseColon")]
	[TestCase("v=10.0.3.17", TestName = "Ipv4_AfterVEquals")]
	[TestCase("host build:10.0.3.17", TestName = "Ipv4_AfterBuildColon")]
	[Category("Unit")]
	[Description("A separator after a version word keeps an address only after 'version' or 'ver': after 'Creatio', "
		+ "'release', 'v' or 'build' with a ':' or '=' the private address goes.")]
	public void Redact_ShouldRemoveAPrivateAddress_AfterAVersionWordOtherThanVersionWithASeparator(string text) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().NotContain("10.0.3.17", because: "only 'version'/'ver' with a separator reads as a version");
	}

	[Test]
	[Category("Unit")]
	[Description("A count word in front of a database or cache port is still a host: 'tests:5432', 'ok:6379' and "
		+ "'pending:1433' go, while a count line keeps its counts.")]
	public void Redact_ShouldRemoveACountWordHost_OnADatabaseOrCachePort() {
		// Arrange
		const string text = "Cannot reach tests:5432 or ok:6379 or pending:1433; Tests run: Failed:22, Passed:421, Total:443";

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be("Cannot reach [redacted-uri] or [redacted-uri] or [redacted-uri]; Tests run: Failed:22, "
			+ "Passed:421, Total:443", because: "a word on a database or cache port is a host whatever the word");
	}

	[TestCase("{\"db_credentials\":\"sa:Passw0rd\"}", "{\"db_credentials\":\"[redacted]\"}", TestName = "Json_PrefixedCredentials")]
	[TestCase("{\"smtp.credentials\":\"user:Passw0rd\"}", "{\"smtp.credentials\":\"[redacted]\"}", TestName = "Json_DottedCredentials")]
	[TestCase("{\"constant-credentials-masked\": true}", "{\"constant-credentials-masked\": true}", TestName = "Json_MaskedFlag")]
	[Category("Unit")]
	[Description("The JSON rule takes 'credentials' in its compound spellings again; only clio's own '-masked' flag stays.")]
	public void Redact_ShouldTakeCompoundCredentialsKeys_ButNotTheMaskedFlag(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "a compound credentials key holds a secret; the masked flag is a boolean");
	}

	#endregion
}
