using System;
using System.Text;
using System.Text.Json;
using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CredentialHeaderParserTests
{
	private const string SecretToken = "super-secret-token-value";
	private const string SecretCookie = "BPMCSRF=super-secret-cookie";
	private const string SecretPassword = "super-secret-password";

	private readonly ICredentialHeaderParser _sut = new CredentialHeaderParser();

	private static string Encode(object payload) {
		string json = JsonSerializer.Serialize(payload);
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
	}

	[Test]
	[Description("Review (FR-11): CredentialMaterial / CredentialContext / CredentialParseResult ToString() must NOT print the token, cookie, or password — an accidental interpolation or log of the record cannot leak a secret.")]
	public void ToString_ShouldNotLeakSecretMaterial_ForCredentialRecords() {
		// Arrange
		CredentialMaterial token = CredentialMaterial.FromAccessToken(SecretToken, "Bearer");
		CredentialMaterial login = CredentialMaterial.FromLoginPassword("admin", SecretPassword);
		CredentialMaterial cookie = CredentialMaterial.FromCookie(SecretCookie);
		CredentialContext context = new("https://env.creatio.com", token, McpTransport.Http, true);
		CredentialParseResult parseResult = new("https://env.creatio.com", login);

		// Act
		string materialText = token.ToString() + login.ToString() + cookie.ToString();
		string contextText = context.ToString();
		string parseResultText = parseResult.ToString();

		// Assert
		materialText.Should().NotContain(SecretToken, because: "the record ToString must not print the access token (FR-11)");
		materialText.Should().NotContain(SecretPassword, because: "the record ToString must not print the password (FR-11)");
		materialText.Should().NotContain(SecretCookie, because: "the record ToString must not print the cookie (FR-11)");
		contextText.Should().NotContain(SecretToken, because: "the context ToString delegates to the redacted material ToString (FR-11)");
		parseResultText.Should().NotContain(SecretPassword, because: "the parse-result ToString delegates to the redacted material ToString (FR-11)");
	}

	[Test]
	[Description("A base64-encoded JSON payload with url and accessToken parses into AccessToken material.")]
	public void TryParse_ShouldReturnAccessTokenMaterial_WhenAccessTokenPresent() {
		// Arrange
		string header = Encode(new { url = "https://env.creatio.com", accessToken = SecretToken, accessTokenType = "Bearer" });

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeTrue(because: "a valid base64 JSON payload with a token is well-formed");
		error.Should().BeNull(because: "there is no defect on the success path");
		result.Url.Should().Be("https://env.creatio.com", because: "url is echoed from the payload");
		result.Auth.Kind.Should().Be(CredentialKind.AccessToken, because: "accessToken is present");
		result.Auth.AccessToken.Should().Be(SecretToken, because: "the token is carried on the material");
		result.Auth.AccessTokenType.Should().Be("Bearer", because: "the token type is carried alongside the token");
	}

	[Test]
	[Description("When accessToken, cookie and login+password are all present, accessToken wins by precedence.")]
	public void TryParse_ShouldPreferAccessToken_WhenAllMaterialsPresent() {
		// Arrange
		string header = Encode(new {
			url = "https://env.creatio.com",
			accessToken = SecretToken,
			cookie = SecretCookie,
			login = "admin",
			password = SecretPassword
		});

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out _);

		// Assert
		ok.Should().BeTrue(because: "the payload is well-formed");
		result.Auth.Kind.Should().Be(CredentialKind.AccessToken, because: "accessToken has the highest precedence");
	}

	[Test]
	[Description("Review: when cookie and login+password are present but accessToken is absent, login+password wins — cookie ranks LAST because it is unsupported in v1 and must not shadow usable credentials.")]
	public void TryParse_ShouldPreferLoginPassword_WhenCookieAndLoginPasswordPresent() {
		// Arrange
		string header = Encode(new {
			url = "https://env.creatio.com",
			cookie = SecretCookie,
			login = "admin",
			password = SecretPassword
		});

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out _);

		// Assert
		ok.Should().BeTrue(because: "the payload is well-formed");
		result.Auth.Kind.Should().Be(CredentialKind.LoginPassword,
			because: "login+password (supported in v1) must not be shadowed by the cookie (unsupported in v1)");
		result.Auth.Login.Should().Be("admin", because: "the login is carried on the resolved material");
	}

	[Test]
	[Description("Review: a cookie-only payload still resolves to Cookie material so the caller gets the specific 'cookie not supported in v1' rejection rather than a generic 'no usable auth material'.")]
	public void TryParse_ShouldResolveCookie_WhenOnlyCookiePresent() {
		// Arrange
		string header = Encode(new { url = "https://env.creatio.com", cookie = SecretCookie });

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out _);

		// Assert
		ok.Should().BeTrue(because: "the payload is well-formed");
		result.Auth.Kind.Should().Be(CredentialKind.Cookie,
			because: "a cookie-only payload resolves to Cookie so the specific v1-unsupported message is produced");
		result.Auth.Cookie.Should().Be(SecretCookie, because: "the cookie is carried on the material");
	}

	[Test]
	[Description("When only login+password are present, LoginPassword material is resolved.")]
	public void TryParse_ShouldReturnLoginPassword_WhenOnlyLoginAndPasswordPresent() {
		// Arrange
		string header = Encode(new { url = "https://env.creatio.com", login = "admin", password = SecretPassword });

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out _);

		// Assert
		ok.Should().BeTrue(because: "login+password is usable auth material");
		result.Auth.Kind.Should().Be(CredentialKind.LoginPassword, because: "no higher-precedence material is present");
		result.Auth.Login.Should().Be("admin", because: "the login is carried on the material");
		result.Auth.Password.Should().Be(SecretPassword, because: "the password is carried on the material");
	}

	[Test]
	[Description("A login with no password is not usable auth material and falls through to a failure.")]
	public void TryParse_ShouldFail_WhenLoginPresentButPasswordMissing() {
		// Arrange
		string header = Encode(new { url = "https://env.creatio.com", login = "admin" });

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeFalse(because: "login+password requires both to be present");
		result.Should().BeNull(because: "no material could be resolved");
		error.Should().Be("no usable auth material", because: "a password is required to use login credentials");
	}

	[Test]
	[Description("A payload with url but no auth fields at all fails with a no-auth-material defect.")]
	public void TryParse_ShouldFail_WhenNoAuthMaterialPresent() {
		// Arrange
		string header = Encode(new { url = "https://env.creatio.com" });

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeFalse(because: "a url alone carries no credentials");
		result.Should().BeNull(because: "there is nothing to resolve");
		error.Should().Be("no usable auth material", because: "the defect names the missing auth material");
	}

	[Test]
	[Description("A payload with auth material but a missing url fails with a missing-url defect.")]
	public void TryParse_ShouldFail_WhenUrlMissing() {
		// Arrange
		string header = Encode(new { accessToken = SecretToken });

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeFalse(because: "url is always required");
		result.Should().BeNull(because: "parsing stopped at the missing url");
		error.Should().Be("missing url", because: "the defect names the missing url");
	}

	[Test]
	[Description("A payload with a blank/whitespace url fails with a missing-url defect.")]
	public void TryParse_ShouldFail_WhenUrlBlank() {
		// Arrange
		string header = Encode(new { url = "   ", accessToken = SecretToken });

		// Act
		bool ok = _sut.TryParse(header, out _, out string error);

		// Assert
		ok.Should().BeFalse(because: "a blank url is treated as missing");
		error.Should().Be("missing url", because: "whitespace is not a usable url");
	}

	[Test]
	[Description("A header value that is not valid base64 fails with a base64 defect and no secret echo.")]
	public void TryParse_ShouldFail_WhenNotValidBase64() {
		// Arrange
		string header = "!!!not-base64!!!";

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeFalse(because: "the header could not be base64-decoded");
		result.Should().BeNull(because: "decoding failed before any parsing");
		error.Should().Be("credential header is not valid base64", because: "the defect names the base64 failure");
	}

	[Test]
	[Description("A base64 value that decodes to non-JSON fails with a JSON defect and no payload echo.")]
	public void TryParse_ShouldFail_WhenDecodedBytesAreNotJson() {
		// Arrange
		string header = Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not json"));

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeFalse(because: "the decoded bytes are not valid JSON");
		result.Should().BeNull(because: "JSON parsing failed");
		error.Should().Be("credential header is not valid JSON", because: "the defect names the JSON failure");
	}

	[Test]
	[Description("An empty or whitespace header fails with an empty-header defect.")]
	public void TryParse_ShouldFail_WhenHeaderIsEmpty() {
		// Arrange
		string header = "   ";

		// Act
		bool ok = _sut.TryParse(header, out _, out string error);

		// Assert
		ok.Should().BeFalse(because: "an empty header carries no credentials");
		error.Should().Be("credential header is empty", because: "the defect names the empty header");
	}

	[Test]
	[Description("No parse-error message ever echoes a token, cookie or password value (FR-11 secret hygiene).")]
	public void TryParse_ShouldNeverEchoSecrets_WhenParsingFails() {
		// Arrange
		string malformedJsonWithSecrets = Convert.ToBase64String(Encoding.UTF8.GetBytes(
			$"{{ \"url\": \"https://env\", \"accessToken\": \"{SecretToken}\", \"password\": \"{SecretPassword}\", "));
		string invalidBase64EmbeddingSecret = SecretToken + "%%%";

		// Act
		_sut.TryParse(malformedJsonWithSecrets, out _, out string jsonError);
		_sut.TryParse(invalidBase64EmbeddingSecret, out _, out string base64Error);

		// Assert
		jsonError.Should().NotContain(SecretToken, because: "malformed-JSON errors must not leak the token (FR-11)");
		jsonError.Should().NotContain(SecretPassword, because: "malformed-JSON errors must not leak the password (FR-11)");
		base64Error.Should().NotContain(SecretToken, because: "base64 errors must not echo the header value (FR-11)");
	}
}
