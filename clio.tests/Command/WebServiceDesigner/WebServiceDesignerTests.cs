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
	private IWebServicePrefixReader _prefix;
	private Clio.Command.WebServiceDesigner.WebServiceDesigner _sut;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IWebServiceDesignerClient>();
		_rights = Substitute.For<ICreatioRightsClient>();
		_prefix = Substitute.For<IWebServicePrefixReader>();
		_rights.GetCanExecuteOperation("CanManageSolution", Arg.Any<CreatioRequestOptions>()).Returns(true);
		_prefix.Read().Returns("Usr");
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
			[new WebServiceOperationArg("addMethod", new WebServiceMethodArg("UsrSuggest", "GET", "suggest"))], null);

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
			[new WebServiceMethodArg("UsrGetOrders", "GET", "orders")]);

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
			new WebServiceMethodArg("UsrGetOrders", "GET", "orders",
				RequestParameters: [new WebServiceParameterArg("UsrApiKey", "header", Path: "X-Api-Key",
					DefaultSysSetting: "UsrErpApiKey")])
		]);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the platform drops a missing setting silently at run time")
			.WithMessage("*UsrErpApiKey*", because: "the message names the missing setting");
	}

	[Test]
	[Description("Registering with oauth20 resolves the OAuth application by name and saves its id.")]
	public void Register_Should_Resolve_OAuth_Application_By_Name() {
		// Arrange
		Guid applicationId = Guid.Parse("0201998c-ecc9-4c78-a8ed-ead83fc1afbb");
		_client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, "UsrErp").Returns(true);
		GivenSelect("SysPackage", new JsonObject { ["UId"] = PackageUId.ToString() });
		_client.Select("OAuthApplications", Arg.Any<IReadOnlyList<string>>(),
				Arg.Is<IReadOnlyDictionary<string, string>>(f => f["Name"] == "Test1"),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>())
			.Returns([new JsonObject { ["Id"] = applicationId.ToString() }]);
		ServiceSchemaDto saved = null;
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => saved = call.Arg<ServiceSchemaDto>());
		_client.ReadSchema(Arg.Any<Guid>(), PackageUId).Returns(_ => saved);

		// Act
		WebServiceToolResult result = _sut.Register("Custom", "UsrErp", null, "https://erp.example/", null,
			new WebServiceAuthArg("oauth20", OAuthApp: "Test1"), [new WebServiceMethodArg("UsrGetOrders", "GET", "orders")]);

		// Assert
		result.Success.Should().BeTrue(because: "the application exists");
		saved.AuthInfo.Values.Items["applicationId"].Value.Should().Be(applicationId.ToString(),
			because: "the platform stores the application by id, not by name");
		saved.Methods.Items.Values.Single().UseAuthInfo.Should().BeTrue(
			because: "a method of a service with authentication sends it by default");
		result.Service.Auth.OAuthAppId.Should().Be(applicationId.ToString(), because: "the read-back shows the reference");
	}

	[Test]
	[Description("setAuth accepts the oauth-app-id describe reports, so authentication can be restored from a describe reply.")]
	public void Modify_SetAuth_Should_Resolve_OAuth_Application_By_Id() {
		// Arrange
		Guid applicationId = Guid.Parse("8695135d-1c0a-4b9e-9d3e-2f1f4f3e2a11");
		GivenRegisteredService(BingSchema());
		_client.Select("OAuthApplications", Arg.Any<IReadOnlyList<string>>(),
				Arg.Is<IReadOnlyDictionary<string, string>>(f => f.ContainsKey("Id") && f["Id"] == applicationId.ToString()),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>())
			.Returns([new JsonObject { ["Id"] = applicationId.ToString() }]);
		ServiceSchemaDto saved = null;
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => saved = call.Arg<ServiceSchemaDto>());

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [
			new WebServiceOperationArg("setAuth", Auth: new WebServiceAuthArg("oauth20", OAuthApp: applicationId.ToString().ToUpperInvariant()))
		], null);

		// Assert
		result.Success.Should().BeTrue(because: "the id names an existing application");
		saved.AuthInfo.Values.Items["applicationId"].Value.Should().Be(applicationId.ToString(),
			because: "the id is looked up by the Id column and stored as the platform stores it");
		_client.DidNotReceive().Select("OAuthApplications", Arg.Any<IReadOnlyList<string>>(),
			Arg.Is<IReadOnlyDictionary<string, string>>(f => f.ContainsKey("Name")), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<string>());
	}

	[Test]
	[Description("Registering with an OAuth application that does not exist is refused before anything is saved.")]
	public void Register_Should_Refuse_Unknown_OAuth_Application() {
		// Arrange
		_client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, "UsrErp").Returns(true);
		GivenSelect("SysPackage", new JsonObject { ["UId"] = PackageUId.ToString() });
		GivenSelect("OAuthApplications");

		// Act
		Action act = () => _sut.Register("Custom", "UsrErp", null, "https://erp.example/", null,
			new WebServiceAuthArg("oauth20", OAuthApp: "NoSuchApp"), [new WebServiceMethodArg("UsrGetOrders", "GET", "orders")]);

		// Assert
		act.Should().Throw<ArgumentException>(because: "a service cannot authenticate with a missing application")
			.WithMessage("*NoSuchApp*", because: "the message names the application");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("list-web-services returns each base URI from the stored metadata, without credentials and without the metadata itself.")]
	public void List_Should_Return_Base_Uri_Without_Credentials() {
		// Arrange
		GivenSelect("VwWebServiceV2", new JsonObject {
			["UId"] = ServiceUId.ToString(), ["PackageUId"] = PackageUId.ToString(), ["Name"] = "UsrErp",
			["Caption"] = "ERP", ["TypeName"] = "REST", ["SysPackageName"] = "Custom",
			["MetaData"] = "\uFEFF{\"MetaData\":{\"Schema\":{\"IQ1\":\"https://admin:hunter2@erp.example/api/\",\"IQ7\":{\"x\":\"hunter2\"}}}}"
		});

		// Act
		WebServiceListItem item = _sut.List().Single();

		// Assert
		item.BaseUri.Should().Be("https://[redacted]@erp.example/api/", because: "the list must not leak base-URI credentials");
		JsonSerializer.Serialize(item).Should().NotContain("hunter2", because: "no part of the metadata is returned");
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
	[Description("Registering a method whose codes lack the environment prefix is refused before anything is saved.")]
	public void Register_Should_Refuse_Unprefixed_Method_Codes() {
		// Arrange
		_client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, "UsrErp").Returns(true);
		GivenSelect("SysPackage", new JsonObject { ["UId"] = PackageUId.ToString() });

		// Act
		Action act = () => _sut.Register("Custom", "UsrErp", null, "https://erp.example/", null, null,
			[new WebServiceMethodArg("GetOrders", "GET", "orders")]);

		// Assert
		act.Should().Throw<ArgumentException>(because: "Studio refuses to save a method whose code lacks the prefix")
			.WithMessage("*'GetOrders'*'Usr'*", because: "the message names the code and the prefix to add");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("modify-web-service checks the prefix on a method it adds, with the prefix resolved from the environment.")]
	public void Modify_Should_Refuse_Adding_An_Unprefixed_Method() {
		// Arrange
		GivenRegisteredService(BingSchema());

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("addMethod", new WebServiceMethodArg("Suggest", "GET", "suggest"))], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "an added method follows the same rule as a registered one")
			.WithMessage("*'Suggest'*", because: "the message names the unprefixed code");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("setMethod keeps the existing unprefixed codes of a method and its parameters, and refuses an unprefixed parameter it adds.")]
	public void Modify_SetMethod_Should_Keep_Existing_Codes_And_Check_New_Ones() {
		// Arrange
		GivenRegisteredService(BingSchema());
		GivenSelect("SysSchema");

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall", [
			new WebServiceOperationArg("setMethod", new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [
				new WebServiceParameterArg("q", "query"), new WebServiceParameterArg("count", "query", Type: "Integer")
			]))
		], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the added parameter lacks the prefix")
			.Which.Message.Should().Contain("'count'", because: "the added parameter is named")
			.And.NotContain("method code 'Search'", because: "the kept method code is not renamed")
			.And.NotContain("code 'q'", because: "the kept parameter code is not renamed");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("setAuth accepts the auth block exactly as describe returns it: oauth-app-id plus the read-only flag.")]
	public void Modify_SetAuth_Should_Accept_The_Describe_Auth_Block() {
		// Arrange
		Guid applicationId = Guid.Parse("5b0f1c0e-7a7d-4c1a-9a51-3f3a0f6a2b10");
		GivenRegisteredService(BingSchema());
		_client.Select("OAuthApplications", Arg.Any<IReadOnlyList<string>>(),
				Arg.Is<IReadOnlyDictionary<string, string>>(f => f.ContainsKey("Id") && f["Id"] == applicationId.ToString()),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>())
			.Returns([new JsonObject { ["Id"] = applicationId.ToString() }]);
		WebServiceAuthArg described = JsonSerializer.Deserialize<WebServiceAuthArg>(
			$$"""{"type":"oauth20","oauth-app-id":"{{applicationId}}","constant-credentials-masked":false}""");

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setAuth", Auth: described)], null);

		// Assert
		described.ExtensionData.Should().BeNullOrEmpty(because: "every field describe returns for auth is a known argument");
		result.Success.Should().BeTrue(because: "the id from describe names an existing application");
	}

	[Test]
	[Description("The same OAuth application given by name and by id is accepted; two different applications are refused readably.")]
	public void Modify_SetAuth_Should_Compare_Resolved_Applications() {
		// Arrange
		Guid erp = Guid.Parse("11111111-2222-3333-4444-555555555555");
		Guid other = Guid.Parse("66666666-7777-8888-9999-000000000000");
		GivenRegisteredService(BingSchema());
		_client.Select("OAuthApplications", Arg.Any<IReadOnlyList<string>>(),
				Arg.Is<IReadOnlyDictionary<string, string>>(f => f.ContainsKey("Name") && f["Name"] == "Erp"),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>())
			.Returns([new JsonObject { ["Id"] = erp.ToString() }]);
		_client.Select("OAuthApplications", Arg.Any<IReadOnlyList<string>>(),
				Arg.Is<IReadOnlyDictionary<string, string>>(f => f.ContainsKey("Id")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>())
			.Returns(call => [new JsonObject { ["Id"] = call.ArgAt<IReadOnlyDictionary<string, string>>(2)["Id"] }]);

		// Act
		WebServiceToolResult same = _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setAuth",
			Auth: new WebServiceAuthArg("oauth20", OAuthApp: "Erp", OAuthAppId: "{" + erp.ToString().ToUpperInvariant() + "}"))], null);
		Action differ = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setAuth",
			Auth: new WebServiceAuthArg("oauth20", OAuthApp: "Erp", OAuthAppId: other.ToString()))], null);

		// Assert
		same.Success.Should().BeTrue(because: "the name and the id resolve to the same application");
		differ.Should().Throw<ArgumentException>(because: "two different applications cannot both be linked")
			.WithMessage("*two different OAuth applications*", because: "the message says what to fix");
	}

	[Test]
	[Description("An object or array as a test-call value is refused by name instead of being sent as raw JSON.")]
	public void ToWireValues_Should_Refuse_Objects_And_Arrays() {
		// Arrange
		var values = new Dictionary<string, JsonElement> { ["UsrItems"] = JsonDocument.Parse("[1,2]").RootElement };

		// Act
		Action act = () => Clio.Command.McpServer.Tools.WebServiceToolRunner.ToWireValues(values);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the test call takes one scalar per top-level parameter")
			.WithMessage("*UsrItems*", because: "the refusal names the parameter");
	}

	[Test]
	[Description("Test-call values arrive as text: numbers and booleans as their JSON text, null left out.")]
	public void ToWireValues_Should_Turn_Json_Scalars_Into_Text() {
		// Arrange
		var values = new Dictionary<string, JsonElement> {
			["UsrAmount"] = JsonDocument.Parse("1.5").RootElement,
			["UsrActive"] = JsonDocument.Parse("true").RootElement,
			["UsrName"] = JsonDocument.Parse("\"Alice\"").RootElement,
			["UsrNote"] = JsonDocument.Parse("null").RootElement
		};

		// Act
		IReadOnlyDictionary<string, string> wire = Clio.Command.McpServer.Tools.WebServiceToolRunner.ToWireValues(values);

		// Assert
		wire.Should().BeEquivalentTo(new Dictionary<string, string> { ["UsrAmount"] = "1.5", ["UsrActive"] = "true", ["UsrName"] = "Alice" },
			because: "the platform's test call takes text, and a null value means the parameter is not sent");
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
	[Description("FindElements reports metadata that names the service but is not JSON as one usage of every method instead of skipping it.")]
	public void FindElements_Should_Fail_Closed_On_Unparsable_Metadata() {
		// Act
		var elements = Clio.Command.WebServiceDesigner.WebServiceDesigner
			.FindElements("not json " + ServiceUId, ServiceUId.ToString()).ToList();

		// Assert
		elements.Should().ContainSingle(because: "a process the scan cannot read may still call the service")
			.Which.Method.Should().BeNull(because: "an unknown method is treated as every method by the usage guard");
	}

	[Test]
	[Description("A process whose metadata names the service but cannot be read blocks every change to a method until it is acknowledged.")]
	public void Modify_Should_Refuse_A_Change_When_A_Caller_Cannot_Be_Read() {
		// Arrange
		GivenRegisteredService(BingSchema());
		GivenSelect("SysSchema", new JsonObject {
			["Name"] = "BrokenProcess", ["Caption"] = "BrokenProcess", ["SysPackageName"] = "Custom",
			["MetaData"] = "{\"a\":1,\"a\":\"" + ServiceUId + "\"}"
		});

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("removeMethod", MethodCode: "Search")], null);

		// Assert
		result.Success.Should().BeFalse(because: "a caller the scan cannot read may call the removed method");
		result.Usages.Should().ContainSingle(u => u.Process == "BrokenProcess" && u.Method == null,
			because: "the unreadable process is named so the user can check it");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("A test call whose setting-bound path value cannot be found in the sent URL returns the status only.")]
	public void TestMethod_Should_Withhold_The_Response_When_A_Path_Secret_Cannot_Be_Located() {
		// Arrange
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(
			new WebServiceMethodArg("Search", "GET", "tenants/{tenant}/search", RequestParameters: [
				new WebServiceParameterArg("tenant", "path", DefaultSysSetting: "UsrTenant")
			]), null, false, errors);
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Clear();
		schema.Methods.Items[method.UId.ToString()] = method;
		GivenRegisteredService(schema);
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyDictionary<string, string>>())
			.Returns(new ServiceMethodCallResult(false, 404, "no tenant hidden-tenant-77", null,
				"GET https://api.bing.example/elsewhere HTTPS/1.1\r\nHost: api.bing.example"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseBody.Should().BeNull(because: "a value that cannot be found cannot be masked, so nothing of the body is returned");
		result.Error.Should().Contain("withheld", because: "the agent is told why the body is missing");
		result.StatusCode.Should().Be(404, because: "the status code carries no secret");
	}

	[Test]
	[Description("oauth-app and oauth-app-id where one names no application are refused naming the unknown one.")]
	public void Modify_SetAuth_Should_Name_The_Unknown_OAuth_Reference() {
		// Arrange
		Guid erp = Guid.Parse("11111111-2222-3333-4444-555555555555");
		GivenRegisteredService(BingSchema());
		_client.Select("OAuthApplications", Arg.Any<IReadOnlyList<string>>(),
				Arg.Is<IReadOnlyDictionary<string, string>>(f => f.ContainsKey("Name")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>())
			.Returns([new JsonObject { ["Id"] = erp.ToString() }]);

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setAuth",
			Auth: new WebServiceAuthArg("oauth20", OAuthApp: "Erp", OAuthAppId: "66666666-7777-8888-9999-000000000000"))], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "an application that does not exist cannot be linked")
			.WithMessage("*oauth-app-id '66666666-7777-8888-9999-000000000000'*", because: "the message names the field to fix, not the valid one");
	}

	[Test]
	[Description("FindElements reads process metadata that starts with a byte-order mark.")]
	public void FindElements_Should_Read_Metadata_With_A_Byte_Order_Mark() {
		// Arrange
		string metadata = "\uFEFF{\"items\":[{\"A2\":\"CallSearch\",\"IX1\":\"" + ServiceUId + "\",\"IX2\":\"Search\"}]}";

		// Act
		var elements = Clio.Command.WebServiceDesigner.WebServiceDesigner
			.FindElements(metadata, ServiceUId.ToString()).ToList();

		// Assert
		elements.Should().ContainSingle(because: "metadata saved by the platform may carry a BOM and is still JSON")
			.Which.Should().Be(("CallSearch", "Search"), because: "the element and the method it calls are read");
	}

	[Test]
	[Description("A method added without use-auth gets authentication when the same call also sets it, whatever the order.")]
	public void Modify_AddMethod_Should_Follow_Authentication_Set_Later_In_The_Same_Call() {
		// Arrange
		Guid applicationId = Guid.Parse("8695135d-1c0a-4b9e-9d3e-2f1f4f3e2a11");
		GivenRegisteredService(BingSchema());
		_client.Select("OAuthApplications", Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>())
			.Returns([new JsonObject { ["Id"] = applicationId.ToString() }]);
		ServiceSchemaDto saved = null;
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => saved = call.Arg<ServiceSchemaDto>());

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [
			new WebServiceOperationArg("addMethod", new WebServiceMethodArg("UsrSuggest", "GET", "suggest")),
			new WebServiceOperationArg("setAuth", Auth: new WebServiceAuthArg("oauth20", OAuthApp: "Erp"))
		], null);

		// Assert
		result.Success.Should().BeTrue(because: "both operations are valid");
		saved.Methods.Items.Values.Single(m => m.Name == "UsrSuggest").UseAuthInfo.Should().BeTrue(
			because: "the new method follows the service's authentication as it is after the call, not before setAuth ran");
		saved.Methods.Items.Values.Single(m => m.Name == "Search").UseAuthInfo.Should().BeFalse(
			because: "setAuth leaves the methods the call does not touch as they were");
	}

	private ServiceSchemaDto SchemaWithMethod(WebServiceMethodArg arg) {
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(arg, null, false, errors);
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Clear();
		schema.Methods.Items[method.UId.ToString()] = method;
		return schema;
	}

	[Test]
	[Description("A test-call value for a code the method does not have is refused with the valid codes, before any request.")]
	public void TestMethod_Should_Refuse_An_Unknown_Parameter_Code() {
		// Arrange
		GivenRegisteredService(BingSchema());

		// Act
		Action act = () => _sut.TestMethod("BingSearchApiCall", "Search", new Dictionary<string, string> { ["qq"] = "x" });

		// Assert
		act.Should().Throw<ArgumentException>(because: "the platform would drop the value silently")
			.WithMessage("*'qq'*valid codes: q*", because: "the refusal lists the codes the method has");
		_client.DidNotReceive().CallMethod(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<IReadOnlyDictionary<string, string>>());
	}

	[Test]
	[Description("When the platform reports no request line and headers, a setting-bound header cannot be masked, so the body is withheld.")]
	public void TestMethod_Should_Withhold_When_The_Sent_Headers_Are_Not_Reported() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [
			new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key", DefaultSysSetting: "UsrBingKey")])));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyDictionary<string, string>>())
			.Returns(new ServiceMethodCallResult(false, null, "echo sk-live-0042", "timeout"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseBody.Should().BeNull(because: "the key may have been sent and cannot be found to mask");
		result.Error.Should().Contain("withheld", because: "the agent is told why there is no body");
	}

	[Test]
	[Description("A method that sends the service authentication is withheld when the platform reports no headers, since the Authorization value cannot be masked.")]
	public void TestMethod_Should_Withhold_An_Authenticated_Call_Without_Reported_Headers() {
		// Arrange
		ServiceSchemaDto schema = SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", UseAuth: true));
		GivenRegisteredService(schema);
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyDictionary<string, string>>())
			.Returns(new ServiceMethodCallResult(false, 401, "bad credentials for admin:hunter2", null));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseBody.Should().BeNull(because: "an echoed Authorization value could not be found to mask");
		result.StatusCode.Should().Be(401, because: "the status code carries no secret");
	}

	[Test]
	[Description("A setting-bound body value in a body that is not JSON (a SOAP envelope) cannot be located, so the body is withheld.")]
	public void TestMethod_Should_Withhold_When_The_Sent_Body_Is_Not_Json() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "POST", "search", RequestParameters: [
			new WebServiceParameterArg("UsrToken", Path: "token", DefaultSysSetting: "UsrBingToken")])));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyDictionary<string, string>>())
			.Returns(new ServiceMethodCallResult(false, 500, "<fault>bad token tok-secret-9</fault>", null,
				"POST https://api.bing.example/search HTTPS/1.1\r\nHost: api.bing.example",
				"<soap:Envelope><soap:Body><token>tok-secret-9</token></soap:Body></soap:Envelope>"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseBody.Should().BeNull(because: "a fault that echoes the envelope would carry the token");
		result.StatusCode.Should().Be(500, because: "the status code carries no secret");
	}

	[Test]
	[Description("A missing setting the service already referenced becomes a notice; one the call adds is refused.")]
	public void Modify_Should_Refuse_Only_Missing_Settings_The_Call_Adds() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [
			new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key", DefaultSysSetting: "UsrDeletedKey")])));
		_client.FindExistingSysSettingCodes(Arg.Any<IReadOnlyCollection<string>>()).Returns(Array.Empty<string>());

		// Act
		WebServiceToolResult unrelated = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("setRetryCount", RetryCount: 2)], null);
		Action adding = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("addMethod",
			new WebServiceMethodArg("UsrSuggest", "GET", "suggest", RequestParameters: [
				new WebServiceParameterArg("UsrKey", "header", Path: "X-Key", DefaultSysSetting: "UsrNewKey")]))], null);

		// Assert
		unrelated.Success.Should().BeTrue(because: "a setting deleted since must not block an unrelated change");
		unrelated.Notices.Should().Contain(n => n.Contains("UsrDeletedKey"),
			because: "the user still learns the reference is broken");
		adding.Should().Throw<ArgumentException>(because: "a new reference to a missing setting would be dropped at run time")
			.WithMessage("*UsrNewKey*", because: "the refusal names the setting");
	}

	[Test]
	[Description("When the SchemaNamePrefix setting cannot be read, register refuses instead of saving without the prefix rule.")]
	public void Register_Should_Refuse_When_The_Prefix_Cannot_Be_Read() {
		// Arrange
		_prefix.Read().Returns(_ => throw new InvalidOperationException("The SchemaNamePrefix system setting could not be read"));

		// Act
		Action act = () => _sut.Register("Custom", "UsrErp", null, "https://erp.example/", null, null,
			[new WebServiceMethodArg("GetOrders", "GET", "orders")]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an unread prefix must not switch the rule off")
			.WithMessage("*SchemaNamePrefix*", because: "the message names what failed");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}
}
