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
	[Description("OPTION B, DECLARED: master's Windows path rule runs first and frozen, and it reads the escaped backslash "
		+ "before an escaped quote as a UNC start - #1990's CanManageSolution fix is not kept. With the default encoder the "
		+ "operation name goes and the envelope still parses; with the relaxed encoder the name stays but the rule leaves a "
		+ "bare quote and the envelope no longer parses, exactly as on master.")]
	public void Redact_ShouldTreatAnEscapedOperationName_AsMastersChainDoes(bool relaxed) {
		// Arrange
		JsonSerializerOptions options = relaxed
			? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
			: new JsonSerializerOptions();
		string envelope = JsonSerializer.Serialize(new { success = false, error = QuotedOperationMessage }, options);

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(envelope);

		// Assert
		redacted.Should().Be(SensitiveErrorTextRedactor.Extra(SensitiveErrorTextRedactor.MasterChain.Redact(envelope)),
			because: "Redact is master's chain followed by span edits");
		Action parse = () => JsonDocument.Parse(redacted).Dispose();
		if (relaxed) {
			parse.Should().Throw<JsonException>(because: "master's path rule leaves a bare quote in a relaxed envelope");
			redacted.Should().Contain("CanManageSolution", because: "the rule stops at the bare quote before the name");
		}
		else {
			parse.Should().NotThrow(because: "the default encoder's \\u0022 is no quote character to the rule");
			JsonDocument.Parse(redacted).RootElement.GetProperty("error").GetString().Should().NotContain("CanManageSolution",
				because: "master's path rule takes the escaped name with the backslash before it");
		}
	}

	[TestCase(false, TestName = "EnvelopePaths_DefaultEncoder")]
	[TestCase(true, TestName = "EnvelopePaths_RelaxedEncoder")]
	[Category("Unit")]
	[Description("A real UNC path, a drive path and a quoted drive path ending in a backslash go inside a serialized "
		+ "envelope. Option B: master's greedy path rule may take the escapes after a path, so the envelope is not "
		+ "asserted to parse.")]
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
		redacted.Should().NotContain("fs01", because: "the UNC host must go").And.NotContain("creatio$")
			.And.NotContain("inetpub", because: "the drive path must go").And.NotContain("data",
				because: "the quoted drive path must go");
		redacted.Should().Contain("is down", because: "the reason before the paths stays");
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
		result.Should().Contain("already [redacted-path] and [redacted-uri]\nAuthorization: [redacted]",
			because: "clio's own placeholders and the line break survive; master's Bearer rule takes the scheme word");
		result.Should().NotContain("x7Kq2mVb9LpQ3").And.NotContain("redis:6379").And.NotContain("10.0.3.17",
			because: "the scheme credential, the single-label host and the private address go");
		document.RootElement.GetProperty("path").GetString().Should().Be("[redacted-path]",
			because: "a drive path ending in a backslash goes whole and leaves the JSON string closed");
	}

	#endregion

	#region Over-redactions fixed, each with a real credential beside it

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
	[TestCase(@"Cannot read ..\Terrasoft.WebApp\ConnectionStrings.config", @"..\Terrasoft.WebApp",
		TestName = "SiteOnly_BackslashRelativePath")]
	[TestCase("Cannot read C:ConnectionStrings.config", "C:ConnectionStrings.config", TestName = "SiteOnly_DriveRelativePath")]
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
		result.Should().Be("Value cannot be null.\\r\\nParameter name: password\nField \"Owner\" is required.\n[redacted]()",
			because: "only the stack trace after the reason goes, replaced by the placeholder, its brackets kept");
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

	[TestCase("Authorization: Bearer x7Kq%2F9LpQ3\u2026 was refused", "Authorization=[redacted] was refused", TestName = "Bearer_PercentEncodedTail")]
	[TestCase("Authorization: Bearer \\u0022x7Kq2mVb9LpQ3\\u0022 was refused", "Authorization=[redacted] was refused", TestName = "Bearer_U0022Quoted")]
	[TestCase("Authorization: Bearer \\\"x7Kq2mVb9LpQ3\\\" was refused", "Authorization=[redacted]\"[redacted]\\\" was refused", TestName = "Bearer_BackslashQuoted")]
	[TestCase("Authorization: Bearer secrettokenvalue", "Authorization=[redacted]", TestName = "Bearer_LowerCaseWordShapedCredential")]
	[TestCase("(Authorization: Bearer x7Kq2mVb9LpQ3)", "(Authorization=[redacted]", TestName = "Bearer_ClosingBracketKept")]
	[TestCase("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.c2ln%2Fx7Kq9Lp", "Authorization=[redacted]", TestName = "Bearer_JwtWithAnEncodedTail")]
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
		+ "write. Option B: master's Bearer and pair rules take the scheme word and may leave a bare quote, so the envelope "
		+ "is not asserted to parse.")]
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
		redacted.Should().NotContain("x7Kq", because: "the credential goes whole, whichever escape the encoder wrote");
		redacted.Should().Contain("was refused").And.Contain("too", because: "the words around it stay");
	}

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
	[TestCase("Cannot read D:/data(old)/secrets/db.mdf now", "Cannot read [redacted-path]([redacted-path])[redacted-path] now", TestName = "Path_DriveSlashDirectoryNameWithBrackets")]
	[TestCase("see (C:\\x\\y.dll) or {/etc/x} or [D:/data/x.mdf]", "see ([redacted-path] or {[redacted-path] or [D:[redacted-path]", TestName = "Path_SurroundingBracketsKept")]
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
		redacted.Should().NotContain("jdoe").And.NotContain("secrets").And.NotContain("db.config",
			because: "both paths go whole; master's path rule also takes the closing bracket after the second");
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
	[TestCase("Cannot reach redis:6379, smtp-host:25 or mail01:25", "Cannot reach [redacted-uri], [redacted-uri]host=[redacted] or [redacted-uri]", TestName = "Keys_HostsStillGo")]
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

	[TestCase("see (C:\\x\\y.dll)\\nNext line", "see ([redacted-path] line", TestName = "PathEscape_LineBreakAfterBracket")]
	[TestCase("Could not load (C:\\app\\x.dll)\\r\\nParameter name: path", "Could not load ([redacted-path] name: path", TestName = "PathEscape_CrLfAfterBracket")]
	[TestCase("see [C:\\x\\y.dll]\\tNext", "see [[redacted-path]", TestName = "PathEscape_TabBeforeACapitalAfterBracket")]
	[TestCase("see (C:\\x\\y.dll)\\n", "see ([redacted-path]", TestName = "PathEscape_LineBreakAtTheEndAfterBracket")]
	[TestCase("see (C:\\x\\y.dll)\\n next", "see ([redacted-path] next", TestName = "PathEscape_LineBreakBeforeASpaceAfterBracket")]
	[Category("Unit")]
	[Description("A closing bracket followed by a written \\n, \\r or \\t before a capital letter, another backslash, "
		+ "whitespace or the end - or by a \\u escape - ends the path: that is a line break, a tab or an escaped quote.")]
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
			.Be("Error: \"([redacted-path] failed",
				because: "master's path rule takes the closing bracket and the escaped quote after it (option B, declared)");
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

	[TestCase("Cannot reach ok:6379 now", "Cannot reach [redacted-uri] now", TestName = "CountWord_AloneOnACachePort")]
	[TestCase("Cannot reach tests:5432 now", "Cannot reach [redacted-uri] now", TestName = "CountWord_AloneOnADatabasePort")]
	[TestCase("Tests run: Failed:22, Passed:421, Total:443", "Tests run: Failed:22, Passed:421, Total:443",
		TestName = "CountWord_CountLine")]
	[TestCase("Passed:5432, Total:5432", "Passed:5432, Total:5432", TestName = "CountWord_CountLineOnDatabasePorts")]
	[TestCase("Total:3306 Passed:8080 Skipped:1433", "Total:3306 Passed:8080 Skipped:1433", TestName = "CountWord_CountLineOnServicePorts")]
	[TestCase("Cannot reach tests:4000 now", "Cannot reach tests:4000 now", TestName = "CountWord_AloneOnAnOrdinaryPort")]
	[TestCase("Passed:5432, Total:5432\nCannot reach ok:6379", "Passed:5432, Total:5432\nCannot reach [redacted-uri]",
		TestName = "CountWord_OtherLineIsNoCountLine")]
	[TestCase("Passed:5432, Total:5432\\nCannot reach ok:6379", "Passed:5432, Total:5432\\nCannot reach [redacted-uri]",
		TestName = "CountWord_WrittenLineBreakEndsTheLine")]
	[Category("Unit")]
	[Description("A count word is a count when another count word is on the same line; alone, 'word:port' takes the "
		+ "general single-label rule, so a database or cache port makes it a host and an ordinary port does not.")]
	public void Redact_ShouldReadACountWordByItsLine(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "a count line keeps its counts, and a lone word on a service port is a host");
	}

	[TestCase("{\"db_credentials\":\"sa:Passw0rd\"}", "{\"db_credentials\":\"[redacted]\"}", TestName = "Json_PrefixedCredentials")]
	[TestCase("{\"smtp.credentials\":\"user:Passw0rd\"}", "{\"smtp.credentials\":\"[redacted]\"}", TestName = "Json_DottedCredentials")]
	[TestCase("{\"constant-credentials-masked\": true}", "{\"constant-credentials-masked\": true}", TestName = "Json_MaskedFlag")]
	[TestCase("{\"credentials.masked\":\"x7Kq9LpQ3\"}", "{\"credentials.masked\":\"[redacted]\"}", TestName = "Json_DottedMaskedSuffix")]
	[TestCase("{\"credentials-masked-value\":\"x7Kq9\"}", "{\"credentials-masked-value\":\"[redacted]\"}",
		TestName = "Json_MaskedInTheMiddle")]
	[TestCase("{\"Constant-Credentials-Masked\":\"x7Kq9\"}", "{\"Constant-Credentials-Masked\":\"[redacted]\"}",
		TestName = "Json_MaskedFlagInAnotherCase")]
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

	#region Round 3: folder names after a bracket, status words, header keys, count lines, quoted clamps

	[TestCase("Cannot read C:\\Users\\jdoe(old)\\temp\\db.config now", "Cannot read [redacted-path] now", TestName = "Folder_TempAfterBracket")]
	[TestCase("Cannot read C:\\Users\\jdoe(old)\\reports\\q3.xlsx now", "Cannot read [redacted-path] now", TestName = "Folder_ReportsAfterBracket")]
	[TestCase("Cannot read C:\\data[1]\\nightly\\db.bak now", "Cannot read [redacted-path] now", TestName = "Folder_NightlyAfterBracket")]
	[TestCase("see [C:\\x\\y.dll]\\tnext", "see [[redacted-path]", TestName = "Folder_TabBeforeALowerCaseWordIsReadAsAFolder")]
	[TestCase("Cannot read C:\\a(b)\\r\\nNext", "Cannot read [redacted-path]", TestName = "Folder_CrLfStillEnds")]
	[Category("Unit")]
	[Description("After a closing bracket, a written \\r, \\n or \\t before a lower-case letter is the start of the next "
		+ "folder ('\\temp', '\\reports', '\\nightly'), so the path runs on: when the two readings disagree, the one that "
		+ "removes the path wins.")]
	public void BothEntryPoints_ShouldTakeAFolderNamedLikeAnEscape_AfterABracket(string text, string expected) {
		// Arrange

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(text);
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		redact.Should().Be(expected, because: "no folder of the path may ship");
		site.Should().Be(expected, because: "the site entry point takes the whole path too");
	}

	[TestCase("Error 404\\nnot found", TestName = "Status_NumberBeforeALineBreak")]
	[TestCase("line 42\\nthe value is too long", TestName = "Status_LineNumberBeforeALineBreak")]
	[TestCase("Status: FAILED\\nreason: timeout", TestName = "Status_FailedBeforeALineBreak")]
	[TestCase("Result: OK\\nnothing to do", TestName = "Status_OkBeforeALineBreak")]
	[TestCase("Level WARNING\\nthe disk is full", TestName = "Status_WarningBeforeALineBreak")]
	[TestCase("Flag TRUE\\nthen stop", TestName = "Status_TrueBeforeALineBreak")]
	[TestCase("Code 500\\treason", TestName = "Status_NumberBeforeATab")]
	[Category("Unit")]
	[Description("A written line break after a number or an all-capitals status word is a line break: neither is a "
		+ "server or domain name, so the reason words after it stay.")]
	public void RedactSiteText_ShouldKeepTheReason_AfterANumberOrAStatusWordAndALineBreak(string text) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(text, because: "a number and a status word are not a server, and the reason must stay");
	}

	[TestCase("Server DB-PROD\\nightly is down", "Server [redacted] is down", TestName = "Server_HyphenatedHostLabel")]
	[TestCase("Login as CORP01\\ntanya failed", "Login as [redacted] failed", TestName = "Server_DomainWithDigits")]
	[Category("Unit")]
	[Description("A host-label server name - capitals with digits or a hyphen - before \\n and a lower-case word is "
		+ "still SERVER\\instance.")]
	public void RedactSiteText_ShouldTakeTheInstance_AfterAHostLabel(string text, string expected) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(expected, because: "a host label before the escape names a server");
	}

	[TestCase("Primary key: Basic info is shown", TestName = "Header_KeyWordInProse")]
	[TestCase("Expected Basic secrettokenvalue here", TestName = "KnownKeep_LowerCaseRunAfterBasicInProse")]
	[Category("Unit")]
	[Description("'key' or 'auth' and a colon in a sentence is no header position and no credential pair, so the prose "
		+ "word after the scheme word stays. A lower-case run after Basic in a sentence is a known keep: master had no "
		+ "floor there.")]
	public void SchemeWord_ShouldKeepAProseWord_AfterAKeyWordInASentence(string text) {
		// Arrange

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(text);
		string console = SensitiveErrorTextRedactor.RedactCredentials(text);

		// Assert
		redact.Should().Be(text, because: "the key word is prose here, not a header or a credential key");
		console.Should().Be(text, because: "clio's console line keeps it too");
	}

	[TestCase("WWW-Authenticate: Negotiate handshake", "WWW-Authenticate: Negotiate [redacted]", TestName = "Header_WwwAuthenticate")]
	[TestCase("{\"auth\":\"x\", \"note\": \"see \\\"token\\\": Basic abcdefgh\"}", "{\"auth\":\"[redacted]\", \"note\": \"see \\\"token\\\": Basic [redacted]\"}",
		TestName = "Header_QuotedCredentialKey")]
	[Category("Unit")]
	[Description("A real header name or a quoted credential key before the scheme word is a header position: only a "
		+ "challenge parameter or the closed list stays there.")]
	public void SchemeWord_ShouldRemoveTheWord_InARealHeaderPosition(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "in a header position the word after the scheme is the credential");
	}

	// A quoted sign-in name with spaces that crosses a clamp: the last space before the clamp is inside the quotes.
	private static string QuotedNameAcrossTheClamp(int clamp) =>
		"https://x.example/" + new string('a', clamp - 18 - 30) + " Login failed for user 'zq invented login'.";

	[Test]
	[Category("Unit")]
	[Description("A clamp before redaction that falls inside an open quote cuts back to before the opening quote, so a "
		+ "quoted name with a space is not split either - in the non-JSON preview and in the fenced rendering.")]
	public void Clamps_ShouldNotSplitAQuotedNameWithASpace() {
		// Arrange
		string preview = QuotedNameAcrossTheClamp(4_000);
		string fencedInput = QuotedNameAcrossTheClamp(2_400);

		// Act
		string error = CreatioResponseError.DescribeNonJsonResponse(preview);
		string fenced = SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(fencedInput);

		// Assert
		error.Should().NotContain("zq", because: "the part of the name before the clamp must not survive the preview");
		fenced.Should().NotContain("zq", because: "the part of the name before the clamp must not survive the fence");
		fenced.Should().Contain("Login failed for user", because: "the reason before the quote stays");
	}

	#endregion

	#region Round 4: credential keys keep no prose, server names with _ . $, linear time, the masked flag's value

	[TestCase("password: Digest secret", "Digest secret", TestName = "CredentialKey_PasswordDigest")]
	[Category("Unit")]
	[Description("After a credential key - password, secret, cookie, token, access_token, client_secret, … - the value "
		+ "is the secret whatever it looks like: a scheme word and a prose-shaped word after the key's colon go in every "
		+ "entry point.")]
	public void AllEntryPoints_ShouldRemoveTheValue_AfterACredentialKeyAndASchemeWord(string text, string secret) {
		// Arrange

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(text);
		string console = SensitiveErrorTextRedactor.RedactCredentials(text);
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		redact.Should().NotContain(secret, because: "a credential key's value is the secret");
		console.Should().NotContain(secret, because: "the console variant removes it too");
		site.Should().NotContain(secret, because: "the site entry point removes it too");
	}

	[TestCase("The token: NTLM handshake failed", "The token=[redacted] handshake failed", TestName = "Trade_TokenKeyInASentence")]
	[TestCase("The secret: Negotiate later", "The secret=[redacted] later", TestName = "Trade_SecretKeyInASentence")]
	[Category("Unit")]
	[Description("THE TRADE, removing wins: a credential key in a sentence ('The token:', 'The secret:') cannot be told "
		+ "from a credential pair, so its value goes with the words after it. Only 'key' and 'auth' keep a scheme word and "
		+ "a prose word after their colon.")]
	public void Redact_ShouldRemoveTheValue_AfterACredentialKeyInASentence(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "after a credential key, removing wins over reading the sentence");
	}

	[TestCase("Instance SQL_PROD\\reporting is down", "SQL_PROD", "reporting", TestName = "ServerName_Underscore")]
	[TestCase("Instance SQL01.CORP.LOCAL\\reporting is down", "CORP.LOCAL", "reporting", TestName = "ServerName_Dotted")]
	[TestCase("Login as CORP.LOCAL\\nick failed", "CORP.LOCAL", "nick", TestName = "ServerName_DottedDomain")]
	[TestCase("Run as SRV$\\nick now", "SRV$", "nick", TestName = "ServerName_Dollar")]
	[Category("Unit")]
	[Description("A server or domain name with '_', '.' or '$' before \\r, \\n or \\t and a lower-case word is still "
		+ "SERVER\\account, so it goes whole.")]
	public void RedactSiteText_ShouldTakeTheAccount_AfterAServerNameWithUnderscoreDotOrDollar(string text, string server,
		string account) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().NotContain(server).And.NotContain(account, because: "the server and the account both go");
	}

	[Test]
	[Category("Unit")]
	[Description("A 30,000-character run of capitals and digits passes RedactSiteText whole: the whole chain neither "
		+ "times out (the bare placeholder) nor changes it. The length stays well inside every rule's 1-second budget on a "
		+ "slow agent; linearity itself is timed on the server-name rule alone below.")]
	public void RedactSiteText_ShouldKeepALongRunOfCapitals_WithoutFailingClosed() {
		// Arrange
		string text = "Value " + string.Concat(Enumerable.Repeat("ABCDEF0123456789", 30_000 / 16)) + " here";

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(text, because: "nothing in it is a secret, and the chain must not fail closed on it");
	}

	[Test]
	[Category("Unit")]
	[Description("The server-name rule ALONE reads a 100,000-character run of capitals and digits in linear time: it "
		+ "looks for its backslash before it looks back. The quadratic form took about 383 ms on 16,000 characters, so "
		+ "100,000 would take about 15 s; the bound is generous for a slow agent and does not depend on other rules.")]
	public void WindowsInstanceRule_ShouldReadALongRunOfCapitals_InLinearTime() {
		// Arrange
		Regex rule = (Regex)typeof(SensitiveErrorTextRedactor)
			.GetMethod("WindowsInstanceRegex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
			.Invoke(null, null)!;
		string text = "Value " + string.Concat(Enumerable.Repeat("ABCDEF0123456789", 100_000 / 16)) + " here";
		rule.IsMatch("warm up");
		System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

		// Act
		string replaced = rule.Replace(text, "[x]");
		watch.Stop();

		// Assert
		replaced.Should().Be(text, because: "a run of capitals with no backslash is no server instance");
		watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(750), because: "the rule must stay linear on a long run");
	}

	[TestCase("X_Key: Basic abcdefghijkl", TestName = "JoinedKey_XUnderscoreKey")]
	[Category("Unit")]
	[Description("A key or auth word joined by '-' or '_' to the word before it is a header name or a compound key, "
		+ "not prose: its value after the scheme word goes in every entry point.")]
	public void AllEntryPoints_ShouldRemoveTheValue_AfterAJoinedKeyOrAuthHeader(string text) {
		// Arrange
		string value = text[(text.LastIndexOf(' ') + 1)..];

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(text);
		string console = SensitiveErrorTextRedactor.RedactCredentials(text);
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		redact.Should().NotContain(value, because: "a joined key's value is the credential");
		console.Should().NotContain(value, because: "the console variant removes it too");
		site.Should().NotContain(value, because: "the site entry point removes it too");
	}

	[TestCase("{\"constant-credentials-masked\":\"x7Kq9LpQ3\"}", "{\"constant-credentials-masked\":\"[redacted]\"}",
		TestName = "MaskedFlag_StringValueGoes")]
	[TestCase("{\"constant-credentials-masked\": false}", "{\"constant-credentials-masked\": false}", TestName = "MaskedFlag_FalseStays")]
	[TestCase("{\"constant-credentials-masked\": true}", "{\"constant-credentials-masked\": true}", TestName = "MaskedFlag_TrueStays")]
	[Category("Unit")]
	[Description("clio's own 'constant-credentials-masked' flag is left alone only when its value is true or false; "
		+ "any other value under that key goes.")]
	public void Redact_ShouldKeepTheMaskedFlag_OnlyWhenItIsABoolean(string text, string expected) {
		// Arrange

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(expected, because: "only a boolean is the flag");
	}

	[Test]
	[Category("Unit")]
	[Description("THE COUNT-LINE TRADE, pinned: a host on a count word's name ('tests:5432', 'ok:6379', 'pending:1433') "
		+ "on the same line as a count summary reads as a count and stays. Master removed none of them either.")]
	public void Redact_ShouldKeepCountWordHosts_OnACountSummaryLine_AsTheDocumentedTrade() {
		// Arrange
		const string text = "Cannot reach tests:5432 or ok:6379 or pending:1433; Tests run: Failed:22, Passed:421, Total:443";

		// Act
		string redacted = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redacted.Should().Be(text, because: "the documented trade keeps a count word on a count line, host or not");
	}

	#endregion

	#region Backslash, drive-relative and escaped relative paths (rule 6, site part)

	// The live case (odata-create against a stub Windows site, 20 Nov): the refused insert named its configuration
	// file by a backslash relative path, and odata-update and odata-delete carried it JSON-escaped.
	private const string LiveInsertRefusal = @"Insert refused. See ..\..\Terrasoft.Configuration\Web.config";

	private const string LiveInsertRefusalEscaped = @"Insert refused. See ..\\..\\Terrasoft.Configuration\\Web.config";

	[TestCase(LiveInsertRefusal, "Insert refused. See [redacted-path]", TestName = "Backslash_LiveCase_AtTheEnd")]
	[TestCase(@"See ..\x\y.config now", "See [redacted-path] now", TestName = "Backslash_TwoDots_BeforeASpace")]
	[TestCase(@"See .\conf\x.config now", "See [redacted-path] now", TestName = "Backslash_OneDot")]
	[TestCase(@"See ..\..\Terrasoft.Configuration\Web.config, then retry", "See [redacted-path], then retry",
		TestName = "Backslash_BeforeAComma")]
	[TestCase(@"See ..\..\Terrasoft.Configuration\Web.config\nNext step", @"See [redacted-path]\nNext step",
		TestName = "Backslash_BeforeAWrittenLineBreak")]
	[TestCase(@"See (..\x\y) now", "See ([redacted-path]) now", TestName = "Backslash_InsideBrackets")]
	[TestCase(@"See [..\x] now", "See [[redacted-path]] now", TestName = "Backslash_InsideSquareBrackets")]
	[TestCase(@"See '..\x\y.config' now", "See '[redacted-path]' now", TestName = "Backslash_InsideSingleQuotes")]
	[TestCase(@"See ""..\x"" now", @"See ""[redacted-path]"" now", TestName = "Backslash_InsideDoubleQuotes")]
	[TestCase(@"Copy ..\a\b.config to .\c\d.config now", "Copy [redacted-path] to [redacted-path] now",
		TestName = "Backslash_TwiceInOneText")]
	[TestCase(@"See ..\temp\Web.config now", "See [redacted-path] now", TestName = "Backslash_FirstFolderLikeAnEscape")]
	[TestCase(@"See .\nlog.config now", "See [redacted-path] now", TestName = "Backslash_FileNameLikeAnEscape")]
	[TestCase(@"See ..\x\nightly.bak now", "See [redacted-path] now", TestName = "Backslash_LaterFolderLikeAnEscape")]
	[TestCase(@"See .\conf/x.config now", "See [redacted-path] now", TestName = "Backslash_MixedSeparators")]
	[TestCase("See ..\\jdoe(old)\\secrets\\db.config now", "See [redacted-path]([redacted-path])[redacted-path] now", TestName = "Backslash_BracketInAFolder")]
	[TestCase(@"Server .\SQLEXPRESS is down", "Server [redacted-path] is down", TestName = "Backslash_LocalInstance")]
	[TestCase(@"See ..\db01.internal:1433\x now", "See [redacted-path] now", TestName = "Backslash_HostInsideThePath")]
	[TestCase(@"See ..\Web.config.", "See [redacted-path].", TestName = "Backslash_FullStopStays")]
	[Category("Unit")]
	[Description("A backslash relative path a Windows site writes goes WHOLE, its leading dots included, wherever it "
		+ "stands: at the end, before a space, a comma, a written line break, a closing bracket or a quote, twice in one "
		+ "text, with a folder named like an escape, mixed with '/' and with a host inside. It used to survive whole.")]
	public void RedactSiteText_ShouldTakeTheWholeBackslashRelativePath(string text, string expected) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(expected, because: "no piece of a site's relative path may ship, and the words around it stay");
	}

	[TestCase(LiveInsertRefusalEscaped, "Insert refused. See [redacted-path]", TestName = "Escaped_LiveCase_AtTheEnd")]
	[TestCase(@"See ..\\x\\y.config now", "See [redacted-path] now", TestName = "Escaped_TwoDots")]
	[TestCase(@"See .\\conf\\x.config now", "See [redacted-path] now", TestName = "Escaped_OneDot")]
	[TestCase("See ..\\\\Web.config, then retry", "See [redacted-path] then retry", TestName = "Escaped_BeforeAComma")]
	[TestCase("See ..\\\\..\\nNext step", "See [redacted-path] step", TestName = "Escaped_BeforeAJsonLineBreak")]
	[TestCase("See (..\\\\x\\\\y) now", "See ([redacted-path] now", TestName = "Escaped_InsideBrackets")]
	[TestCase("See [..\\\\x] now", "See [[redacted-path] now", TestName = "Escaped_InsideSquareBrackets")]
	[TestCase(@"See '..\\y.config' now", "See '[redacted-path]' now", TestName = "Escaped_InsideSingleQuotes")]
	[TestCase("See \\\"..\\\\x\\\" now", "See \\\"[redacted-path]\" now", TestName = "Escaped_InsideEscapedQuotes")]
	[TestCase(@"See \u0022..\\x\u0022 now", @"See \u0022[redacted-path] now", TestName = "Escaped_AfterAUnicodeQuote")]
	[TestCase(@"Copy ..\\a\\b.config to .\\c\\d.config now", "Copy [redacted-path] to [redacted-path] now",
		TestName = "Escaped_TwiceInOneText")]
	[TestCase(@"See .\\conf/x.config now", "See [redacted-path] now", TestName = "Escaped_MixedSeparators")]
	[TestCase(@"See ..\\jdoe(old)\\secrets\\db.config now", "See [redacted-path] now", TestName = "Escaped_BracketInAFolder")]
	[TestCase(@"Server .\\SQLEXPRESS is down", "Server [redacted-path] is down", TestName = "Escaped_LocalInstance")]
	[TestCase(@"See ..\\db01.internal:1433\\x now", "See [redacted-path] now", TestName = "Escaped_HostInsideThePath")]
	[TestCase(@"See ..\\..[redacted-path] and .[redacted-path]", "See [redacted-path] and [redacted-path]",
		TestName = "Escaped_TextRedactedBeforeThisRule")]
	[Category("Unit")]
	[Description("The same path JSON-escaped, as the OData error's raw text carries it to odata-update and "
		+ "odata-delete: the WHOLE path goes, the leading '..\\\\..' included, and the escaped quotes and line break "
		+ "around it stay. Redact's UNC rule used to take the tail and leave '..\\\\..[redacted-path]'; a text that "
		+ "already holds that remnant loses it too. Where that UNC rule took the tail, it - not this rule - decided "
		+ "where the path ends, so the line-break case uses a path it cannot start on.")]
	public void RedactSiteText_ShouldTakeTheWholeEscapedRelativePath(string text, string expected) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(expected, because: "the escaped spelling must lose the whole path, dots included");
	}

	[TestCase(@"See .\\conf/x\y.config now", "See [redacted-path] now", TestName = "Mixed_EscapedThenDecodedSeparator")]
	[TestCase(@"See ..\x\\y.config now", "See [redacted-path] now", TestName = "Mixed_DecodedThenEscapedSeparator")]
	[TestCase(@"See ..\\\\x\\\\y.config now", "See [redacted-path] now", TestName = "Mixed_EscapedTwice")]
	[TestCase("See ..\\x\\y.config\\u0022Password=hunter2 now", "See [redacted] now", TestName = "Glued_EscapeAndAPairAfterABackslashPath")]
	[TestCase(@"Reach SQL01\SQLEXPRESS.\x\y.config now", "Reach [redacted][redacted-path] now",
		TestName = "Placeholder_BackslashPathAfterAnInstance")]
	[TestCase("See ../x/y.config\\u0022Password=hunter2 now", "See [redacted] now", TestName = "Glued_EscapeAndAPairAfterAForwardPath")]
	[Category("Unit")]
	[Description("A path whose separators mix one, two or four backslashes goes whole; so does a \\uXXXX glued to more "
		+ "text after it - the Redact pair rule cannot start after such an escape, so 'Password=hunter2' there goes only "
		+ "with the path (master's chain left '[redacted-path]]=hunter2' after a forward path) - and a path right after the "
		+ "placeholder the server-instance rule wrote.")]
	public void RedactSiteText_ShouldTakeThePath_WhenSpellingsMixOrAnEscapeIsGluedToIt(string text, string expected) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(expected, because: "no piece of the path, and nothing glued to it, may ship");
	}

	[TestCase(@"See ../x/y.config\\\u0022CORP\\jdoe now", "See [redacted-path] now",
		TestName = "Kept_GluedEscapeAndAnAccountAfterAForwardPath")]
	[TestCase(@"See ../x/y.config\\\u0022;(SQL01\\SQLEXPRESS, now", "See [redacted-path] now",
		TestName = "Kept_GluedEscapeAndAnEscapedInstanceAfterAForwardPath")]
	[TestCase(@"Reach SQL01\SQLEXPRESS../x/y.config now", "Reach [redacted][redacted-path] now",
		TestName = "Kept_ForwardPathAfterAnInstance")]
	[Category("Unit")]
	[Description("GUARD, found by the differential run of 20 Nov: what the forward-slash rule removed before the fix it "
		+ "still removes - a \\uXXXX glued to more text after the path, with the account or server instance glued to it "
		+ "that no rule before this one can start on, and a path right after the server-instance placeholder.")]
	public void RedactSiteText_ShouldKeepRemovingWhatAForwardPathTookWithIt(string text, string expected) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(expected, because: "stopping the path at the escape showed a value the old rule hid");
	}

	[TestCase(@"See C:foo\bar.config now", "See [redacted-path] now", TestName = "Drive_RelativeWithAFolder")]
	[TestCase(@"See C:foo\\bar.config now", "See [redacted-path] now", TestName = "Drive_RelativeWithAFolderEscaped")]
	[TestCase(@"See C:Web.config now", "See [redacted-path] now", TestName = "Drive_RelativeFileName")]
	[TestCase(@"Use C:Web.config.", "Use [redacted-path].", TestName = "Drive_RelativeFileNameAtASentenceEnd")]
	[TestCase(@"See (C:Web.config) now", "See ([redacted-path]) now", TestName = "Drive_RelativeInsideBrackets")]
	[TestCase(@"See ""C:foo\bar.config"" now", @"See ""[redacted-path]"" now", TestName = "Drive_RelativeInsideQuotes")]
	[TestCase("See \\\"C:foo\\\\bar.config\\\" now", "See \\\"[redacted-path]\" now", TestName = "Drive_RelativeInsideEscapedQuotes")]
	[Category("Unit")]
	[Description("A drive-relative path - a drive letter with no separator after its colon - goes whole, decoded or "
		+ "escaped. It used to survive whole or lose only its tail ('C:[redacted]', '\\\"C:foo[redacted-path]\\\"').")]
	public void RedactSiteText_ShouldTakeADriveRelativePath(string text, string expected) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(expected, because: "a drive-relative path names a file on the site's server");
	}

	[TestCase(@"See \u0022../x/y.config\u0022 now", @"See \u0022[redacted-path]\u0022 now",
		TestName = "Forward_AfterAUnicodeQuote")]
	[TestCase(@"See \u0027../conf/x.config\u0027 now", @"See \u0027[redacted-path]\u0027 now",
		TestName = "Forward_AfterAUnicodeApostrophe")]
	[TestCase(@"See \""../x/y.config\"" now", @"See \""[redacted-path]\"" now", TestName = "Forward_InsideEscapedQuotes")]
	[Category("Unit")]
	[Description("The forward-slash relative path in an escaped text: it starts right after an escaped quote and leaves "
		+ "the closing escape whole. It used to keep '../x/' after a \\u0022, and to take the backslash of a closing \\\" "
		+ "and leave a bare quote.")]
	public void RedactSiteText_ShouldTakeAForwardRelativePath_BetweenEscapedQuotes(string text, string expected) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(expected, because: "the path goes and both escapes around it stay whole");
	}

	[TestCase(@"See ../..\Terrasoft.Configuration/Web.config now", "See [redacted-path] now", TestName = "Kept_MixedForwardFirst")]
	[TestCase(@"See ../..\\Terrasoft.Configuration/Web.config now", "See [redacted-path] now",
		TestName = "Kept_MixedForwardFirstEscaped")]
	[TestCase(@"See \\SRV01\share\Web.config now", "See [redacted-path] now", TestName = "Kept_Unc")]
	[TestCase(@"See \\\\SRV01\\share\\Web.config now", "See [redacted-path] now", TestName = "Kept_UncEscaped")]
	[TestCase(@"Cannot read C:\Users\jdoe\x.config now", "Cannot read [redacted-path] now", TestName = "Kept_DrivePath")]
	[TestCase(@"Cannot read C:\\Users\\jdoe\\x.config now", "Cannot read [redacted-path] now", TestName = "Kept_DrivePathEscaped")]
	[TestCase("See ../conf/x.config now", "See [redacted-path] now", TestName = "Kept_ForwardRelative")]
	[TestCase("See ~/creatio/x.config now", "See [redacted-path] now", TestName = "Kept_HomeRelative")]
	[TestCase(@"Data Source=.\SQLEXPRESS;Initial Catalog=InventedDb", "Data Source=[redacted];Initial Catalog=[redacted]",
		TestName = "Kept_ConnectionStringWithALocalInstance")]
	[Category("Unit")]
	[Description("GUARD, not a gap: the path shapes clio already removed whole - a forward-first mixed path, a UNC path, "
		+ "a drive path, a home path and a connection string naming a local instance - still go exactly as before.")]
	public void RedactSiteText_ShouldKeepRemovingThePathShapesItAlreadyTook(string text, string expected) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(expected, because: "the new rules must not change what the older rules already removed");
	}

	[TestCase(@"Step one failed.\nNext step", TestName = "Keep_SentenceEndAndAWrittenLineBreak")]
	[TestCase(@"It is done.\""", TestName = "Keep_SentenceEndAndAnEscapedQuote")]
	[TestCase(@"Current user does not have sufficient permissions to run \""CanManageSolution\""",
		TestName = "Keep_EscapedOperationName")]
	[TestCase(@"Saved ""Order"".\nNext record", TestName = "Keep_QuoteDotAndAWrittenLineBreak")]
	[TestCase(@"Saved \""Order\"".\nNext record", TestName = "Keep_EscapedQuoteDotAndAJsonLineBreak")]
	[TestCase(@"Value .\nNext", TestName = "Keep_LoneDotAndAWrittenLineBreak")]
	[TestCase(@"Value .\tNext and .\r\n", TestName = "Keep_LoneDotAndAWrittenTabOrCarriageReturn")]
	[TestCase(@"Value .\u0022 here", TestName = "Keep_LoneDotAndAUnicodeEscape")]
	[TestCase(@"Cannot delete ""Order"".\nthe record is referenced", TestName = "Keep_LineBreakBeforeALowerCaseWord")]
	[TestCase(@"(see log).\nNext step", TestName = "Keep_BracketDotAndAWrittenLineBreak")]
	[TestCase(@"Loading...\nNext and Loading ...\nNext", TestName = "Keep_EllipsisAndAWrittenLineBreak")]
	[TestCase("Wait .. then retry ... later", TestName = "Keep_ProseDots")]
	[TestCase("Creatio 8.1.0.5 detected", TestName = "Keep_Version")]
	[TestCase("Could not load file or assembly 'Terrasoft.Core, Version=10.0.0.0, Culture=neutral, PublicKeyToken=null'",
		TestName = "Keep_AssemblyVersion")]
	[TestCase("Step C: retry", TestName = "Keep_DriveLetterInProse")]
	[TestCase("Started at 10:30 and ended 10:45", TestName = "Keep_Time")]
	[TestCase("Range A:B is empty", TestName = "Keep_LetterRange")]
	[TestCase("Element x:Item.Value is invalid and key p:Item.name is unknown", TestName = "Keep_XmlAndKeyNames")]
	[TestCase("Elapsed T:12.5s with v:1.2.3", TestName = "Keep_LetterColonNumbers")]
	[TestCase(@"See C:foo\nNext now", TestName = "Keep_DriveLetterAndAWrittenLineBreak")]
	[Category("Unit")]
	[Description("What the backslash and drive-relative rules must NOT take: JSON escapes and written escapes after a "
		+ "sentence end, a lone dot before an escape, prose dots, versions, a drive letter in prose, a time, a letter "
		+ "range, XML and key names, a pattern, and a drive letter before a line break. Each guards an over-match the "
		+ "rules could make: the separator after the dots must not be an escape.")]
	public void RedactSiteText_ShouldKeepEscapesProseAndVersions_AroundDotsBackslashesAndColons(string text) {
		// Arrange

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Assert
		site.Should().Be(text, because: "none of these is a path, and the site's words must reach the caller whole");
	}

	[TestCase(LiveInsertRefusal, TestName = "Idempotent_Decoded")]
	[TestCase(LiveInsertRefusalEscaped, TestName = "Idempotent_Escaped")]
	[TestCase(@"Copy ..\a\b.config to .\c\d.config, then C:Web.config\nNext", TestName = "Idempotent_Mixed")]
	[Category("Unit")]
	[Description("RedactSiteText over its own output changes nothing: the placeholder is no path start.")]
	public void RedactSiteText_ShouldBeIdempotent_OnABackslashPath(string text) {
		// Arrange
		string once = SensitiveErrorTextRedactor.RedactSiteText(text);

		// Act
		string twice = SensitiveErrorTextRedactor.RedactSiteText(once);

		// Assert
		once.Should().NotContain("..").And.NotContain("Terrasoft", because: "the first pass takes the path whole");
		twice.Should().Be(once, because: "a second pass must find nothing left to take");
	}

	[TestCase(false, TestName = "BackslashEnvelope_DefaultEncoder")]
	[TestCase(true, TestName = "BackslashEnvelope_RelaxedEncoder")]
	[Category("Unit")]
	[Description("Inside a serialized envelope, through RedactSiteText: every path shape loses every piece. Option B: "
		+ "master's path rule may take the escapes after a path, so the envelope is not asserted to parse.")]
	public void RedactSiteText_ShouldTakeEveryPathShape_InsideASerializedEnvelope(bool relaxed) {
		// Arrange
		const string message = LiveInsertRefusal + " now; also '..\\zq1\\zq2.config' and \"C:zq3\\zq4.config\" (.\\zq5\\zq6)\n"
			+ "And ../zq7/zq8.config with C:Zq9.config.";
		JsonSerializerOptions options = relaxed
			? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
			: new JsonSerializerOptions();
		string envelope = JsonSerializer.Serialize(new { success = false, error = message }, options);

		// Act
		string redacted = SensitiveErrorTextRedactor.RedactSiteText(envelope);

		// Assert
		redacted.Should().NotContain("..").And.NotContain("Terrasoft").And.NotContain("Web.config").And.NotContain("zq",
			because: "no piece of any path may survive");
		redacted.Should().Contain("Insert refused. See [redacted-path]", because: "the reason before the paths stays");
	}

	// The rules timed alone, by name.
	private static Regex SiteRule(string name) =>
		(Regex)typeof(SensitiveErrorTextRedactor)
			.GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
			.Invoke(null, null)!;

	// 100,000 characters of dots and backslashes: the decoded and the escaped separator, and a seeded mix with the
	// spaces, brackets and letters that let a path start and a segment run on.
	private static string DotsAndBackslashes(string shape) {
		const int length = 100_000;
		switch (shape) {
			case "decoded":
				return " " + string.Concat(Enumerable.Repeat(".\\", length / 2));
			case "escaped":
				return " " + string.Concat(Enumerable.Repeat("..\\\\", length / 4));
			default:
				Random random = new(1990);
				const string alphabet = ".\\.\\.\\ (nC:";
				return new string(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The backslash and drive-relative rules ALONE read 100,000 characters of dots and backslashes in "
		+ "linear time: every segment is atomic and every unit starts on a different character, so nothing backtracks. "
		+ "The bound is the server-name rule's, generous for a slow agent.")]
	public void BackslashPathRules_ShouldReadALongRunOfDotsAndBackslashes_InLinearTime(
		[Values("RelativeBackslashPathRegex", "DriveRelativePathRegex", "RelativePathRegex")] string ruleName,
		[Values("decoded", "escaped", "mixed")] string shape) {
		// Arrange
		Regex rule = SiteRule(ruleName);
		string text = DotsAndBackslashes(shape);
		rule.IsMatch("warm up");
		System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

		// Act
		string replaced = rule.Replace(text, "[x]");
		watch.Stop();

		// Assert
		replaced.Should().NotBeNull();
		watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(750), because: "the rule must stay linear on a long run");
	}

	#endregion

	#region Option B: what master's chain removes more, and what B leaves

	[TestCase("redact", "The bearer token expired", "The [redacted] expired", "master's Bearer rule takes the scheme word with its run", TestName = "OverMatch_Kept_ProseAfterBearer")]
	[TestCase("redact", "WWW-Authenticate: Bearer realm=\"api\", error=\"invalid_token\"", "WWW-Authenticate: [redacted]\"api\", error=\"invalid_token\"", "master's Bearer rule takes the scheme word with its run", TestName = "OverMatch_Kept_Challenge")]
	[TestCase("redact", "Invalid Bearer token.", "Invalid [redacted]", "master's Bearer rule takes the scheme word with its run", TestName = "OverMatch_Kept_BearerTokenSentence")]
	[TestCase("redact", "Bearer token-based access denied", "[redacted] access denied", "master's Bearer rule takes the scheme word with its run", TestName = "OverMatch_Kept_HyphenatedProse")]
	[TestCase("redact", "The bearer token expired; Authorization: Bearer x7Kq2mVb9LpQ3", "The [redacted] expired; Authorization=[redacted]", "master's Bearer rule takes the scheme word with its run", TestName = "OverMatch_Fix_ProseAfterBearer")]
	[TestCase("redact", "Bearer token-based access denied; retry with Bearer abcdefgh.ijklmnop-qrstuv", "[redacted] access denied; retry with [redacted]", "master's Bearer rule takes the scheme word with its run", TestName = "OverMatch_Fix_HyphenatedProseAfterBearer")]
	[TestCase("redact", "Invalid Bearer token. Authorization: Basic U3VwZXJ2aXNvcjpTdXBlcnZpc29y", "Invalid [redacted] Authorization=[redacted] [redacted]", "master's Bearer rule takes the scheme word with its run", TestName = "OverMatch_Fix_BearerTokenSentence")]
	[TestCase("redact", "WWW-Authenticate: Bearer realm=\"api\", error=\"invalid_token\"; sent Authorization: Bearer x7Kq2mVb9LpQ3", "WWW-Authenticate: [redacted]\"api\", error=\"invalid_token\"; sent Authorization=[redacted]", "master's Bearer rule takes the scheme word with its run", TestName = "OverMatch_Fix_Challenge")]
	[TestCase("redact", "Error in UsrOrderPage.js:42:13 while calling db01.internal:1433", "Error in [redacted-uri]:13 while calling [redacted-uri]", "master's host:port rule reads a file and a line as a host and a port", TestName = "OverMatch_Fix_FileLineColumn")]
	[TestCase("redact", "Key: UsrStatus; token: expired; token: x7Kq2mVb9Lp", "Key: UsrStatus; token=[redacted]; token=[redacted]", "master's pair rule takes the word after a credential key's colon and writes key=[redacted]", TestName = "OverMatch_Fix_IdentifierAfterTokenColon")]
	[TestCase("redact", "client_secret=s3cr3t&api_key=abc&key=v", "client_secret=[redacted]", "master's pair value runs across '&' and takes the key names after it", TestName = "OverMatch_Fix_PairValueAcrossAmpersand")]
	[TestCase("redact", "see (/var/log/x) or {C:\\x\\y.dll} with Password=hunter2", "see ([redacted-path] or {[redacted-path] with Password=[redacted]", "master's path rules take a closing bracket, an escape and what follows", TestName = "OverMatch_Fix_PathEatsClosingBracket")]
	[TestCase("site", "Wrote end.\\\\n here", "Wrote end.[redacted-path] here", "master's Windows path rule reads an escaped backslash as the start of a UNC path", TestName = "OverMatch_Keep_SentenceEndAndAnEscapedBackslash")]
	[TestCase("site", "Saved \\u0022Order\\u0022.\\\\nNext record", "Saved \\u0022Order\\u0022[redacted-path] record", "master's Windows path rule reads an escaped backslash as the start of a UNC path", TestName = "OverMatch_Keep_UnicodeQuoteDotAndAnEscapedLineBreak")]
	[TestCase("site", "Pattern \"^[A-Z].\\d{3}$\" is invalid", "Pattern \"^[A-Z][redacted-path]{[redacted-path]}$\" is invalid", "R3, removing wins: a dot after a closing bracket starts a relative path", TestName = "OverMatch_Keep_DotAfterAClosingBracket")]
	[TestCase("redact", "auth: Basic mode is off", "auth=[redacted] mode is off", "master's pair rule takes the word after a credential key's colon and writes key=[redacted]", TestName = "OverMatch_Header_AuthWordInProse")]
	[TestCase("redact", "IdentityService did not return a usable bearer token. Check the OAuth client credentials.", "IdentityService did not return a usable [redacted] Check the OAuth client credentials.", "master's Bearer rule takes the scheme word with its run", TestName = "OverMatch_ProseBearerTokenInClioText")]
	[Category("Unit")]
	[Description("DECLARED OVER-MATCHES of option B: master's chain runs first and frozen, so what it removes goes, where "
		+ "#1990's own rules kept it. Each case pins the output and names the master rule that takes the text; every case "
		+ "still loses its secret.")]
	public void MastersChain_ShouldRemoveMore_AsDeclared(string entry, string text, string expected, string reason) {
		// Arrange

		// Act
		string output = entry == "site" ? SensitiveErrorTextRedactor.RedactSiteText(text) : SensitiveErrorTextRedactor.Redact(text);

		// Assert
		output.Should().Be(expected, because: reason);
	}

	[TestCase("password: Basic secrettokenvalue", "password=[redacted] secrettokenvalue", TestName = "Residue_CredentialKey_PasswordBasic")]
	[TestCase("client_secret: NTLM abcdefghijkl", "client_secret=[redacted] abcdefghijkl", TestName = "Residue_CredentialKey_ClientSecretNtlm")]
	[TestCase("token: Basic Summer", "token=[redacted] Summer", TestName = "Residue_CredentialKey_TokenBasic")]
	[TestCase("access_token: Negotiate abcdefgh", "access_token=[redacted] abcdefgh", TestName = "Residue_CredentialKey_AccessTokenNegotiate")]
	[TestCase("Cookie: Basic abcdefghijkl", "Cookie=[redacted] abcdefghijkl", TestName = "Residue_CredentialKey_CookieBasic")]
	[TestCase("X-Auth: Basic abcdefghijkl", "X-Auth=[redacted] abcdefghijkl", TestName = "Residue_JoinedKey_XAuth")]
	[TestCase("Api-Key: Basic abcdefghijkl", "Api-Key=[redacted] abcdefghijkl", TestName = "Residue_JoinedKey_ApiKey")]
	[TestCase("Proxy-Auth: Basic abcdefghijkl", "Proxy-Auth=[redacted] abcdefghijkl", TestName = "Residue_JoinedKey_ProxyAuth")]
	[TestCase("X-Auth: Basic token", "X-Auth=[redacted] token", TestName = "Residue_JoinedKey_XAuthClosedListWord")]
	[TestCase("Authorization: Basic configuration", "Authorization=[redacted] configuration", TestName = "Residue_Header_ProseWordAfterAuthorization")]
	[TestCase("Proxy-Authorization: Negotiate handshake", "Proxy-Authorization=[redacted] handshake", TestName = "Residue_Header_ProseWordAfterProxyAuthorization")]
	[TestCase("Authorization: NTLM abcdefghijkl", "Authorization=[redacted] abcdefghijkl", TestName = "Residue_Header_LowerCaseRun")]
	[TestCase("Authorization: Basic configuration", "Authorization=[redacted] configuration", TestName = "Residue_Header_Authorization")]
	[TestCase("Proxy-Authorization: NTLM abcdefgh", "Proxy-Authorization=[redacted] abcdefgh", TestName = "Residue_Header_ProxyAuthorization")]
	[Category("Unit")]
	[Description("RESIDUE of option B against #1990's head 5ae2a08f8: a prose-shaped run after a scheme word after a "
		+ "credential key ('password: Basic secrettokenvalue', 'Authorization: Basic configuration') shows. Master's pair "
		+ "rule takes the scheme word as the value and writes key=[redacted]; the run after the placeholder can then not be "
		+ "told from the sentence after a removed value ('Authorization: Bearer x returned 401' becomes 'Authorization=[redacted] "
		+ "returned 401' the same way), so Extra takes it only when it is credential-shaped. Master shows these too.")]
	public void Residue_ProseShapedRunAfterASchemeWordAfterACredentialKey_Shows(string text, string expected) {
		// Arrange

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(text);

		// Assert
		redact.Should().Be(expected, because: "the residue is pinned so a fix shows up here");
	}

	#endregion
}
