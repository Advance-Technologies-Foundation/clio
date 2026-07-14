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

	private static string EncodeJson(string json) =>
		Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

	[Test]
	[Description("A base64-encoded JSON payload with url and accessToken parses into AccessToken material.")]
	public void TryParse_ShouldReturnAccessTokenMaterial_WhenAccessTokenPresent() {
		// Arrange
		string header = Encode(new { url = "https://env.creatio.com", accessToken = SecretToken, accessTokenType = "Bearer", isNetCore = false });

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeTrue(because: "a valid base64 JSON payload with a token is well-formed");
		error.Should().BeNull(because: "there is no defect on the success path");
		result.Url.Should().Be("https://env.creatio.com", because: "url is echoed from the payload");
		result.Auth.Kind.Should().Be(CredentialKind.AccessToken, because: "accessToken is present");
		result.Auth.AccessToken.Should().Be(SecretToken, because: "the token is carried on the material");
		result.Auth.AccessTokenType.Should().Be("Bearer", because: "the token type is carried alongside the token");
		result.IsNetCore.Should().BeFalse(because: "the explicit runtime boolean must be preserved");
	}

	[Test]
	[Description("An explicit JSON true runtime value is accepted and carried through the parse result.")]
	public void TryParse_ShouldReturnNetCoreRuntime_WhenIsNetCoreIsTrue() {
		// Arrange
		string header = Encode(new {
			url = "https://env.creatio.com",
			accessToken = SecretToken,
			accessTokenType = "Bearer",
			isNetCore = true
		});

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeTrue(because: "a JSON boolean true is a valid runtime selection");
		error.Should().BeNull(because: "the explicit true value is valid");
		result.IsNetCore.Should().BeTrue(because: "the parser must preserve the Core runtime selection");
	}

	[Test]
	[Description("The runtime property is matched case-insensitively while the parsed value remains a JSON boolean.")]
	public void TryParse_ShouldAcceptRuntimeProperty_WhenPropertyNameUsesDifferentCasing() {
		// Arrange
		string header = EncodeJson(
			$"{{\"url\":\"https://env.creatio.com\",\"accessToken\":\"{SecretToken}\",\"IsNetCore\":true}}");

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeTrue(because: "the existing JSON policy is case-insensitive");
		error.Should().BeNull(because: "casing does not change the field meaning");
		result.IsNetCore.Should().BeTrue(because: "the case-insensitive field must retain its boolean value");
	}

	[Test]
	[Description("A missing runtime property is rejected before authentication resolution with a defect-only error.")]
	public void TryParse_ShouldFail_WhenIsNetCoreIsMissing() {
		// Arrange
		string header = Encode(new { url = "https://env.creatio.com", accessToken = SecretToken });

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeFalse(because: "runtime selection is required for passthrough headers");
		result.Should().BeNull(because: "missing runtime metadata cannot produce a parse result");
		error.Should().Be("missing isNetCore", because: "the caller needs to know which required field is absent");
		error.Should().NotContain(SecretToken, because: "validation errors must not expose credentials");
	}

	[TestCase("null")]
	[TestCase("\"true\"")]
	[TestCase("1")]
	[TestCase("[]")]
	[TestCase("{}")]
	[Description("A present non-boolean runtime value is rejected without coercion or secret disclosure.")]
	public void TryParse_ShouldFail_WhenIsNetCoreIsNotJsonBoolean(string runtimeJson) {
		// Arrange
		string header = EncodeJson(
			$"{{\"url\":\"https://env.creatio.com\",\"accessToken\":\"{SecretToken}\",\"isNetCore\":{runtimeJson}}}");

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out string error);

		// Assert
		ok.Should().BeFalse(because: "only JSON booleans are valid runtime metadata");
		result.Should().BeNull(because: "an invalid runtime cannot produce a parse result");
		error.Should().Be("isNetCore must be a JSON boolean", because: "the error must identify the invalid JSON kind without echoing it");
		error.Should().NotContain(SecretToken, because: "validation errors must not expose credentials");
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
			password = SecretPassword,
			isNetCore = false
		});

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out _);

		// Assert
		ok.Should().BeTrue(because: "the payload is well-formed");
		result.Auth.Kind.Should().Be(CredentialKind.AccessToken, because: "accessToken has the highest precedence");
	}

	[Test]
	[Description("When cookie and login+password are present but accessToken is absent, cookie wins by precedence.")]
	public void TryParse_ShouldPreferCookie_WhenAccessTokenAbsentAndCookiePresent() {
		// Arrange
		string header = Encode(new {
			url = "https://env.creatio.com",
			cookie = SecretCookie,
			login = "admin",
			password = SecretPassword,
			isNetCore = false
		});

		// Act
		bool ok = _sut.TryParse(header, out CredentialParseResult result, out _);

		// Assert
		ok.Should().BeTrue(because: "the payload is well-formed");
		result.Auth.Kind.Should().Be(CredentialKind.Cookie, because: "cookie has precedence over login+password");
		result.Auth.Cookie.Should().Be(SecretCookie, because: "the cookie is carried on the material");
	}

	[Test]
	[Description("When only login+password are present, LoginPassword material is resolved.")]
	public void TryParse_ShouldReturnLoginPassword_WhenOnlyLoginAndPasswordPresent() {
		// Arrange
		string header = Encode(new { url = "https://env.creatio.com", login = "admin", password = SecretPassword, isNetCore = false });

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
		string header = Encode(new { url = "https://env.creatio.com", login = "admin", isNetCore = false });

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
		string header = Encode(new { url = "https://env.creatio.com", isNetCore = false });

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
