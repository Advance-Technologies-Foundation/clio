using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.WebServiceDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WebServiceDefinitionBuilderTests {

	private static WebServiceMethodArg Orders(params WebServiceParameterArg[] request) =>
		new("GetOrders", "GET", "/orders/{id}", RequestParameters: request);

	[Test]
	[Description("A constant default on a header parameter is refused and the refusal never repeats the value.")]
	public void BuildMethod_Should_Refuse_Constant_Header_Default() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = Orders(new WebServiceParameterArg("id", "path"),
			new WebServiceParameterArg("XApiKey", "header", DefaultValue: "sk-live-123"));

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors);

		// Assert
		errors.Should().ContainSingle(because: "only the header default breaks a rule")
			.Which.Should().Contain("default-sys-setting", because: "the refusal names the allowed route");
		errors.Single().Should().NotContain("sk-live-123", because: "a refused credential is never echoed back");
	}

	[Test]
	[Description("A constant default on a query parameter whose name looks like a credential is refused too.")]
	public void BuildMethod_Should_Refuse_Constant_Default_On_Credential_Like_Name() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = Orders(new WebServiceParameterArg("id", "path"),
			new WebServiceParameterArg("api_token", "query", DefaultValue: "abc"));

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors);

		// Assert
		errors.Should().ContainSingle(because: "a token in a query string is as secret as one in a header");
	}

	[Test]
	[Description("A system-setting reference becomes source 2 with the code as value, and a path parameter is required by default.")]
	public void BuildMethod_Should_Bind_SysSetting_Default_And_Require_Path_Parameter() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = Orders(new WebServiceParameterArg("id", "path"),
			new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key", DefaultSysSetting: "UsrErpApiKey"));

		// Act
		ServiceMethodDto built = WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors);

		// Assert
		errors.Should().BeEmpty(because: "the method is valid");
		built.Request.Uri.Should().Be("orders/{id}", because: "the leading slash is dropped, as Studio stores it");
		ServiceParameterDto header = built.Request.Parameters.Items.Values.Single(p => p.Name == "XApiKey");
		header.DefValue.Source.Should().Be(ServiceSchemaCodes.ValueSourceSysSetting,
			because: "the platform reads the value from the setting at call time");
		header.DefValue.Value.Should().Be("UsrErpApiKey", because: "the stored value is the setting code");
		built.Request.Parameters.Items.Values.Single(p => p.Name == "id").IsRequired.Should().BeTrue(
			because: "a path segment cannot be omitted");
	}

	[Test]
	[Description("A {placeholder} without a path parameter and a path parameter without a placeholder are both reported.")]
	public void BuildMethod_Should_Report_Path_Placeholder_Mismatches() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = Orders(new WebServiceParameterArg("orderId", "path"));

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors);

		// Assert
		errors.Should().HaveCount(2, because: "{id} has no parameter and orderId has no placeholder");
	}

	[Test]
	[Description("Rebuilding a method keeps its UId and the UIds of the parameters that keep their code, nested items included.")]
	public void BuildMethod_Should_Keep_UIds_Of_Kept_Parameters() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg first = new("GetOrders", "GET", "orders", ResponseParameters: [
			new WebServiceParameterArg("items", Type: "Object", Array: true,
				Items: [new WebServiceParameterArg("number"), new WebServiceParameterArg("total")])
		]);
		ServiceMethodDto existing = WebServiceDefinitionBuilder.BuildMethod(first, null, false, errors);
		WebServiceMethodArg second = first with {
			ResponseParameters = [
				new WebServiceParameterArg("items", Type: "Object", Array: true,
					Items: [new WebServiceParameterArg("number")])
			]
		};

		// Act
		ServiceMethodDto rebuilt = WebServiceDefinitionBuilder.BuildMethod(second, existing, false, errors);

		// Assert
		errors.Should().BeEmpty(because: "both definitions are valid");
		rebuilt.UId.Should().Be(existing.UId, because: "a process element finds the method by name, the designer by UId");
		ServiceParameterDto oldItems = existing.Response.Parameters.Items.Values.Single();
		ServiceParameterDto newItems = rebuilt.Response.Parameters.Items.Values.Single();
		newItems.UId.Should().Be(oldItems.UId, because: "a kept parameter keeps its identity");
		newItems.ItemProperties.Items.Values.Single().UId.Should().Be(
			oldItems.ItemProperties.Items.Values.Single(p => p.Name == "number").UId,
			because: "nested items are matched by code as well");
	}

	[Test]
	[Description("An Object parameter needs items, and items are refused on a scalar.")]
	public void BuildMethod_Should_Validate_Object_Items() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("GetOrders", "GET", "orders", ResponseParameters: [
			new WebServiceParameterArg("items", Type: "Object", Array: true),
			new WebServiceParameterArg("count", Type: "Integer", Items: [new WebServiceParameterArg("x")])
		]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors);

		// Assert
		errors.Should().HaveCount(2, because: "one Object array lacks items and one scalar carries them");
	}

	[Test]
	[Description("A new single Object parameter is refused, request or response, top level or nested, with the dotted-path alternative.")]
	public void BuildMethod_Should_Refuse_A_Single_Object() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("CreateOrder", "POST", "orders",
			RequestParameters: [
				new WebServiceParameterArg("customer", Type: "Object", Items: [new WebServiceParameterArg("name")])
			],
			ResponseParameters: [
				new WebServiceParameterArg("lines", Type: "Object", Array: true, Items: [
					new WebServiceParameterArg("product", Type: "Object", Items: [new WebServiceParameterArg("sku")])
				])
			]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors);

		// Assert
		errors.Should().HaveCount(2, because: "the request object and the object nested in the array are single objects");
		errors.Should().OnlyContain(e => e.Contains("must be an array") && e.Contains("customer.name"),
			because: "each refusal names the rule and the dotted-path alternative");
	}

	[Test]
	[Description("A single nested object described by dotted paths is accepted and the paths are stored as given.")]
	public void BuildMethod_Should_Accept_Dotted_Paths_For_A_Nested_Object() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("UsrCreateOrder", "POST", "orders",
			RequestParameters: [new WebServiceParameterArg("UsrCustomerName", Path: "customer.name")],
			ResponseParameters: [new WebServiceParameterArg("UsrCustomerName", Path: "$.customer.name")]);

		// Act
		ServiceMethodDto built = WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors, "Usr");

		// Assert
		errors.Should().BeEmpty(because: "flat parameters with dotted paths are how Studio describes a nested object");
		built.Request.Parameters.Items.Values.Single().Path.Should().Be("customer.name",
			because: "the platform splits the path into nested JSON when it renders the body");
		built.Response.Parameters.Items.Values.Single().Path.Should().Be("$.customer.name",
			because: "the response path is a JSONPath read as given");
	}

	[Test]
	[Description("A single Object the service already has is kept on a rebuild, as other existing codes are.")]
	public void BuildMethod_Should_Keep_An_Existing_Single_Object() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto existing = WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("Orders", "GET", "orders"),
			null, false, errors);
		existing.Response.Parameters.Items["x"] = new ServiceParameterDto {
			UId = Guid.NewGuid(), Name = "meta", Path = "$.meta", DataValueTypeName = "Object"
		};
		WebServiceMethodArg rebuilt = new("Orders", "GET", "orders", ResponseParameters: [
			new WebServiceParameterArg("meta", Type: "Object", Items: [new WebServiceParameterArg("total")])
		]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(rebuilt, existing, false, errors);

		// Assert
		errors.Should().BeEmpty(because: "changing a kept parameter's shape is the user's call, not a new single object");
	}

	[TestCase("https://api.example.com/v1", "https://api.example.com/v1/")]
	[TestCase("https://api.example.com/v1/", "https://api.example.com/v1/")]
	[Description("The base URI is stored with a trailing slash because the designer concatenates base and method URI verbatim.")]
	public void NormalizeBaseUri_Should_End_With_Slash(string input, string expected) {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();

		// Act
		string normalized = WebServiceDefinitionBuilder.NormalizeBaseUri(input, errors);

		// Assert
		normalized.Should().Be(expected, because: "orders/{id} must not glue onto the last path segment");
		errors.Should().BeEmpty(because: "the URI is valid");
	}

	[TestCase("https://user:secret@api.example.com/")]
	[TestCase("ftp://api.example.com/")]
	[TestCase("api.example.com")]
	[Description("A base URI with credentials, a non-http scheme or no scheme is refused without echoing a password.")]
	public void NormalizeBaseUri_Should_Refuse_Invalid_Uri(string input) {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();

		// Act
		WebServiceDefinitionBuilder.NormalizeBaseUri(input, errors);

		// Assert
		errors.Should().ContainSingle(because: "the URI breaks a rule");
		errors.Single().Should().NotContain("secret", because: "credentials in a URI are never echoed");
	}

	[Test]
	[Description("Basic auth takes system-setting codes only and stores them as SysSetting sources.")]
	public void BuildAuth_Should_Store_Basic_Credentials_As_SysSetting_References() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();

		// Act
		ServiceAuthInfoDto auth = WebServiceDefinitionBuilder.BuildAuth(
			new WebServiceAuthArg("basic", "UsrErpUser", "UsrErpPassword"), null, errors);

		// Assert
		errors.Should().BeEmpty(because: "both codes were given");
		auth.Values.Items.Values.Should().OnlyContain(v => v.Source == ServiceSchemaCodes.ValueSourceSysSetting,
			because: "the agent never supplies a credential value");
	}

	[Test]
	[Description("OAuth 2.0 stores the resolved application id as a constant applicationId, the shape a designer-made service has.")]
	public void BuildAuth_Should_Store_OAuth_Application_Id() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		Guid applicationId = Guid.Parse("0201998c-ecc9-4c78-a8ed-ead83fc1afbb");

		// Act
		ServiceAuthInfoDto auth = WebServiceDefinitionBuilder.BuildAuth(
			new WebServiceAuthArg("oauth20", OAuthApp: "Test1"), applicationId, errors);

		// Assert
		errors.Should().BeEmpty(because: "the application was found");
		auth.AuthType.Should().Be(ServiceSchemaCodes.AuthOAuth20, because: "oauth20 maps to the platform's OAuth 2.0 type");
		auth.Values.Items.Should().ContainSingle(because: "OAuth 2.0 carries only the application reference")
			.Which.Should().Match<KeyValuePair<string, ServiceParameterValueDto>>(item =>
				item.Key == "applicationId" && item.Value.Value == applicationId.ToString()
				&& item.Value.Source == ServiceSchemaCodes.ValueSourceConst,
				because: "Studio reads the application from a constant applicationId (measured on 10.2.565)");
	}

	[Test]
	[Description("OAuth 2.0 without a resolved application is refused and names the application that was not found.")]
	public void BuildAuth_Should_Refuse_OAuth_Without_Application() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();

		// Act
		ServiceAuthInfoDto auth = WebServiceDefinitionBuilder.BuildAuth(
			new WebServiceAuthArg("oauth20", OAuthApp: "NoSuchApp"), null, errors);

		// Assert
		auth.Should().BeNull(because: "nothing valid can be stored without the application");
		errors.Should().ContainSingle(because: "the missing application is the only problem")
			.Which.Should().Contain("NoSuchApp", because: "the refusal names what the caller passed");
	}

	[Test]
	[Description("A new method and its new request, response and nested parameters must carry the schema-name prefix, and every unprefixed code is reported in one answer.")]
	public void BuildMethod_Should_Require_Prefix_On_New_Method_And_Parameter_Codes() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("GetOrders", "GET", "orders/{id}",
			RequestParameters: [new WebServiceParameterArg("id", "path")],
			ResponseParameters: [
				new WebServiceParameterArg("UsrItems", Type: "Object", Array: true, Path: "$.items",
					Items: [new WebServiceParameterArg("number")])
			]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors, "Usr");

		// Assert
		errors.Should().HaveCount(3, because: "the method code, the path parameter and the nested item each lack the prefix");
		errors.Should().Contain(e => e.Contains("'GetOrders'"), because: "the unprefixed method code is named");
		errors.Should().Contain(e => e.Contains("'id'"), because: "the unprefixed request parameter is named");
		errors.Should().Contain(e => e.Contains("'number'"), because: "a nested item is checked like a top-level parameter");
		errors.Should().OnlyContain(e => e.Contains("'Usr'"), because: "each refusal names the prefix to add");
	}

	[Test]
	[Description("Prefixed codes pass, and the wire names still come from uri and path, not from the codes.")]
	public void BuildMethod_Should_Accept_Prefixed_Codes_And_Keep_Wire_Names() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("UsrGetOrders", "GET", "orders/{id}",
			RequestParameters: [new WebServiceParameterArg("UsrId", "path", Path: "id")]);

		// Act
		ServiceMethodDto built = WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors, "Usr");

		// Assert
		errors.Should().BeEmpty(because: "every code carries the prefix");
		built.Request.Parameters.Items.Values.Single().Path.Should().Be("id",
			because: "the prefix is part of the code only; the request still fills {id}");
	}

	[Test]
	[Description("Rebuilding an existing method keeps its unprefixed code and the unprefixed codes of parameters it keeps, but a parameter it adds needs the prefix.")]
	public void BuildMethod_Should_Keep_Existing_Unprefixed_Codes_On_Rebuild() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto existing = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [new WebServiceParameterArg("q", "query")]),
			null, false, errors);
		WebServiceMethodArg rebuilt = new("Search", "GET", "search", RequestParameters: [
			new WebServiceParameterArg("q", "query"),
			new WebServiceParameterArg("count", "query", Type: "Integer")
		]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(rebuilt, existing, false, errors, "Usr");

		// Assert
		errors.Should().ContainSingle(because: "renaming kept codes would break the processes that call the method")
			.Which.Should().Contain("'count'", because: "only the parameter this call introduces is checked");
	}

	[Test]
	[Description("An environment without a schema-name prefix has no prefix rule, as in Studio.")]
	public void BuildMethod_Should_Not_Require_Prefix_When_Environment_Has_None() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("GetOrders", "GET", "orders/{id}",
			RequestParameters: [new WebServiceParameterArg("id", "path")]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors, string.Empty);

		// Assert
		errors.Should().BeEmpty(because: "Studio checks the prefix only when SchemaNamePrefix is set");
	}

	[Test]
	[Description("A new parameter whose code carries the prefix must name its wire name in path, or the API would see the prefixed code.")]
	public void BuildMethod_Should_Require_Path_On_New_Prefixed_Parameter() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("UsrGetOrders", "GET", "orders",
			RequestParameters: [new WebServiceParameterArg("UsrPage", "query")],
			ResponseParameters: [new WebServiceParameterArg("UsrStatus")]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors, "Usr");

		// Assert
		errors.Should().HaveCount(2, because: "neither the query parameter nor the response value names its wire name");
		errors.Should().OnlyContain(e => e.Contains("needs path"), because: "the refusal says what to add");
	}

	[Test]
	[Description("The prefix check is case-sensitive like Studio's, and a case-only change of a kept method code is not exempt.")]
	public void BuildMethod_Should_Check_Prefix_Case_Sensitively() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto existing = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("UsrGetOrders", "GET", "orders"), null, false, errors, "Usr");
		WebServiceMethodArg recased = new("usrGetOrders", "GET", "orders");

		// Act
		WebServiceDefinitionBuilder.BuildMethod(recased, existing, false, errors, "Usr");

		// Assert
		errors.Should().ContainSingle(because: "Studio's ^Usr check would refuse usrGetOrders")
			.Which.Should().Contain("'usrGetOrders'", because: "the refusal names the re-cased code");
	}

	[Test]
	[Description("Under a kept Object parameter the kept item keeps its unprefixed code and wire name, and a new item needs the prefix.")]
	public void BuildMethod_Should_Check_Only_New_Nested_Items() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto existing = WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("Orders", "GET", "orders",
			ResponseParameters: [new WebServiceParameterArg("items", Type: "Object", Array: true, Path: "$.items",
				Items: [new WebServiceParameterArg("number", Path: "num")])]), null, false, errors);
		WebServiceMethodArg rebuilt = new("Orders", "GET", "orders", ResponseParameters: [
			new WebServiceParameterArg("items", Type: "Object", Array: true,
				Items: [new WebServiceParameterArg("number"), new WebServiceParameterArg("total")])
		]);

		// Act
		ServiceMethodDto built = WebServiceDefinitionBuilder.BuildMethod(rebuilt, existing, false, errors, "Usr");

		// Assert
		errors.Should().ContainSingle(because: "only the added item is new")
			.Which.Should().Contain("'total'", because: "the refusal names the new item");
		built.Response.Parameters.Items.Values.Single().Path.Should().Be("$.items",
			because: "a kept parameter sent without path keeps its stored wire name");
	}

	[Test]
	[Description("A constant default is refused when the wire name looks like a credential even though the code does not.")]
	public void BuildMethod_Should_Refuse_Constant_When_Wire_Name_Is_Credential_Like() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("UsrGetOrders", "GET", "orders", RequestParameters: [
			new WebServiceParameterArg("UsrAccess", "query", Path: "api_key", DefaultValue: "sk-live-1")
		]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors, "Usr");

		// Assert
		errors.Should().ContainSingle(because: "the API receives the constant as api_key")
			.Which.Should().Contain("default-sys-setting", because: "the refusal names the allowed route");
	}

	[Test]
	[Description("Response parameters can be read from a response header or cookie, and describe reports where each one is read from.")]
	public void BuildMethod_Should_Keep_Header_And_Cookie_Response_Placements() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("UsrGetOrders", "GET", "orders", ResponseParameters: [
			new WebServiceParameterArg("UsrRequestId", "header", Path: "X-Request-Id"),
			new WebServiceParameterArg("UsrSession", "cookie", Path: "sid"),
			new WebServiceParameterArg("UsrStatus", Path: "$.status")
		]);

		// Act
		ServiceMethodDto built = WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors, "Usr");
		WebServiceMethodView view = WebServiceSchemaView.From(new ServiceSchemaDto {
			Name = "UsrErp", Type = ServiceSchemaCodes.ServiceTypeRest,
			Methods = { Items = { [built.UId.ToString()] = built } }
		}, "Custom").Methods.Single();

		// Assert
		errors.Should().BeEmpty(because: "Studio offers body, header and cookie for a response parameter");
		view.ResponseParameters.Select(p => (p.Code, p.In)).Should().BeEquivalentTo(
			new[] { ("UsrRequestId", "header"), ("UsrSession", "cookie"), ("UsrStatus", "body") },
			because: "describe must report the real placement, or a setMethod rebuilt from it turns a header into a body value");
	}

	[Test]
	[Description("A response parameter cannot be read from a query, path or unknown placement.")]
	public void BuildMethod_Should_Refuse_A_Request_Only_Placement_On_A_Response_Parameter() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("UsrGetOrders", "GET", "orders",
			ResponseParameters: [new WebServiceParameterArg("UsrPage", "query", Path: "page")]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors, "Usr");

		// Assert
		errors.Should().ContainSingle(because: "a response has no query string")
			.Which.Should().Contain("body, header or cookie", because: "the refusal names the allowed placements");
	}

	[Test]
	[Description("A GET with a body request parameter is refused, as Studio refuses it; a POST with the same parameter passes.")]
	public void BuildMethod_Should_Refuse_A_Body_Parameter_On_Get() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceParameterArg[] body = [new WebServiceParameterArg("UsrNumber", Path: "number")];

		// Act
		WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("UsrGetOrders", "GET", "orders",
			RequestParameters: body), null, false, errors, "Usr");
		var postErrors = new WebServiceDefinitionBuilder.Errors();
		WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("UsrPostOrders", "POST", "orders",
			RequestParameters: body), null, false, postErrors, "Usr");

		// Assert
		errors.Should().ContainSingle(because: ".NET refuses to send a body with GET and Studio cannot save one")
			.Which.Should().Contain("GET", because: "the refusal names the rule");
		postErrors.Should().BeEmpty(because: "a POST carries a body");
	}

	[Test]
	[Description("An xml method with a body parameter in its request or its response is refused; one with only query and header parameters passes.")]
	public void BuildMethod_Should_Refuse_Body_Parameters_On_An_Xml_Method() {
		// Arrange
		var requestErrors = new WebServiceDefinitionBuilder.Errors();
		var responseErrors = new WebServiceDefinitionBuilder.Errors();
		var plainErrors = new WebServiceDefinitionBuilder.Errors();

		// Act
		WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("UsrAdd", "POST", "add", ContentType: "xml",
			RequestParameters: [new WebServiceParameterArg("UsrA", Path: "a")]), null, false, requestErrors, "Usr");
		WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("UsrAdd", "GET", "add", ContentType: "xml",
			ResponseParameters: [new WebServiceParameterArg("UsrResult", Path: "AddResult")]), null, false, responseErrors, "Usr");
		WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("UsrAdd", "GET", "add", ContentType: "xml",
			RequestParameters: [new WebServiceParameterArg("UsrA", "query", Path: "a")],
			ResponseParameters: [new WebServiceParameterArg("UsrTrace", "header", Path: "X-Trace")]), null, false,
			plainErrors, "Usr");

		// Assert
		requestErrors.Should().ContainSingle(because: "the platform builds a REST body as JSON only")
			.Which.Should().Contain("RequestBody", because: "the refusal names the process route for an XML body");
		responseErrors.Should().ContainSingle(because: "Studio refuses a body parameter in an xml response as well");
		plainErrors.Should().BeEmpty(because: "an xml method without body parameters is one Studio saves");
	}

	[Test]
	[Description("A header or cookie parameter carries one value: an Object there is refused, and a field inside an Object array cannot have its own placement.")]
	public void BuildMethod_Should_Refuse_Objects_In_Headers_And_Placements_On_Nested_Fields() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		WebServiceMethodArg method = new("UsrGetOrders", "GET", "orders", ResponseParameters: [
			new WebServiceParameterArg("UsrMeta", "header", Type: "Object", Array: true, Path: "X-Meta",
				Items: [new WebServiceParameterArg("UsrA", Path: "a")]),
			new WebServiceParameterArg("UsrLines", Type: "Object", Array: true, Path: "$.lines",
				Items: [new WebServiceParameterArg("UsrTrace", "header", Path: "x-trace")])
		]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors, "Usr");

		// Assert
		errors.Should().HaveCount(2, because: "the header Object and the header field inside the array are both invalid");
		errors.Should().Contain(e => e.Contains("header or cookie parameter carries one value"),
			because: "an Object in a header is refused");
		errors.Should().Contain(e => e.Contains("no placement of its own"), because: "a nested field cannot be a header");
	}

	[Test]
	[Description("A parameter read back by describe-web-service, read-only default-masked included, is valid input for a rebuild.")]
	public void Describe_Shaped_Parameter_Should_Deserialize_Without_Unknown_Fields() {
		// Arrange
		const string described = """
			{"code":"UsrApiKey","caption":"UsrApiKey","in":"header","path":"X-Api-Key","type":"Text","array":false,
			 "required":false,"default-sys-setting":null,"default-value":null,"default-masked":true,"items":[]}
			""";

		// Act
		WebServiceParameterArg arg = System.Text.Json.JsonSerializer.Deserialize<WebServiceParameterArg>(described);

		// Assert
		arg.ExtensionData.Should().BeNullOrEmpty(because: "every field describe returns is a known argument, read-only ones included");
		arg.DefaultMasked.Should().BeTrue(because: "the read-only flag binds and is then ignored");
	}

	[Test]
	[Description("A kept parameter whose secret constant default describe hides keeps that default when the rebuild sends none.")]
	public void BuildMethod_Should_Keep_A_Hidden_Secret_Constant_On_Rebuild() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto existing = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key")]),
			null, false, errors);
		ServiceParameterDto key = existing.Request.Parameters.Items.Values.Single();
		key.DefValue = new ServiceParameterValueDto { Value = "sk-studio-1", Source = ServiceSchemaCodes.ValueSourceConst };
		WebServiceMethodArg rebuilt = new("Search", "GET", "search", TimeoutMs: 9000, RequestParameters: [
			new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key", DefaultMasked: true)
		]);

		// Act
		ServiceMethodDto built = WebServiceDefinitionBuilder.BuildMethod(rebuilt, existing, false, errors, "Usr");

		// Assert
		errors.Should().BeEmpty(because: "the kept header is resent as describe showed it");
		ServiceParameterValueDto kept = built.Request.Parameters.Items.Values.Single().DefValue;
		kept.Value.Should().Be("sk-studio-1", because: "describe hid the constant, so leaving it out must not erase it");
		kept.Source.Should().Be(ServiceSchemaCodes.ValueSourceConst, because: "the default stays a constant");
	}

	[Test]
	[Description("A hidden secret constant is not carried into a visible place; an explicit empty default clears it; a system setting replaces it.")]
	public void BuildMethod_Should_Not_Expose_Or_Pin_A_Hidden_Secret_Constant() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto existing = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [new WebServiceParameterArg("XPartner", "header", Path: "X-Partner")]),
			null, false, errors);
		existing.Request.Parameters.Items.Values.Single().DefValue =
			new ServiceParameterValueDto { Value = "s3cr3t-partner", Source = ServiceSchemaCodes.ValueSourceConst };
		WebServiceMethodArg Moved(WebServiceParameterArg p) => new("Search", "GET", "search", RequestParameters: [p]);
		var movedErrors = new WebServiceDefinitionBuilder.Errors();
		var clearedErrors = new WebServiceDefinitionBuilder.Errors();
		var settingErrors = new WebServiceDefinitionBuilder.Errors();

		// Act
		WebServiceDefinitionBuilder.BuildMethod(Moved(new WebServiceParameterArg("XPartner", "query", Path: "partner")),
			existing, false, movedErrors);
		ServiceMethodDto cleared = WebServiceDefinitionBuilder.BuildMethod(
			Moved(new WebServiceParameterArg("XPartner", "header", Path: "X-Partner", DefaultValue: "")), existing, false, clearedErrors);
		ServiceMethodDto setting = WebServiceDefinitionBuilder.BuildMethod(
			Moved(new WebServiceParameterArg("XPartner", "header", Path: "X-Partner", DefaultSysSetting: "UsrPartnerKey")),
			existing, false, settingErrors);

		// Assert
		movedErrors.Should().ContainSingle(because: "a query parameter would print the secret in the next reply and send it in the URL")
			.Which.Should().NotContain("s3cr3t-partner", because: "the refusal never repeats the hidden value");
		clearedErrors.Should().BeEmpty(because: "an explicit empty default is how a hidden constant is removed");
		cleared.Request.Parameters.Items.Values.Single().DefValue.Value.Should().BeEmpty(
			because: "the cleared parameter sends no constant any more");
		settingErrors.Should().BeEmpty(because: "a system-setting reference is the allowed replacement");
		setting.Request.Parameters.Items.Values.Single().DefValue.Source.Should().Be(ServiceSchemaCodes.ValueSourceSysSetting,
			because: "the setting replaces the hidden constant instead of being ignored");
	}

	[Test]
	[Description("A constant describe shows (not secret-like) is dropped when the rebuild leaves it out, since the caller saw it.")]
	public void BuildMethod_Should_Drop_A_Visible_Constant_The_Rebuild_Omits() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto existing = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Search", "GET", "search",
				RequestParameters: [new WebServiceParameterArg("page", "query", DefaultValue: "1")]), null, false, errors);

		// Act
		ServiceMethodDto rebuilt = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [new WebServiceParameterArg("page", "query")]),
			existing, false, errors);

		// Assert
		errors.Should().BeEmpty(because: "the rebuild is valid");
		rebuilt.Request.Parameters.Items.Values.Single().DefValue.Value.Should().BeEmpty(
			because: "only a constant describe hid is kept; a visible one the caller left out is removed");
	}

	[Test]
	[Description("An unknown field in the auth block is reported readably after error redaction.")]
	public void BuildAuth_Unknown_Field_Message_Should_Survive_Redaction() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		var auth = new WebServiceAuthArg("none") {
			ExtensionData = new Dictionary<string, System.Text.Json.JsonElement> {
				["tokenUrl"] = System.Text.Json.JsonDocument.Parse("\"x\"").RootElement
			}
		};

		// Act
		WebServiceDefinitionBuilder.BuildAuth(auth, null, errors);
		string redacted = Clio.Common.SensitiveErrorTextRedactor.Redact(string.Join(" ", errors));

		// Assert
		redacted.Should().Contain("unknown field(s) tokenUrl", because: "the agent needs the field name to correct the call");
	}

	[Test]
	[Description("A service code without the environment prefix is refused.")]
	public void ValidateServiceCode_Should_Require_Prefix() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();

		// Act
		WebServiceDefinitionBuilder.ValidateServiceCode("ErpOrders", "Usr", errors);

		// Assert
		errors.Should().ContainSingle(because: "Creatio rejects an unprefixed custom schema elsewhere, and the save path does not");
	}

	[Test]
	[Description("Errors.ThrowIfAny joins every problem into one ArgumentException so the agent can fix them in one retry.")]
	public void Errors_Should_Throw_All_Messages_At_Once() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors { "first.", "second." };

		// Act
		Action act = errors.ThrowIfAny;

		// Assert
		act.Should().Throw<ArgumentException>(because: "there are errors")
			.WithMessage("first. second.", because: "both problems are reported together");
	}

	[TestCase("https://api.example.com/v1?api_key=SECRET")]
	[TestCase("https://api.example.com/v1/#part")]
	[Description("A base URI with a query or a fragment is refused, since it would precede every method path and could store a key.")]
	public void NormalizeBaseUri_Should_Refuse_A_Query_Or_Fragment(string baseUri) {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();

		// Act
		WebServiceDefinitionBuilder.NormalizeBaseUri(baseUri, errors);

		// Assert
		errors.Should().ContainSingle(because: "query values belong in query parameters")
			.Which.Should().Contain("query parameters", because: "the refusal names the alternative");
	}

	[Test]
	[Description("A credential-like key with a constant value in a method URI query is refused; an ordinary fixed query is kept.")]
	public void BuildMethod_Should_Refuse_A_Constant_Credential_In_The_Uri_Query() {
		// Arrange
		var refused = new WebServiceDefinitionBuilder.Errors();
		var kept = new WebServiceDefinitionBuilder.Errors();

		// Act
		WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("Search", "GET", "search?api_key=abc123"),
			null, false, refused);
		WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("Search", "GET", "search?format=json"),
			null, false, kept);

		// Assert
		refused.Should().ContainSingle(because: "a key in the URI is a stored constant every reader of the schema sees")
			.Which.Should().Contain("default-sys-setting", because: "the refusal points at the setting route");
		kept.Should().BeEmpty(because: "a fixed query value that is not a credential is fine");
	}

	[Test]
	[Description("A path parameter may fill a placeholder in the base URI, as the platform substitutes those too.")]
	public void BuildMethod_Should_Accept_A_Placeholder_In_The_Base_Uri() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();

		// Act
		WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("Orders", "GET", "orders",
				RequestParameters: [new WebServiceParameterArg("version", "path")]),
			null, false, errors, null, "https://api.example.com/{version}/");

		// Assert
		errors.Should().BeEmpty(because: "{version} in the base URI is filled from the path parameter");
	}

	[Test]
	[Description("Items read from JSON keep the case-insensitive key comparison, so a remove by another letter case still finds the item.")]
	public void ServiceItemCollection_Should_Keep_Its_Comparer_After_Deserialization() {
		// Act
		var collection = System.Text.Json.JsonSerializer.Deserialize<ServiceItemCollection<int>>(
			"{\"Items\":{\"AB12CD34-0000-0000-0000-000000000001\":1}}");

		// Assert
		collection!.Items.Remove("ab12cd34-0000-0000-0000-000000000001").Should().BeTrue(
			because: "the platform's key case must not decide whether setMethod replaces or duplicates a method");
	}
}
