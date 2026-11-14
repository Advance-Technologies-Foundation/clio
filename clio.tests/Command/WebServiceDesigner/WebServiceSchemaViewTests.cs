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
