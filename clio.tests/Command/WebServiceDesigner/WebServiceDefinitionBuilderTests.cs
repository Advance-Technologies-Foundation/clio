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
			new WebServiceParameterArg("items", Type: "Object"),
			new WebServiceParameterArg("count", Type: "Integer", Items: [new WebServiceParameterArg("x")])
		]);

		// Act
		WebServiceDefinitionBuilder.BuildMethod(method, null, false, errors);

		// Assert
		errors.Should().HaveCount(2, because: "one Object lacks items and one scalar carries them");
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
