using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.WebServiceDesigner;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WebServiceDesignerTests {

	private static readonly Guid ServiceUId = Guid.Parse("505a9338-4878-46a2-a099-ffb433b8b591");
	private static readonly Guid PackageUId = Guid.Parse("c1a044a2-cb8c-9387-837b-d9ae9183cd47");

	private IWebServiceDesignerClient _client;
	private ICreatioRightsClient _rights;
	private ISchemaNamePrefixResolver _prefix;
	private Clio.Command.WebServiceDesigner.WebServiceDesigner _sut;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IWebServiceDesignerClient>();
		_rights = Substitute.For<ICreatioRightsClient>();
		_prefix = Substitute.For<ISchemaNamePrefixResolver>();
		_rights.GetCanExecuteOperation("CanManageSolution", Arg.Any<CreatioRequestOptions>()).Returns(true);
		_prefix.Resolve(null).Returns("Usr");
		_sut = new Clio.Command.WebServiceDesigner.WebServiceDesigner(_client, _rights, _prefix);
	}

	private void GivenSelect(string root, params JsonObject[] rows) =>
		_client.Select(root, Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>()).Returns(rows);

	private void GivenRegisteredService(ServiceSchemaDto schema) {
		GivenSelect("VwWebServiceV2", new JsonObject {
			["UId"] = ServiceUId.ToString(), ["PackageUId"] = PackageUId.ToString(), ["Name"] = "BingSearchApiCall",
			["Caption"] = "Bing", ["TypeName"] = "REST", ["SysPackageName"] = "CrtCustomer360AI"
		});
		_client.ReadSchema(ServiceUId, PackageUId).Returns(schema);
	}

	private static ServiceSchemaDto BingSchema() {
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [new WebServiceParameterArg("q", "query")]),
			null, false, errors);
		var schema = new ServiceSchemaDto {
			UId = ServiceUId, RealUId = ServiceUId, Name = "BingSearchApiCall", PackageUId = PackageUId,
			Type = ServiceSchemaCodes.ServiceTypeRest, BaseUri = "https://api.bing.example/"
		};
		schema.Methods.Items[method.UId.ToString()] = method;
		return schema;
	}

	private static JsonObject ProcessRow(string name, string method) => new() {
		["Name"] = name, ["Caption"] = name, ["SysPackageName"] = "CrtCustomer360AI",
		["MetaData"] = JsonSerializer.Serialize(new {
			MetaData = new { Schema = new { BK4 = new object[] {
				new { BL1 = "Terrasoft.Core.Process.ProcessSchemaWebService", A2 = "WebService1",
					IX1 = ServiceUId.ToString(), IX2 = method }
			} } }
		})
	};

	[Test]
	[Description("Removing a method a process calls is refused, names the process and saves nothing.")]
	public void Modify_Should_Refuse_Removing_A_Called_Method_Without_Acknowledgement() {
		// Arrange
		GivenRegisteredService(BingSchema());
		GivenSelect("SysSchema", ProcessRow("BingSearchProcess", "Search"));

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("removeMethod", MethodCode: "Search")], null);

		// Assert
		result.Success.Should().BeFalse(because: "the change would break BingSearchProcess silently");
		result.Usages.Should().ContainSingle(u => u.Process == "BingSearchProcess" && u.Element == "WebService1",
			because: "the refusal names the process and its element");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("The same removal saves when the user acknowledged the named process.")]
	public void Modify_Should_Save_When_Usages_Are_Acknowledged() {
		// Arrange
		GivenRegisteredService(BingSchema());
		GivenSelect("SysSchema", ProcessRow("BingSearchProcess", "Search"));

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("removeMethod", MethodCode: "Search")], ["BingSearchProcess"]);

		// Assert
		result.Success.Should().BeTrue(because: "the user agreed to break that process");
		_client.Received(1).SaveSchema(Arg.Is<ServiceSchemaDto>(s => s.Methods.Items.Count == 0));
	}

	[Test]
	[Description("Adding a method never consults usages and saves directly.")]
	public void Modify_Should_Add_Method_Without_Usage_Scan() {
		// Arrange
		GivenRegisteredService(BingSchema());

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("addMethod", new WebServiceMethodArg("Suggest", "GET", "suggest"))], null);

		// Assert
		result.Success.Should().BeTrue(because: "an added method breaks no caller");
		_client.DidNotReceive().Select("SysSchema", Arg.Any<IReadOnlyList<string>>(),
			Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>());
	}

	[Test]
	[Description("Registering a code another service already uses is refused, because the platform save would overwrite that service.")]
	public void Register_Should_Refuse_Taken_Code() {
		// Arrange
		_client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, "UsrErp").Returns(false);
		GivenSelect("SysPackage", new JsonObject { ["UId"] = PackageUId.ToString() });

		// Act
		Action act = () => _sut.Register("Custom", "UsrErp", null, "https://erp.example/", null, null,
			[new WebServiceMethodArg("GetOrders", "GET", "orders")]);

		// Assert
		act.Should().Throw<ArgumentException>(because: "an existing service must be changed with modify-web-service")
			.WithMessage("*already used*", because: "the message says why");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("A reference to a system setting that does not exist is refused before anything is saved.")]
	public void Register_Should_Refuse_Missing_SysSetting() {
		// Arrange
		_client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, "UsrErp").Returns(true);
		GivenSelect("SysPackage", new JsonObject { ["UId"] = PackageUId.ToString() });
		GivenSelect("SysSettings");

		// Act
		Action act = () => _sut.Register("Custom", "UsrErp", null, "https://erp.example/", null, null, [
			new WebServiceMethodArg("GetOrders", "GET", "orders",
				RequestParameters: [new WebServiceParameterArg("XApiKey", "header", DefaultSysSetting: "UsrErpApiKey")])
		]);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the platform drops a missing setting silently at run time")
			.WithMessage("*UsrErpApiKey*", because: "the message names the missing setting");
	}

	[Test]
	[Description("A test call never hands back a key the platform filled in from a system setting, even when the API echoes it.")]
	public void TestMethod_Should_Mask_A_Setting_Value_The_Api_Echoes() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [
				new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key", DefaultSysSetting: "UsrBingKey")
			]), null, false, errors);
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Clear();
		schema.Methods.Items[method.UId.ToString()] = method;
		GivenRegisteredService(schema);
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyDictionary<string, string>>())
			.Returns(new ServiceMethodCallResult(false, 401, """{"error":"key sk-live-0042 is not valid"}""", null,
				"GET https://api.bing.example/search HTTPS/1.1\r\nX-Api-Key: sk-live-0042\r\nHost: api.bing.example"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseBody.Should().NotContain("sk-live-0042", because: "an error body that quotes the key must not reach the agent");
		result.ResponseBody.Should().Contain(WebServiceResponseMasker.Mask, because: "the reader sees where a value was hidden");
		result.SentSecretsMasked.Should().BeTrue(because: "the result says that something was masked");
		result.StatusCode.Should().Be(401, because: "masking changes the body only");
	}

	[Test]
	[Description("Every tool that reads or writes a service refuses a user without CanManageSolution.")]
	public void Describe_Should_Require_CanManageSolution() {
		// Arrange
		_rights.GetCanExecuteOperation("CanManageSolution", Arg.Any<CreatioRequestOptions>()).Returns(false);

		// Act
		Action act = () => _sut.Describe("BingSearchApiCall", false);

		// Assert
		act.Should().Throw<UnauthorizedAccessException>(because: "the platform's own read is not gated, so clio gates it");
		_client.DidNotReceive().ReadSchema(Arg.Any<Guid>(), Arg.Any<Guid>());
	}

	[Test]
	[Description("FindElements returns nothing for metadata that is not JSON instead of throwing.")]
	public void FindElements_Should_Ignore_Unparsable_Metadata() {
		// Act
		var elements = Clio.Command.WebServiceDesigner.WebServiceDesigner
			.FindElements("not json " + ServiceUId, ServiceUId.ToString()).ToList();

		// Assert
		elements.Should().BeEmpty(because: "a damaged process row must not stop the scan");
	}
}
