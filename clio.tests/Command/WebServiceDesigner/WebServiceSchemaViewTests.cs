using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.WebServiceDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WebServiceSchemaViewTests {

	// Shape of a ServiceSchemaRequest answer for a designer-made service with every secret Studio allows:
	// a constant Basic password, a Bearer header default and credentials in the base URI.
	private const string PlatformRead = """
		{
		 "uId": "5ea85757-61d6-49d6-a235-767e834e5c23", "realUId": "5ea85757-61d6-49d6-a235-767e834e5c23",
		 "name": "UsrLegacy", "caption": {"en-US": "Legacy"}, "description": {}, "extendParent": false,
		 "packageUId": "6d7929ef-24da-1079-a4e8-dfb3c988735c",
		 "baseUri": "https://admin:hunter2@erp.example/api/", "type": 1, "retryCount": 0,
		 "namespaces": {"Items": {}},
		 "methods": {"Items": {"4747679d-db17-473a-90f7-8ff5063b7bcd": {
		  "uId": "4747679d-db17-473a-90f7-8ff5063b7bcd", "name": "GetOrders", "requestTimeout": 5000,
		  "request": {"uri": "orders", "contentType": 1, "httpMethodType": 1, "bodyBuilderId": "00000000-0000-0000-0000-000000000000",
		   "parameters": {"Items": {
		    "a1": {"uId": "0b5b8a39-6a2f-4b71-9f8a-6b8b1b1a0001", "type": 4, "name": "Authorization", "path": "Authorization",
		     "defValue": {"value": "Bearer eyJ-secret", "source": 1}, "dataValueTypeName": "Text", "isArray": false,
		     "isRequired": false, "itemProperties": {"Items": {}}, "caption": {"en-US": "Authorization"}},
		    "a2": {"uId": "0b5b8a39-6a2f-4b71-9f8a-6b8b1b1a0002", "type": 3, "name": "pageSize", "path": "pageSize",
		     "defValue": {"value": "50", "source": 1}, "dataValueTypeName": "Integer", "isArray": false,
		     "isRequired": false, "itemProperties": {"Items": {}}, "caption": {"en-US": "pageSize"}}
		   }}},
		  "response": {"contentType": 1, "parameters": {"Items": {}}},
		  "caption": {"en-US": "Get orders"}, "useAuthInfo": true
		 }}},
		 "authInfo": {"authType": 1, "values": {"Items": {
		  "username": {"value": "UsrErpUser", "source": 2},
		  "password": {"value": "hunter2", "source": 1}
		 }}}
		}
		""";

	[TestCase("https://user:p@ss@erp.example/v1/", "https://[redacted]@erp.example/v1/")]
	[TestCase("https://user:pa ss@erp.example/", "https://[redacted]@erp.example/")]
	[TestCase("https://user:p%41ss@erp.example/", "https://[redacted]@erp.example/")]
	[TestCase("https://user:p\u00e4@erp.example/", "https://[redacted]@erp.example/")]
	[TestCase("https://user:p/ss@erp.example/v1?x=1", "https://[redacted]@erp.example/v1?x=1")]
	[TestCase("https://user:p?ss@erp.example/", "https://[redacted]@erp.example/")]
	[TestCase("https://user:p#ss@erp.example/", "https://[redacted]@erp.example/")]
	[TestCase("https://erp.example/v1/?mail=a@b", "https://[redacted]@b")]
	[TestCase("https://?x", "https://?x")]
	[TestCase("https://erp.example/v1/", "https://erp.example/v1/")]
	[Description("StripUserInfo hides user information in every form Studio can store, including ones Uri cannot parse.")]
	public void StripUserInfo_Should_Fail_Closed(string stored, string expected) {
		// Act
		string result = WebServiceSchemaView.StripUserInfo(stored);

		// Assert
		result.Should().Be(expected, because: "nothing between the scheme and the last '@' may be shown, whatever the password contains");
	}

	[Test]
	[Description("No credential value of a designer-made service reaches the view: Basic password, Bearer header default and base-URI credentials.")]
	public void From_Should_Strip_Every_Credential_Value() {
		// Arrange
		ServiceSchemaDto schema = JsonSerializer.Deserialize<ServiceSchemaDto>(PlatformRead);

		// Act
		string view = JsonSerializer.Serialize(WebServiceSchemaView.From(schema, "Custom"));

		// Assert
		view.Should().NotContain("hunter2", because: "neither the Basic password nor the base-URI password may leave clio");
		view.Should().NotContain("eyJ-secret", because: "a Bearer header default is a credential");
		view.Should().Contain("UsrErpUser", because: "a system-setting code is a reference, not a secret");
		view.Should().Contain("\"constant-credentials-masked\":true", because: "the reader learns a constant credential exists");
	}

	[TestCase("https://erp.example/svc?sig=SECRET-42-SECRET-42&format=json", "https://erp.example/svc?sig=[masked]&format=json")]
	[TestCase("https://erp.example/svc?user=a@corp.com&sig=SECRET-42-SECRET-42", "https://[redacted]@corp.com&sig=[masked]")]
	[TestCase("https://u:pw?token=abc@erp.example/svc?api_key=SECRET-42-SECRET-42", "https://[redacted]@[masked]")]
	[TestCase("https://erp.example/svc.asmx?password=Pa;ss-SECRET-42", "https://erp.example/svc.asmx?password=[masked]")]
	[TestCase("https://erp.example/svc.asmx?user=bob&password=ab@cd-SECRET-42", "https://[redacted]@[masked]")]
	[TestCase("https://erp.example/svc?a/token=SECRET-42-SECRET-42", "https://erp.example/svc?a/token=[masked]")]
	[TestCase("https://erp.example/svc?api%5Fkey=SECRET-42-SECRET-42#top", "https://erp.example/svc?api%5Fkey=[masked]")]
	[TestCase("https://erp.example/svc.asmx?user=ops@corp.com&sig=SECRET-42-SECRET-42&ret=/cb?x=1", "https://[redacted]@corp.com&sig=[masked]&ret=/cb?x=1")]
	[TestCase("https://u:p@erp.example/a=b/svc?sig=SECRET-42-SECRET-42", "https://[redacted]@erp.example/a=b/svc?sig=[masked]")]
	[TestCase("https://erp.example/svc?format=json;api_key=SECRET-42-SECRET-42", "https://erp.example/svc?format=json;api_key=[masked]")]
	[TestCase("https://erp.example/orders?x=1#&api_key=SECRET-42-SECRET-42", "https://erp.example/orders?x=1#&api_key=[masked]")]
	[TestCase("https://erp.example/svc?sig=SECRET-42-SECRET-42?tail", "https://erp.example/svc?sig=[masked]")]
	[TestCase("https://api.example/weather?q=London&appid=SECRET-42-SECRET-42", "https://api.example/weather?q=London&appid=[masked]")]
	[TestCase("https://erp.example/svc?api_key={key}", "https://erp.example/svc?api_key={key}")]
	[TestCase("https://erp.example/svc?api_key={a}SECRET-42-SECRET-42{b}", "https://erp.example/svc?api_key=[masked]")]
	[TestCase("https://erp.example/svc?format=json", "https://erp.example/svc?format=json")]
	[TestCase("https://erp.example/rates?code=USD", "https://erp.example/rates?code=USD")]
	[TestCase("https://erp.example/rates?code=USD;callback=handleResponse", "https://erp.example/rates?code=USD;callback=handleResponse")]
	[TestCase("https://erp.example/odata/Authors(Id={id})/Books", "https://erp.example/odata/Authors(Id={id})/Books")]
	[TestCase("https://erp.example/odata/Products(ProductKey='{id}')", "https://erp.example/odata/Products(ProductKey='{id}')")]
	[TestCase("http://h.example/Svc;token=SECRET-42-SECRET-42?x=1", "http://h.example/Svc;token=[masked]?x=1")]
	[TestCase("https://erp.example/svc?api_key=%5BMasked%5D", "https://erp.example/svc?api_key=%5BMasked%5D")]
	[TestCase("http://h.example/Svc;token=SECRET-42-SECRET-42;sig=SECRET-42-SECRET-42/x", "http://h.example/Svc;token=[masked];sig=[masked]/x")]
	[TestCase("https://erp.example/svc?sig=SE/CRET+42%2FSECRET42", "https://erp.example/svc?sig=[masked]")]
	[Description("ShownUri hides user information and every secret query value: after an '@' in the query that StripUserInfo cut at, whatever follows; with ';' separators; after a fragment; when a path with '=' precedes the query. A short value under code stays visible.")]
	public void ShownUri_Should_Hide_User_Information_And_Query_Secrets(string stored, string expected) {
		// Act
		string shown = WebServiceSchemaView.ShownUri(stored);

		// Assert
		shown.Should().Be(expected, because: "neither user information nor a key kept in the query may be shown");
		shown.Should().NotContain("SECRET-42", because: "the secret value never leaves clio");
	}

	[TestCase("https://u:p?w@erp.example/svc?sig=SECRET-42-SECRET-42")]
	[TestCase("https://erp.example/orders?api_key=SECRET-42-SECRET-42#part")]
	[TestCase("https://erp.example/orders?api_key=SECRET%2D42-SECRET-42")]
	[TestCase("https://erp.example/orders?api_key=SECRET-42+SECRET-42")]
	[Description("UriQuerySecrets finds the value as it goes out on the wire: behind a '?' in a password, without the fragment, and URL-unescaped, so a test call masks its echo.")]
	public void UriQuerySecrets_Should_Return_The_Value_As_Sent(string stored) {
		// Act
		IReadOnlyList<string> secrets = WebServiceSchemaView.UriQuerySecrets(stored);

		// Assert
		secrets.Should().Contain(s => s == "SECRET-42-SECRET-42" || s == "SECRET-42 SECRET-42",
			because: "the echo of a call carries the value as it was sent, a '+' read as a space included");
	}

	[TestCase("sig", "SECRET-42-SECRET-42", true)]
	[TestCase("sas", "0123456789abcdef", true)]
	[TestCase("sign", "0123456789abcde", false)]
	[TestCase("hmac", "0123456789abcdef", true)]
	[TestCase("code", "USD", false)]
	[TestCase("appid", "0123456789abcdef0123", true)]
	[TestCase("api_key", "x", true)]
	[TestCase("format", "SECRET-42-SECRET-42", false)]
	[Description("IsSecretQueryValue takes any value under a credential name and a key-length value under a hosting key name as a secret, and leaves short hosting values and other names alone.")]
	public void IsSecretQueryValue_Should_Tell_Keys_From_Ordinary_Values(string name, string value, bool expected) {
		// Act
		bool secret = WebServiceSchemaView.IsSecretQueryValue(name, value);

		// Assert
		secret.Should().Be(expected, because: "read masking and the write refusal follow the same rule");
	}

	[TestCase("Lk7Hd93Qz20mWr4TbN8e", true, Description = "a letters-and-digits key")]
	[TestCase("0123456789abcdef", true, Description = "a 16-character hex key, the shortest key length")]
	[TestCase("0123456789abcde", false, Description = "15 characters is under the key length")]
	[TestCase("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc", true, Description = "a key behind a scheme")]
	[TestCase("\"QmFzZTY0U2lnbmF0dXJlVmFsdWUx==\"", true, Description = "a quoted, padded base64 key")]
	[TestCase("sig=QmFzZTY0U2lnbmF0dXJlVmFsdWUx", true, Description = "a name=value part with a key value")]
	[TestCase("3fa85f64-5717-4562-b3fc-2c963f66afa6", true, Description = "a GUID, a known false positive")]
	[TestCase("https://bot:hunter2@hooks.example.com/in", true, Description = "an address with user information")]
	[TestCase("https://hooks.example.com/in?token=abc", true, Description = "an address with a credential-named query key")]
	[TestCase("https://hooks.example.com/in?x=QmFzZTY0U2lnbmF0dXJlVmFsdWUx", true, Description = "an address with a key-like query value")]
	[TestCase("https://hooks.example.com/v2/orders/2024/archive?mode=full", false, Description = "an address with digits in its host and path")]
	[TestCase("en-US,en;q=0.9", false, Description = "a language list")]
	[TestCase("en-US", false, Description = "a language tag")]
	[TestCase("2", false, Description = "a version")]
	[TestCase("application/xml", false, Description = "a media type")]
	[TestCase("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false, Description = "a long media type without digits")]
	[TestCase("text/xml; charset=utf-8", false, Description = "a media type with a parameter")]
	[TestCase("max-age=31536000", false, Description = "a directive judged by its value")]
	[TestCase("gzip, deflate, br", false, Description = "a list of words")]
	[TestCase("", false, Description = "an empty value")]
	[TestCase(null, false, Description = "no value")]
	[Description("IsKeyLikeValue takes a part of 16+ characters mixing letters and digits (after quotes, padding and a name= prefix) for a key, and an address only by its user information or query; words, media types, language tags, versions and directives are not keys.")]
	public void IsKeyLikeValue_Should_Tell_Keys_From_Plain_Header_Values(string value, bool expected) {
		// Act
		bool keyLike = WebServiceSchemaView.IsKeyLikeValue(value);

		// Assert
		keyLike.Should().Be(expected, because: "a header constant is hidden and refused only when its value looks like a key");
	}

	[TestCase("Accept-Language", "header", "en-US", false, Description = "neutral name, plain value: shown")]
	[TestCase("X-Partner", "header", "Lk7Hd93Qz20mWr4TbN8e", true, Description = "neutral name, key-like value: secret")]
	[TestCase("X-Api-Key", "header", "1", true, Description = "credential name: secret whatever the value")]
	[TestCase("Authorization", "header", "Basic", true, Description = "Authorization is a credential name")]
	[TestCase("theme", "cookie", "dark", true, Description = "a cookie is secret on its placement")]
	[TestCase("format", "query", "Lk7Hd93Qz20mWr4TbN8e", false, Description = "a query value keeps the query rule: a neutral name is not judged by value")]
	[TestCase("sig", "query", "Lk7Hd93Qz20mWr4TbN8e", true, Description = "a query value keeps the hosting-key rule")]
	[TestCase("mode", "body", "Lk7Hd93Qz20mWr4TbN8e", false, Description = "a body value is judged by name only")]
	[Description("IsSecretLike is the one rule every web service tool follows for a constant default: a cookie always, a credential-like name always, a query value by the query rule, a header value when it looks like a key.")]
	public void IsSecretLike_Should_Apply_One_Rule_Per_Placement(string name, string placement, string value, bool expected) {
		// Act
		bool secret = WebServiceSchemaView.IsSecretLike(name, ServiceSchemaCodes.Placements[placement], value);

		// Assert
		secret.Should().Be(expected, because: "register refuses, describe hides and a test call masks by this one rule");
	}

	[Test]
	[Description("A header constant describe shows is not masked in a test call's echo, and one describe hides is, so no constant is shown in one place and masked in the other.")]
	public void Describe_And_Masker_Should_Agree_On_Header_Constants() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("Send", "GET", "send",
			RequestParameters: [
				new WebServiceParameterArg("AcceptLanguage", "header", Path: "Accept-Language"),
				new WebServiceParameterArg("XPartner", "header", Path: "X-Partner"),
				new WebServiceParameterArg("XApiVersion", "header", Path: "X-Api-Version")
			]), null, false, errors);
		foreach (ServiceParameterDto parameter in method.Request.Parameters.Items.Values) {
			parameter.DefValue = new ServiceParameterValueDto {
				Value = parameter.Name switch {
					"AcceptLanguage" => "en-US,en;q=0.9", "XPartner" => "Lk7Hd93Qz20mWr4TbN8e", _ => "2024-11"
				},
				Source = ServiceSchemaCodes.ValueSourceConst
			};
		}
		var schema = new ServiceSchemaDto { Name = "UsrHeaders", Type = ServiceSchemaCodes.ServiceTypeRest };
		schema.Methods.Items[method.UId.ToString()] = method;

		// Act
		IReadOnlyList<WebServiceParameterView> shown = WebServiceSchemaView.From(schema, "Custom").Methods.Single().RequestParameters;
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method,
			"GET https://x/send HTTP/1.1\r\nAccept-Language: en-US,en;q=0.9\r\nX-Partner: Lk7Hd93Qz20mWr4TbN8e\r\nX-Api-Version: 2024-11", null);

		// Assert
		errors.Should().BeEmpty(because: "the method has no defaults when it is built");
		shown.Single(p => p.Code == "AcceptLanguage").DefaultValue.Should().Be("en-US,en;q=0.9",
			because: "a language list under a neutral name is not a credential");
		shown.Single(p => p.Code == "XApiVersion").DefaultValue.Should().Be("2024-11", because: "a version is not a credential");
		shown.Single(p => p.Code == "XPartner").Should().Match<WebServiceParameterView>(p => p.DefaultValue == null && p.DefaultMasked,
			because: "a key-like value under a neutral name Studio stored stays hidden");
		secrets.Should().Equal(["Lk7Hd93Qz20mWr4TbN8e"],
			because: "a test call masks exactly the header constant describe hides, and leaves the shown ones readable");
	}

	[Test]
	[Description("A method's parameters are listed in stored order, the order the platform renders them in, not sorted by name.")]
	public void From_Should_List_Parameters_In_Stored_Order() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("Send", "POST", "send",
			RequestParameters: [new WebServiceParameterArg("Zeta"), new WebServiceParameterArg("Alpha")]), null, false, errors);
		var schema = new ServiceSchemaDto {
			UId = Guid.NewGuid(), Name = "UsrOrdered", Type = ServiceSchemaCodes.ServiceTypeRest,
			BaseUri = "https://erp.example/"
		};
		schema.Methods.Items[method.UId.ToString()] = method;

		// Act
		WebServiceView view = WebServiceSchemaView.From(schema, "Custom");

		// Assert
		errors.Count.Should().Be(0, because: "the method is valid");
		view.Methods.Single().RequestParameters.Select(p => p.Code).Should().Equal(["Zeta", "Alpha"],
			because: "describe output sent back as setMethod must keep the order, or a SOAP envelope is reordered");
	}

	[Test]
	[Description("A harmless constant default on a query parameter stays visible.")]
	public void From_Should_Keep_Non_Secret_Constant_Default() {
		// Arrange
		ServiceSchemaDto schema = JsonSerializer.Deserialize<ServiceSchemaDto>(PlatformRead);

		// Act
		WebServiceView view = WebServiceSchemaView.From(schema, "Custom");

		// Assert
		WebServiceParameterView pageSize = view.Methods.Single().RequestParameters.Single(p => p.Code == "pageSize");
		pageSize.DefaultValue.Should().Be("50", because: "a page size is not a credential");
		pageSize.DefaultMasked.Should().BeFalse(because: "nothing was hidden");
	}

	[Test]
	[Description("Members the contract does not model survive a read and re-serialization unchanged, so a modify cannot drop them.")]
	public void ServiceSchemaDto_Should_Round_Trip_Unmodelled_Members() {
		// Arrange
		ServiceSchemaDto schema = JsonSerializer.Deserialize<ServiceSchemaDto>(PlatformRead);

		// Act
		string written = JsonSerializer.Serialize(schema);

		// Assert
		written.Should().Contain("bodyBuilderId", because: "the request's body builder is not modelled but must be re-sent");
	}
}
