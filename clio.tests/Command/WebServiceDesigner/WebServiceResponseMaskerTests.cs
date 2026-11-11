using System;
using System.Collections.Generic;
using System.Text;
using Clio.Command.WebServiceDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WebServiceResponseMaskerTests {

	private static ServiceMethodDto Method(params WebServiceParameterArg[] request) {
		var errors = new WebServiceDefinitionBuilder.Errors();
		return WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Echo", "POST", "anything", RequestParameters: request), null, false, errors);
	}

	// The shape CallServiceSchemaService reports as RequestRawData (measured on 10.2.565).
	private const string RawRequest = "POST https://httpbin.org/anything?token=q%2Fsecret-42&page=2 HTTPS/1.1\r\n"
		+ "X-Api-Key: sk-header-secret\r\nCookie: sid=cookie-secret-9; theme=dark\r\n"
		+ "Accept: application/json, */*\r\nHost: httpbin.org";

	[Test]
	[Description("Every value a system setting filled in, in a header, the query, a cookie or the body, is collected; constant and caller values are not.")]
	public void SentSecrets_Should_Collect_Setting_Bound_Values_By_Placement() {
		// Arrange
		ServiceMethodDto method = Method(
			new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key", DefaultSysSetting: "UsrKey"),
			new WebServiceParameterArg("token", "query", DefaultSysSetting: "UsrToken"),
			new WebServiceParameterArg("page", "query"),
			new WebServiceParameterArg("sid", "cookie", DefaultSysSetting: "UsrSid"),
			new WebServiceParameterArg("authSecret", Path: "auth.secret", DefaultSysSetting: "UsrBodySecret"));

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method, RawRequest,
			"""{"auth":{"secret":"body-secret-7"},"page":"2"}""");

		// Assert
		secrets.Should().BeEquivalentTo(["sk-header-secret", "q/secret-42", "cookie-secret-9", "body-secret-7"],
			because: "each setting-bound value is read from where the platform put it, a dotted body path under its last segment, and 'page' is the caller's own value");
	}

	[Test]
	[Description("In a JSON body a secret is masked in string values only, so a secret equal to a property name keeps the structure.")]
	public void ApplyToBody_Should_Mask_Json_Values_Not_Property_Names() {
		// Arrange
		const string body = """{"authenticated":true,"user":"user","nested":{"echo":"token user-77"}}""";

		// Act
		string result = WebServiceResponseMasker.ApplyToBody(body, ["user"], out bool masked);

		// Assert
		masked.Should().BeTrue(because: "the value carries the secret");
		result.Should().Contain("\"user\": \"[masked]\"", because: "the property name stays and its value is masked");
		result.Should().Contain("token [masked]-77", because: "a secret inside a longer string value is masked too");
		result.Should().Contain("\"authenticated\": true", because: "the rest of the structure is unchanged");
	}

	[Test]
	[Description("A secret carried by a JSON number or boolean is masked whole.")]
	public void ApplyToBody_Should_Mask_A_Numeric_Secret() {
		// Arrange
		const string body = """{"accountId":12345678,"active":true}""";

		// Act
		string result = WebServiceResponseMasker.ApplyToBody(body, ["12345678"], out bool masked);

		// Assert
		masked.Should().BeTrue(because: "a setting-bound integer echoed as a number is still the secret");
		result.Should().Contain("\"accountId\": \"[masked]\"", because: "the number is replaced by the mask");
		result.Should().NotContain("12345678", because: "no form of the secret may survive");
	}

	[Test]
	[Description("A token-like secret used as a property name is masked; a short plain word used as one is not.")]
	public void ApplyToBody_Should_Mask_Token_Like_Property_Names_Only() {
		// Arrange
		const string body = """{"sessions":{"tok_abcdef":{"ok":true}},"user":"user"}""";

		// Act
		string result = WebServiceResponseMasker.ApplyToBody(body, ["tok_abcdef", "user"], out bool masked);

		// Assert
		masked.Should().BeTrue(because: "both secrets occur");
		result.Should().NotContain("tok_abcdef", because: "a token echoed as a key is still a token");
		result.Should().Contain("\"user\": \"[masked]\"",
			because: "a short plain-word secret is masked in the value and the key stays readable");
	}

	[Test]
	[Description("JSON with duplicate keys falls back to plain-text masking instead of failing the call.")]
	public void ApplyToBody_Should_Fall_Back_On_Duplicate_Keys() {
		// Arrange
		const string body = """{"k":"sk-secret-1","k":"y"}""";

		// Act
		string result = WebServiceResponseMasker.ApplyToBody(body, ["sk-secret-1"], out bool masked);

		// Assert
		masked.Should().BeTrue(because: "the secret is still found");
		result.Should().Be("""{"k":"[masked]","k":"y"}""", because: "the body is masked as text when the JSON walk rejects it");
	}

	[Test]
	[Description("A JSON body without any secret comes back byte for byte, and a JSON string at the root is masked as text.")]
	public void ApplyToBody_Should_Leave_A_Clean_Body_Unchanged() {
		// Arrange
		const string clean = "{\n  \"a\" : 1 }";

		// Act
		string unchanged = WebServiceResponseMasker.ApplyToBody(clean, ["sk-secret-1"], out bool cleanMasked);
		string rootString = WebServiceResponseMasker.ApplyToBody("\"sk-secret-1\"", ["sk-secret-1"], out bool rootMasked);

		// Assert
		cleanMasked.Should().BeFalse(because: "nothing to mask");
		unchanged.Should().Be(clean, because: "an unmasked body is not re-serialized");
		rootMasked.Should().BeTrue(because: "a JSON string at the root is text");
		rootString.Should().Be("\"[masked]\"", because: "the root string is masked in place");
	}

	[Test]
	[Description("A body that is not JSON is masked as plain text.")]
	public void ApplyToBody_Should_Mask_Plain_Text() {
		// Arrange
		const string body = "<r><key>sk-secret-1</key></r>";

		// Act
		string result = WebServiceResponseMasker.ApplyToBody(body, ["sk-secret-1"], out bool masked);

		// Assert
		masked.Should().BeTrue(because: "the XML body carries the secret");
		result.Should().Be("<r><key>[masked]</key></r>", because: "plain text is masked wherever the secret occurs");
	}

	[Test]
	[Description("A Basic Authorization header yields the encoded credentials and both decoded parts.")]
	public void SentSecrets_Should_Collect_Basic_Authorization_Parts() {
		// Arrange
		string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("erp-user:erp-password"));
		string raw = $"GET https://erp.example/orders HTTPS/1.1\r\nAuthorization: Basic {basic}\r\nHost: erp.example";

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(Method(), raw, string.Empty);

		// Assert
		secrets.Should().BeEquivalentTo([basic, "erp-user", "erp-password"],
			because: "an API that echoes the header or the decoded user would otherwise hand the password back");
	}

	[Test]
	[Description("Apply replaces a secret in its plain, URL-escaped and JSON-escaped forms and reports that it did.")]
	public void Apply_Should_Mask_Every_Form_Of_A_Secret() {
		// Arrange
		string body = """{"headers":{"X-Api-Key":"a/b\"c-key"},"url":"https://x/?k=a%2Fb%22c-key"}""";

		// Act
		string result = WebServiceResponseMasker.Apply(body, ["a/b\"c-key"], out bool masked);

		// Assert
		result.Should().Be("""{"headers":{"X-Api-Key":"[masked]"},"url":"https://x/?k=[masked]"}""",
			because: "an echo can carry the value escaped for JSON or for a URL");
		masked.Should().BeTrue(because: "the caller is told that something was hidden");
	}

	[Test]
	[Description("Values shorter than the minimum are not collected, so common short words in a body are not erased.")]
	public void SentSecrets_Should_Skip_Short_Values() {
		// Arrange
		ServiceMethodDto method = Method(new WebServiceParameterArg("lang", "query", DefaultSysSetting: "UsrLang"));

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method,
			"GET https://x/?lang=en HTTPS/1.1\r\nHost: x", null);

		// Assert
		secrets.Should().BeEmpty(because: "'en' is below the minimum masked length");
	}
}
