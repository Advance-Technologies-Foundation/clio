using System;
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
