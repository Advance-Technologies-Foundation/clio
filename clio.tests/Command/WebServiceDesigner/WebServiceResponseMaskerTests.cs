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
	[Description("A secret constant default describe hides is collected even though no setting filled it; a visible constant is not.")]
	public void SentSecrets_Should_Collect_Secret_Constants() {
		// Arrange
		ServiceMethodDto method = Method(
			new WebServiceParameterArg("XPartner", "header", Path: "X-Partner"),
			new WebServiceParameterArg("apiKey", "query"),
			new WebServiceParameterArg("lang", "query"));
		foreach (ServiceParameterDto parameter in method.Request.Parameters.Items.Values) {
			parameter.DefValue = new ServiceParameterValueDto {
				Value = parameter.Name switch { "XPartner" => "partner-secret-1", "apiKey" => "query-key-2", _ => "en-GB" },
				Source = ServiceSchemaCodes.ValueSourceConst
			};
		}

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method, "GET https://x/ HTTP/1.1", null);

		// Assert
		secrets.Should().BeEquivalentTo(["partner-secret-1", "query-key-2"],
			because: "a constant on a header or under a credential-like name is hidden by describe, so an echo must hide it too, while 'lang' is shown by describe anyway");
	}

	[Test]
	[Description("A path parameter filled from a system setting is found in the sent URL through the method URI template.")]
	public void SentSecrets_Should_Collect_A_Setting_Bound_Path_Value() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Account", "GET", "accounts/{tenant}/orders", RequestParameters: [
				new WebServiceParameterArg("tenant", "path", DefaultSysSetting: "UsrTenant")]), null, false, errors);

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method,
			"GET https://erp.example/v1/accounts/tenant%20secret/orders?page=1 HTTPS/1.1\r\nHost: erp.example", null);

		// Assert
		errors.Should().BeEmpty(because: "the method is valid");
		secrets.Should().Contain(["tenant%20secret", "tenant secret"],
			because: "the value is read from the path segment the template names, as sent and unescaped");
	}

	[Test]
	[Description("In a JSON body a secret is masked in values and in property names, and keys masked to the same text keep both values.")]
	public void ApplyToBody_Should_Mask_Values_And_Property_Names() {
		// Arrange
		const string body = """{"authenticated":true,"user":"user","nested":{"echo":"token user-77","sk-1111":1}}""";

		// Act
		string result = WebServiceResponseMasker.ApplyToBody(body, ["user", "sk-1111"], out bool masked);

		// Assert
		masked.Should().BeTrue(because: "the secrets occur");
		result.Should().NotContain("user", because: "a secret echoed as a key is still the secret, so masking fails closed");
		result.Should().NotContain("sk-1111", because: "no form of a secret may survive");
		result.Should().Contain("\"[masked]\": \"[masked]\"", because: "the key and the value carrying the secret are both masked");
		result.Should().Contain("token [masked]-77", because: "a secret inside a longer string value is masked too");
		result.Should().Contain("\"authenticated\": true", because: "values without a secret are unchanged");
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
	[Description("Two keys masked to the same text both survive, so no echoed value is dropped.")]
	public void ApplyToBody_Should_Keep_Both_Values_When_Masked_Keys_Collide() {
		// Arrange
		const string body = """{"tok_a":"x","tok_b":"y"}""";

		// Act
		string result = WebServiceResponseMasker.ApplyToBody(body, ["tok_a", "tok_b"], out bool masked);

		// Assert
		masked.Should().BeTrue(because: "both keys carry a secret");
		result.Should().Contain("\"[masked]\": \"x\"", because: "the first masked key keeps its value");
		result.Should().Contain("\"[masked]#2\": \"y\"", because: "the second gets a suffix instead of overwriting the first");
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
