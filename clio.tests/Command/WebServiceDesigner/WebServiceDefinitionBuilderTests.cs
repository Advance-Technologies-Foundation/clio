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
}
