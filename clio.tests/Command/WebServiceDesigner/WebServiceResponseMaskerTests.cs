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
	[Description("A setting-bound path value that spans segments is located; one that cannot be located is reported so the response is withheld.")]
	public void HasUnlocatedSecret_Should_Fail_Closed_For_A_Path_Value() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Account", "GET", "accounts/{tenant}/orders", RequestParameters: [
				new WebServiceParameterArg("tenant", "path", DefaultSysSetting: "UsrTenant")]), null, false, errors);
		const string spanning = "GET https://erp.example/v1/accounts/a/b-secret/orders HTTPS/1.1";
		const string unrelated = "GET https://erp.example/elsewhere HTTPS/1.1";

		// Act
		bool spanningUnlocated = WebServiceResponseMasker.HasUnlocatedSecret(method, spanning);
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method, spanning, null);
		bool unrelatedUnlocated = WebServiceResponseMasker.HasUnlocatedSecret(method, unrelated);

		// Assert
		spanningUnlocated.Should().BeFalse(because: "the literal template text anchors a value that contains a slash");
		secrets.Should().Contain("a/b-secret", because: "the whole value between the anchors is the secret");
		WebServiceResponseMasker.SentSecrets(method, "GET https://erp.example/v1/accounts/acme-key/orders HTTPS/1.1", null)
			.Should().Contain("acme-key", because: "a capture that starts inside the base path still yields the real value as a suffix");
		unrelatedUnlocated.Should().BeTrue(because: "a value that cannot be found cannot be masked, so the caller must withhold the response");
	}

	[TestCase("{apiKey}/data", "GET https://h.example/v2/KEY-0042/data HTTPS/1.1", "KEY-0042")]
	[TestCase("{apiKey}", "GET https://erp.example/v1/SECRET-77 HTTPS/1.1", "SECRET-77")]
	[TestCase("orders/{apiKey}", "GET https://h.example/orders/v1/orders/SECRET-88 HTTPS/1.1", "SECRET-88")]
	[TestCase("cities/{apiKey}", "GET https://h.example/cities/New York secret HTTPS/1.1", "New York secret")]
	[Description("A setting-bound path value is found even when the template's literal text also occurs in the base URI or the value contains spaces.")]
	public void SentSecrets_Should_Locate_Path_Values_Without_A_Left_Anchor(string uri, string requestLine, string expected) {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Data", "GET", uri, RequestParameters: [
				new WebServiceParameterArg("apiKey", "path", DefaultSysSetting: "UsrKey")]), null, false, errors);

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method, requestLine, null, out bool unlocated);

		// Assert
		errors.Should().BeEmpty(because: "the method is valid");
		unlocated.Should().BeFalse(because: "the template matches the end of the sent path");
		secrets.Should().Contain(expected, because: "the real value is one of the masked suffixes of the capture");
	}

	[Test]
	[Description("A space in the sent URL does not stop the query values filled from settings from being collected.")]
	public void SentSecrets_Should_Read_The_Query_Of_A_Url_With_Spaces() {
		// Arrange
		ServiceMethodDto method = Method(new WebServiceParameterArg("apikey", "query", DefaultSysSetting: "UsrKey"));

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method,
			"POST https://api.example/cities/New York?apikey=query-secret-5 HTTPS/1.1", null);

		// Assert
		secrets.Should().Contain("query-secret-5", because: "the request target runs from the first to the last space");
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

	private static ServiceParameterDto BodyParameter(string name, string path, string setting = null,
		bool isArray = false, string type = "Text", params ServiceParameterDto[] items) {
		var parameter = new ServiceParameterDto {
			UId = Guid.NewGuid(), Name = name, Type = 1, Path = path, DataValueTypeName = type, IsArray = isArray,
			DefValue = setting is null
				? new ServiceParameterValueDto()
				: new ServiceParameterValueDto { Source = ServiceSchemaCodes.ValueSourceSysSetting, Value = setting }
		};
		foreach (ServiceParameterDto item in items) {
			parameter.ItemProperties.Items[item.UId.ToString()] = item;
		}
		return parameter;
	}

	private static ServiceMethodDto JsonMethod(params ServiceParameterDto[] parameters) {
		var method = new ServiceMethodDto { Name = "UsrOp" };
		foreach (ServiceParameterDto parameter in parameters) {
			method.Request.Parameters.Items[parameter.UId.ToString()] = parameter;
		}
		return method;
	}

	private const string JsonRaw = "POST https://x/op HTTPS/1.1\r\nContent-Type: application/json\r\nHost: x";

	[Test]
	[Description("A setting-bound JSON body value is found under the name the platform writes it to, also for an indexed or bracketed path; one missing from the body fails closed, and one sent as null is located.")]
	public void SentSecrets_Should_Locate_Json_Values_By_The_Platform_Path_Rules() {
		// Arrange
		ServiceMethodDto method = JsonMethod(
			BodyParameter("UsrIndexed", "k[0]", "UsrKeyA"),
			BodyParameter("UsrBracketed", "$['b']", "UsrKeyB"));

		// Act
		IReadOnlyList<string> found = WebServiceResponseMasker.SentSecrets(method, JsonRaw,
			"""{"k":["v-index-11"],"b":"v-bracket-22"}""", false, null, out bool foundUnlocated);
		WebServiceResponseMasker.SentSecrets(method, JsonRaw, """{"k":["v-index-11"]}""", false, null,
			out bool missingUnlocated);
		WebServiceResponseMasker.SentSecrets(method, JsonRaw, """{"k":["v-index-11"],"b":null}""", false, null,
			out bool nullUnlocated);

		// Assert
		found.Should().Contain(["v-index-11", "v-bracket-22"],
			because: "k[0] writes the value inside an array under k, and $['b'] writes it under b");
		foundUnlocated.Should().BeFalse(because: "every setting-bound value was found");
		missingUnlocated.Should().BeTrue(because: "a top-level body value is always rendered, so a missing one is a lookup miss");
		nullUnlocated.Should().BeFalse(because: "a value sent as null (an empty setting) carries nothing to hide");
	}

	[Test]
	[Description("A bracketed name in double quotes is looked up with its quotes, because the platform strips only single quotes and writes the value under the quoted name; a property named without the quotes is not taken for it.")]
	public void SentSecrets_Should_Keep_The_Double_Quotes_Of_A_Bracketed_Name() {
		// Arrange
		ServiceMethodDto method = JsonMethod(BodyParameter("UsrQuoted", "$[\"api-key\"]", "UsrKeyQ"));

		// Act
		IReadOnlyList<string> found = WebServiceResponseMasker.SentSecrets(method, JsonRaw,
			"""{"\"api-key\"":"v-quoted-66","api-key":"decoy-77"}""", false, null, out bool foundUnlocated);
		WebServiceResponseMasker.SentSecrets(method, JsonRaw, """{"api-key":"decoy-77"}""", false, null,
			out bool decoyUnlocated);

		// Assert
		found.Should().Contain("v-quoted-66", because: "the platform writes $[\"api-key\"] under the name \"api-key\" with its quotes");
		found.Should().NotContain("decoy-77", because: "a property named without the quotes is not where the value went");
		foundUnlocated.Should().BeFalse(because: "the value was found under the quoted name");
		decoyUnlocated.Should().BeTrue(because: "with only the unquoted name in the body the sent value is missing, so the response is withheld");
	}

	[TestCase("a[b", """{"a":{"[b":"v-open-44"}}""", Description = "a top-level path ending in an unclosed bracket")]
	[TestCase("a", """{"a":{"x":"v-object-55"}}""", Description = "a name that holds only an object")]
	[Description("A setting-bound JSON value whose name holds only an object, or whose path ends in an unclosed bracket the platform writes under a name of its own, is not taken as found, so the response is withheld instead of returned unmasked.")]
	public void SentSecrets_Should_Fail_Closed_When_The_Json_Name_Holds_No_Value(string path, string body) {
		// Arrange
		ServiceMethodDto method = JsonMethod(BodyParameter("UsrOdd", path, "UsrKeyOdd"));

		// Act
		WebServiceResponseMasker.SentSecrets(method, JsonRaw, body, false, null, out bool unlocated);

		// Assert
		unlocated.Should().BeTrue(because: "the value cannot be told apart from the rest of the body, so it cannot be masked");
	}

	[Test]
	[Description("A setting-bound field of a JSON array item is found in every item, also when its own placement says header; it fails closed when an item that went out lacks it or the given array is missing, and a setting-bound array or an array left out hides nothing.")]
	public void SentSecrets_Should_Locate_Json_Array_Item_Fields_And_Fail_Closed() {
		// Arrange
		ServiceParameterDto key = BodyParameter("UsrKey", "key", "UsrItemKey");
		key.Type = ServiceSchemaCodes.Placements["header"];
		ServiceMethodDto method = JsonMethod(BodyParameter("UsrLines", "lines", type: "Object", isArray: true, items: [
			key, BodyParameter("UsrTags", "tags", "UsrTagSetting", isArray: true)
		]));

		// Act
		IReadOnlyList<string> filled = WebServiceResponseMasker.SentSecrets(method, JsonRaw,
			"""{"lines":[{"key":"k-item-33"}]}""", false, ["UsrLines"], out bool filledUnlocated);
		WebServiceResponseMasker.SentSecrets(method, JsonRaw, """{"lines":[{"sku":"x"}]}""", false, ["UsrLines"],
			out bool fieldMissing);
		WebServiceResponseMasker.SentSecrets(method, JsonRaw, "{}", false, [], out bool arrayLeftOut);
		WebServiceResponseMasker.SentSecrets(method, JsonRaw, "{}", false, ["UsrLines"], out bool givenArrayMissing);
		WebServiceResponseMasker.SentSecrets(method, JsonRaw, """{"lines":[]}""", false, ["UsrLines"],
			out bool emptyArray);

		// Assert
		filled.Should().Contain("k-item-33",
			because: "the platform renders every item field into the body, whatever placement the field carries");
		filledUnlocated.Should().BeFalse(because: "the item field was found, and a body array is never filled from its setting");
		fieldMissing.Should().BeTrue(because: "an item that went out carries every field, so a missing one is a lookup miss");
		arrayLeftOut.Should().BeFalse(because: "an array the call left out is not rendered, so its fields were not sent");
		givenArrayMissing.Should().BeTrue(because: "an array the call filled is rendered, so its absence is a lookup miss");
		emptyArray.Should().BeFalse(because: "an array sent empty carries no item field");
	}

	[Test]
	[Description("A setting-bound field under a single Object inside an array item fails closed when neither it nor the Object is in the body: the Object is never given a value, so whether it went out cannot be told.")]
	public void SentSecrets_Should_Fail_Closed_For_A_Field_Under_A_Single_Object() {
		// Arrange
		ServiceMethodDto method = JsonMethod(BodyParameter("UsrLines", "lines", type: "Object", isArray: true, items: [
			BodyParameter("UsrSku", "sku"),
			BodyParameter("UsrMeta", "meta", type: "Object", items: [BodyParameter("UsrKey", "key", "UsrItemKey")])
		]));

		// Act
		WebServiceResponseMasker.SentSecrets(method, JsonRaw, """{"lines":[{"sku":"x"}]}""", false, ["UsrLines"],
			out bool unlocated);

		// Assert
		unlocated.Should().BeTrue(because: "a field under an Object that may have gone out is a lookup miss, not proof it was not sent");
	}

	[Test]
	[Description("An array item field whose path starts with $ is looked for under that name, as the platform keeps an item path as written, so a decoy field of the name without $ is not taken for the setting's value; a top-level $. path still loses its root.")]
	public void SentSecrets_Should_Keep_The_Root_Sign_Of_An_Item_Field_Path() {
		// Arrange
		ServiceMethodDto method = JsonMethod(
			BodyParameter("UsrToken", "$.token", "UsrTopSetting"),
			BodyParameter("UsrLines", "lines", type: "Object", isArray: true, items: [
				BodyParameter("UsrKey", "$apiKey", "UsrItemKey"), BodyParameter("UsrDecoy", "apiKey")
			]));

		// Act
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method, JsonRaw,
			"""{"token":"top-secret-1","lines":[{"$apiKey":"item-secret-2","apiKey":"decoy-3"}]}""", false,
			["UsrLines", "UsrDecoy"], out bool unlocated);

		// Assert
		secrets.Should().Contain("item-secret-2", because: "the item field went out under its path as written, $ included");
		secrets.Should().Contain("top-secret-1", because: "a top-level path's $. names the body root");
		secrets.Should().NotContain("decoy-3", because: "the caller's own field is not a setting value");
		unlocated.Should().BeFalse(because: "every setting value was found where the platform wrote it");
	}

	[Test]
	[Description("A setting-bound field of a SOAP array item whose own placement says header is collected from the envelope body, where the platform renders it, instead of being looked for in the headers.")]
	public void SentSecrets_Should_Find_An_Item_Field_In_The_Body_Whatever_Its_Placement() {
		// Arrange
		ServiceMethodDto method = SoapServiceFixture.Method();
		foreach (ServiceParameterDto lines in method.Request.Parameters.Items.Values) {
			foreach (ServiceParameterDto field in lines.ItemProperties.Items.Values) {
				field.Type = ServiceSchemaCodes.Placements["header"];
			}
		}

		// Act
		IReadOnlyList<string> found = WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw,
			SoapServiceFixture.SentWithItem, false, ["UsrLines"], out bool unlocated);
		WebServiceResponseMasker.SentSecrets(method, SoapServiceFixture.SentRaw,
			SoapServiceFixture.SentWithItem.Replace("<tem:Token>", "<tem:Other>").Replace("</tem:Token>", "</tem:Other>"),
			false, ["UsrLines"], out bool missing);

		// Assert
		found.Should().Contain(SoapServiceFixture.LineTokenValue, because: "the item field is rendered in the envelope body");
		unlocated.Should().BeFalse(because: "the item field was found where the platform put it");
		missing.Should().BeTrue(because: "an item that went out without the field means the lookup missed it");
	}
}
