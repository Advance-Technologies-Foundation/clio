using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command.McpServer;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Unit tests for <see cref="McpResultDiagnostics"/> (GitHub issue #1384), the payload-describing helper
/// shared by <see cref="EntitySchemaStructuredResultParser"/> and every sibling result parser in this
/// folder. They construct <see cref="CallToolResult"/> instances in-memory (no MCP server, no stand, no
/// network I/O), so they are categorized <c>Unit</c> rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("McpE2E.NoEnvironment")]
[Property("Module", "McpServer")]
public sealed class McpResultDiagnosticsTests {
	[Test]
	[Description("Describes a result with neither StructuredContent nor Content as IsError plus '(none)' for both payload fields.")]
	public void Describe_ShouldReportNonePayloads_WhenResultIsEmpty() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = false,
			Content = []
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("IsError=False",
			because: "the call did not report an error");
		description.Should().Contain("StructuredContent=(none)",
			because: "no StructuredContent was present on the result");
		description.Should().Contain("Content=(none)",
			because: "an empty Content array carries no items to describe");
	}

	[Test]
	[Description("Describes a text Content item by rendering its type and text verbatim.")]
	public void Describe_ShouldRenderContentItem_WhenResultCarriesTextPayload() {
		// Arrange
		const string payloadText = "plain diagnostic text";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = payloadText }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("IsError=True",
			because: "the call reported an error");
		description.Should().Contain($"{{type=text, text=\"{payloadText}\"}}",
			because: "the single text content item's type and text must both be rendered");
	}

	[Test]
	[Description("Describes StructuredContent by dumping its raw serialized JSON.")]
	public void Describe_ShouldRenderStructuredContent_WhenResultCarriesStructuredPayload() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = false,
			Content = [],
			StructuredContent = JsonSerializer.SerializeToElement(new { code = 7 })
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("StructuredContent={\"code\":7}",
			because: "the structured payload's raw JSON must be embedded so the actual shape mismatch is visible");
	}

	[Test]
	[Description("Redacts an absolute file path embedded in a Content item's text.")]
	public void Describe_ShouldRedactSensitiveText_WhenContentCarriesAnAbsolutePath() {
		// Arrange
		const string sensitiveText = "Failed reading /Users/alex/secrets/credentials.json: invalid format";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = sensitiveText }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().NotContain("/Users/alex/secrets/credentials.json",
			because: "an absolute path must be redacted before it reaches the diagnostic text");
		description.Should().Contain("[redacted-path]",
			because: "the redactor replaces an absolute path with its stable placeholder rather than dropping the whole message");
	}

	[Test]
	[Description("Includes the last JsonException's redacted message and it is only appended when the caller supplies one.")]
	public void Describe_ShouldIncludeLastJsonError_WhenCallerSuppliesOne() {
		// Arrange
		CallToolResult callResult = new() { IsError = false, Content = [] };
		JsonException lastJsonException;
		try {
			JsonSerializer.Deserialize<int>("not-json");
			throw new InvalidOperationException("Expected a JsonException to be thrown by the arrange step.");
		}
		catch (JsonException exception) {
			lastJsonException = exception;
		}

		// Act
		string descriptionWithException = McpResultDiagnostics.Describe(callResult, lastJsonException);
		string descriptionWithoutException = McpResultDiagnostics.Describe(callResult);

		// Assert
		descriptionWithException.Should().Contain("LastJsonError=",
			because: "the caller supplied a JsonException, so its message must appear in the description");
		descriptionWithoutException.Should().NotContain("LastJsonError=",
			because: "no JsonException was supplied, so the description must not fabricate one");
	}

	[Test]
	[Description("Truncates text longer than the documented cap and reports the total original length, instead of flooding the CI log unbounded.")]
	public void Truncate_ShouldCapAndReportTotalLength_WhenTextExceedsTheLimit() {
		// Arrange
		string hugeText = new string('a', McpResultDiagnostics.PayloadDiagnosticLimit + 5_000);

		// Act
		string truncated = McpResultDiagnostics.Truncate(hugeText);

		// Assert
		truncated.Length.Should().BeLessThan(hugeText.Length,
			because: "the truncated text must be capped rather than embedding the full original payload");
		truncated.Should().EndWith(
			$" … {hugeText.Length} characters total, truncated to fit {McpResultDiagnostics.PayloadDiagnosticLimit}",
			because: "the note must state the EXACT original length; a regex on \\d+ accepted the number the double truncation used to produce, which described the already-cut text rather than the payload");
		truncated.Length.Should().Be(McpResultDiagnostics.PayloadDiagnosticLimit,
			because: "the note is paid for out of the budget, so the result lands ON the limit rather than overshooting it by the note's own length");
	}

	[Test]
	[Description("Leaves text at or under the documented cap unchanged.")]
	public void Truncate_ShouldReturnTextUnchanged_WhenTextIsAtOrUnderTheLimit() {
		// Arrange
		string shortText = new string('a', McpResultDiagnostics.PayloadDiagnosticLimit);

		// Act
		string result = McpResultDiagnostics.Truncate(shortText);

		// Assert
		result.Should().Be(shortText,
			because: "text at exactly the cap must not be marked as truncated");
	}

	[Test]
	[Description("Renders an HTML login page returned in a text block instead of collapsing it into a bare parse failure.")]
	public void Describe_ShouldRenderHtmlLoginPage_WhenToolAnsweredWithASignInPage() {
		// Arrange
		const string loginPage =
			"<!DOCTYPE html><html><head><title>Creatio</title></head>" +
			"<body><form id=\"loginForm\"><h1>Sign in</h1></form></body></html>";
		CallToolResult callResult = new() {
			IsError = false,
			Content = [new TextContentBlock { Text = loginPage }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("<!DOCTYPE html>",
			because: "an authentication redirect answers with an HTML page, and recognizing it is the whole point of dumping the payload");
		description.Should().Contain("Sign in",
			because: "the page's own text must survive into the diagnostic so the reader sees it is a login page, not a shape mismatch");
	}

	[Test]
	[Description("Renders a stderr-like blob returned in a text block verbatim rather than skipping it as unparsable.")]
	public void Describe_ShouldRenderStderrBlob_WhenTextIsNeitherJsonNorHtml() {
		// Arrange
		const string stderrBlob =
			"Unhandled exception. System.Net.Http.HttpRequestException: Connection refused\n" +
			"   at Clio.Common.ApplicationClient.ExecutePostRequest(String url)";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = stderrBlob }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("HttpRequestException: Connection refused",
			because: "a process's error output is a legitimate payload shape and must be shown, not discarded for not being JSON");
		description.Should().Contain("IsError=True",
			because: "the reader needs to know the call itself reported an error alongside the blob");
	}

	[Test]
	[Description("Dumps a content block that carries no text string (an image block) as its own raw JSON instead of reporting only '(no text)'.")]
	public void Describe_ShouldDumpRawBlock_WhenContentItemCarriesNoTextString() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = true,
			Content = [ImageContentBlock.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png")]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("{type=image",
			because: "the block's declared type must still be named");
		description.Should().Contain("image/png",
			because: "a block with no text string must be dumped whole, since '(no text)' named the block's existence while discarding everything that said what came back");
		description.Should().Contain("raw={",
			because: "the block must be rendered as its own raw JSON object, which is the replacement for the placeholder that used to hide it");
	}

	[Test]
	[Description("Redacts a JSON-quoted credential property in StructuredContent, which the production key=value redaction rule does not reach.")]
	public void Describe_ShouldRedactJsonQuotedCredential_WhenStructuredContentCarriesAPassword() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = true,
			Content = [],
			StructuredContent = JsonSerializer.SerializeToElement(new { login = "Supervisor", password = "s3cr3t-value" })
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().NotContain("s3cr3t-value",
			because: "an e2e payload dump reaches a TeamCity log readable by everyone who can see the build, so a credential must never appear in it");
		description.Should().Contain("\"password\":\"[redacted]\"",
			because: "the pair must be rewritten in its JSON shape so the surrounding dump stays readable rather than losing a quote");
		description.Should().Contain("Supervisor",
			because: "redaction is surgical: the non-secret fields that explain the failure must survive");
	}

	[Test]
	[Description("Redacts a credential property that is nested one JSON-escaping level deep, as a serialized response body inside StructuredContent is.")]
	public void Describe_ShouldRedactEscapedJsonCredential_WhenStructuredContentNestsSerializedJson() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = true,
			Content = [],
			StructuredContent = JsonSerializer.SerializeToElement(
				new { body = "{\"password\":\"s3cr3t-value\",\"code\":1}" })
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().NotContain("s3cr3t-value",
			because: "a response body carried as a string property is escaped, not plain, and the secret inside it leaks just as badly");
	}

	[Test]
	[Description("Redacts a session cookie and a bearer token carried in a text block.")]
	public void Describe_ShouldRedactCookieAndBearerToken_WhenTextBlockCarriesThem() {
		// Arrange
		const string secretText =
			"Request rejected. Cookie: BPMCSRF=Zq19Lk; .ASPXAUTH=A1B2C3 Authorization: Bearer abc.def.ghi";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = secretText }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().NotContain("Zq19Lk",
			because: "a Creatio forms-auth cookie value IS the session, so it belongs in the same class as a password");
		description.Should().NotContain("abc.def.ghi",
			because: "a bearer token must not reach a build log");
		description.Should().Contain("[redacted]",
			because: "the secrets must be replaced with the stable placeholder rather than the whole message being dropped");
	}

	[Test]
	[Description("Caps a multi-megabyte payload with an explicit marker and still shows the payload's beginning, rather than collapsing into a bare placeholder or flooding the log.")]
	public void Describe_ShouldCapAndStillShowTheBeginning_WhenPayloadIsMultiMegabyte() {
		// Arrange
		const string leadingErrorText = "Access denied for user.";
		string hugePayload = leadingErrorText + new string('x', 3_000_000);
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = hugePayload }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Length.Should().BeLessThan(McpResultDiagnostics.PayloadDiagnosticLimit + 200,
			because: "a three-megabyte tool result must not reach a CI log, whether or not the caller remembered to truncate");
		description.Should().Contain(
			$"…({hugePayload.Length} characters, first {McpResultDiagnostics.RawPayloadInputLimit} shown)",
			because: "the note must state the payload's EXACT size, not the size of the text left after cutting it");
		description.Should().Contain(leadingErrorText,
			because: "the beginning, where the error text lives, is exactly what must survive the cap");
		description.Should().NotBe("[redacted]",
			because: "bounding the raw text before the redaction rules run is what stops their one-second timeout from discarding the whole diagnostic");
	}

	[Test]
	[Description("Redacts before capping, so a secret near the cap boundary cannot be cut out of a redaction rule's reach.")]
	public void Describe_ShouldRedactBeforeCapping_WhenASecretSitsNearTheBoundary() {
		// Arrange
		string paddedSecret =
			new string('p', McpResultDiagnostics.PayloadDiagnosticLimit - 40) + "{\"password\":\"s3cr3t-value\"}";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = paddedSecret }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().NotContain("s3cr3t-value",
			because: "capping a composed message that had not been redacted yet would let a secret straddle the boundary and survive");
	}

	[Test]
	[Description("Redacts OAuth token keys in both snake_case and camelCase, which anchoring the rule to the whole quoted key missed entirely.")]
	public void Describe_ShouldRedactOAuthTokenKeys_WhenStructuredContentCarriesATokenResponse() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = true,
			Content = [],
			StructuredContent = JsonSerializer.SerializeToElement(new Dictionary<string, object?> {
				["access_token"] = "opaque123",
				["refresh_token"] = "r456",
				["accessToken"] = "camel789",
				["refreshToken"] = "camel012",
				["idToken"] = "camel345",
				["clientId"] = "client678",
				["dbPassword"] = "db901",
				["expires_in"] = 3600
			})
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		foreach (string secret in new[] { "opaque123", "r456", "camel789", "camel012", "camel345", "client678", "db901" }) {
			description.Should().NotContain(secret,
				because: "an OAuth envelope spells its keys with a prefix or a suffix, so a rule anchored to the whole quoted key redacted none of them");
		}

		description.Should().Contain("3600",
			because: "redaction is surgical: a non-secret field that explains the failure must survive");
	}

	[Test]
	[Description("Redacts a secret nested one level under a secret key, which no rule here can match as a balanced object value.")]
	public void Describe_ShouldRedactNestedSecret_WhenSecretKeyHoldsAnObject() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = true,
			Content = [],
			StructuredContent = JsonSerializer.SerializeToElement(
				new { token = new { value = "abc123" } })
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().NotContain("abc123",
			because: "the outer key's value is an object no regex here matches, so the inner key has to carry the redaction instead");
	}

	[Test]
	[Description("Redacts a credential written with literal backslash-quote escaping, the spelling a double-encoded body carries.")]
	public void Describe_ShouldRedactBackslashQuotedCredential_WhenTextBlockIsDoubleEncodedJson() {
		// Arrange
		const string doubleEncodedBody = "{\"body\":\"{\\\"password\\\":\\\"s3cr3t-value\\\"}\"}";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = doubleEncodedBody }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("\\\"password\\\"",
			because: "the arrange must actually carry the backslash-quote spelling, otherwise this test would prove nothing about that branch");
		description.Should().NotContain("s3cr3t-value",
			because: "a body that reaches the harness already double-encoded spells its quotes with a backslash rather than \\u0022");
	}

	[Test]
	[Description("Redacts a credential whose closing quote was cut away by the raw input bound, instead of leaking the half that survived.")]
	public void Describe_ShouldRedactUnterminatedSecret_WhenTheRawBoundCutsThroughIt() {
		// Arrange
		// The padding is URIs, not filler: redaction SHRINKS them (roughly 500 characters each collapse to
		// "[redacted-uri]"), which is exactly why the raw-input boundary is reachable inside the display
		// budget at all. Plain filler would push the cut point far past the display cap and the test would
		// prove nothing.
		string uriToken = "https://host.example.com/" + new string('a', 470) + " ";
		StringBuilder padding = new();
		while (padding.Length < McpResultDiagnostics.RawPayloadInputLimit - 20) {
			padding.Append(uriToken);
		}

		string paddedSecret = padding.ToString()[..(McpResultDiagnostics.RawPayloadInputLimit - 20)]
			+ "{\"password\":\"s3cr3t-value\"}";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = paddedSecret }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("\"password\":\"[redacted]\"",
			because: "the unterminated value must be rewritten, proving the rule fired at all rather than the secret simply falling outside the window");
		description.Should().NotContain("s3cr3t",
			because: "the raw bound cuts before the value's closing quote, and a rule that requires that quote would have let the surviving half of the secret through");
	}

	[Test]
	[Description("Stops rendering content blocks once the budget is spent and counts the rest, rather than building a multi-megabyte diagnostic out of many small blocks.")]
	public void Describe_ShouldCountRemainingBlocks_WhenContentCarriesFarMoreThanTheBudget() {
		// Arrange
		ContentBlock[] blocks = [.. Enumerable.Range(0, 500)
			.Select(index => new TextContentBlock { Text = new string('b', 1_000) + index })];
		CallToolResult callResult = new() {
			IsError = true,
			Content = blocks
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("more blocks",
			because: "the blocks past the budget must be counted, so the reader knows the dump is partial rather than complete");
		description.Length.Should().BeLessThan(McpResultDiagnostics.PayloadDiagnosticLimit + 200,
			because: "five hundred blocks of a kilobyte each must not each be redacted and appended on a failure-reporting path");
	}

	[Test]
	[Description("Returns a diagnostics-unavailable note instead of throwing when describing the payload itself fails, so the original parse failure is never masked.")]
	public void Describe_ShouldReportUnavailable_WhenSerializingThePayloadThrows() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = true,
			Content = new ThrowingContentList()
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().StartWith("(diagnostics unavailable:",
			because: "this method composes the explanation of another failure, so it must never replace that failure with one of its own");
		description.Should().MatchRegex(@"\(diagnostics unavailable: \w+Exception: ",
			because: "naming the type of the failure is what makes an unavailable diagnostic actionable rather than merely silent");
	}

	[Test]
	[Description("Does not leave a lone high surrogate when the cap falls inside a surrogate pair.")]
	public void Truncate_ShouldNotSplitASurrogatePair_WhenTheCapFallsInsideOne() {
		// Arrange: one leading char shifts the emoji run onto odd offsets, so the cut provably lands
		// BETWEEN the two halves of one pair rather than happening to miss it.
		const int limit = 60;
		string emojiRun = "a" + string.Concat(Enumerable.Repeat("\U0001F600", 100));
		string note = $" … {emojiRun.Length} characters total, truncated to fit {limit}";

		// Act
		string truncated = McpResultDiagnostics.Truncate(emojiRun, limit);

		// Assert
		string kept = truncated[..^note.Length];
		kept.Length.Should().Be(limit - note.Length - 1,
			because: "the cut fell inside a surrogate pair, so exactly one orphaned half must have been dropped - if it were not, this test would pass without testing anything");
		char.IsHighSurrogate(kept[^1]).Should().BeFalse(
			because: "a lone high surrogate is invalid UTF-16 and makes whatever serializes the message next throw instead of reporting the failure");
	}

	[Test]
	[Description("Covers every secret key the production redactor knows, so a key added there cannot silently go unredacted here.")]
	public void CredentialKeyCore_ShouldCoverEveryProductionCredentialKey() {
		// Arrange
		GeneratedRegexAttribute productionRule = typeof(SensitiveErrorTextRedactor)
			.GetMethod("CredentialPairRegex", BindingFlags.NonPublic | BindingFlags.Static)!
			.GetCustomAttribute<GeneratedRegexAttribute>()!;
		string productionKeyAlternation = Regex.Match(productionRule.Pattern, @"\\b\((?<keys>[^)]*)\)\\b").Groups["keys"].Value;
		string[] productionKeys = productionKeyAlternation.Split('|', StringSplitOptions.RemoveEmptyEntries);

		// Act
		string[] missingKeys = [.. productionKeys.Where(key => !McpResultDiagnostics.CredentialKeyCore.Contains(key, StringComparison.Ordinal))];

		// Assert
		productionKeys.Should().NotBeEmpty(
			because: "the oracle is worthless if it silently extracts nothing from the production pattern");
		missingKeys.Should().BeEmpty(
			because: "this harness duplicates the production key list, so a key added to SensitiveErrorTextRedactor and not here would leak through the e2e payload dump unnoticed");
	}

	/// <summary>
	/// A content collection whose enumeration throws, standing in for any payload whose serialization
	/// fails on this path (a lazily materialized list, a cyclic graph, a throwing property getter).
	/// </summary>
	private sealed class ThrowingContentList : IList<ContentBlock> {
		public ContentBlock this[int index] {
			get => throw new NotSupportedException("Materializing the content blocks is not supported.");
			set => throw new NotSupportedException("Materializing the content blocks is not supported.");
		}

		public int Count => 1;

		public bool IsReadOnly => true;

		public IEnumerator<ContentBlock> GetEnumerator() =>
			throw new NotSupportedException("Materializing the content blocks is not supported.");

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

		public void Add(ContentBlock item) => throw new NotSupportedException();

		public void Clear() => throw new NotSupportedException();

		public bool Contains(ContentBlock item) => throw new NotSupportedException();

		public void CopyTo(ContentBlock[] array, int arrayIndex) => throw new NotSupportedException();

		public int IndexOf(ContentBlock item) => throw new NotSupportedException();

		public void Insert(int index, ContentBlock item) => throw new NotSupportedException();

		public bool Remove(ContentBlock item) => throw new NotSupportedException();

		public void RemoveAt(int index) => throw new NotSupportedException();
	}
}
