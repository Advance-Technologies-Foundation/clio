using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
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

	private void GivenRegisteredService(ServiceSchemaDto schema, string name = "BingSearchApiCall") {
		GivenSelect("VwWebServiceV2", new JsonObject {
			["UId"] = ServiceUId.ToString(), ["PackageUId"] = PackageUId.ToString(), ["Name"] = name,
			["Caption"] = "Bing", ["TypeName"] = WebServiceSchemaView.TypeName(schema.Type),
			["SysPackageName"] = "CrtCustomer360AI"
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
		result.Error.Should().Contain("lose their parameters", because: "a removed method leaves the element unbound");
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
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrErp", null, "https://erp.example/", null, null,
			[new WebServiceMethodArg("UsrGetOrders", "GET", "orders")]));

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
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrErp", null, "https://erp.example/", null, null, [
			new WebServiceMethodArg("UsrGetOrders", "GET", "orders",
				RequestParameters: [new WebServiceParameterArg("UsrApiKey", "header", Path: "X-Api-Key",
					DefaultSysSetting: "UsrErpApiKey")])
		]));

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
		WebServiceToolResult result = _sut.Register("Custom", new WebServiceRegistration("UsrErp", null, "https://erp.example/", null,
			new WebServiceAuthArg("oauth20", OAuthApp: "Test1"), [new WebServiceMethodArg("UsrGetOrders", "GET", "orders")]));

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
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrErp", null, "https://erp.example/", null,
			new WebServiceAuthArg("oauth20", OAuthApp: "NoSuchApp"), [new WebServiceMethodArg("UsrGetOrders", "GET", "orders")]));

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
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
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
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrErp", null, "https://erp.example/", null, null,
			[new WebServiceMethodArg("GetOrders", "GET", "orders")]));

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
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
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
		Action act = () => _sut.TestMethod("BingSearchApiCall", "Search",
			new Dictionary<string, JsonElement> { ["qq"] = JsonSerializer.SerializeToElement("x") });

		// Assert
		act.Should().Throw<ArgumentException>(because: "the platform would drop the value silently")
			.WithMessage("*'qq'*valid codes: q*", because: "the refusal lists the codes the method has");
		_client.DidNotReceive().CallMethod(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<IReadOnlyList<ServiceCallParameter>>());
	}

	[TestCase("POST")]
	[TestCase("PUT")]
	[TestCase("PATCH")]
	[TestCase("DELETE")]
	[Description("A test call of a method that can change data, without the user's confirmation, is refused with confirmation-required and nothing is sent; with the confirmation it goes out.")]
	public void TestMethod_Should_Refuse_A_Data_Changing_Call_Until_The_User_Confirms(string verb) {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", verb, "search")));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, "{}", null, "POST https://api.bing.example/search HTTP/1.1", "{}"));

		// Act
		WebServiceTestResult refused = _sut.TestMethod("BingSearchApiCall", "Search", null);
		int sentWhenRefused = _client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IWebServiceDesignerClient.CallMethod));
		WebServiceTestResult confirmed = _sut.TestMethod("BingSearchApiCall", "Search", null, userConfirmed: true);

		// Assert
		sentWhenRefused.Should().Be(0, because: "nothing reaches the external API before the user's yes");
		refused.Success.Should().BeFalse(because: "a request to test is not a confirmation");
		refused.ConfirmationRequired.Should().BeTrue(because: "the result says the user has to be asked first");
		refused.StatusCode.Should().BeNull(because: "nothing was sent");
		refused.Error.Should().Contain($"a {verb} method", because: "the refusal names why the call needs a yes")
			.And.Contain("do not set user-confirmed yourself", because: "the agent must ask, not confirm on its own")
			.And.Contain("user-confirmed: true", because: "the refusal says how to repeat the call after the yes");
		confirmed.Success.Should().BeTrue(because: "the confirmed call goes out");
		confirmed.ConfirmationRequired.Should().BeNull(because: "a confirmed call carries no confirmation flag");
		_client.Received(1).CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>());
	}

	[Test]
	[Description("Every SOAP test call needs the user's confirmation, because each one is a POST whose effect clio cannot know.")]
	public void TestMethod_Should_Refuse_A_Soap_Call_Until_The_User_Confirms() {
		// Arrange
		GivenRegisteredService(SoapServiceFixture.Schema(), "UsrSoapProbe");

		// Act
		WebServiceTestResult result = _sut.TestMethod("UsrSoapProbe", "UsrOp", null);

		// Assert
		result.ConfirmationRequired.Should().BeTrue(because: "a SOAP call can change data in the external API");
		result.Error.Should().Contain("a SOAP method", because: "the refusal names why the call needs a yes");
		_client.DidNotReceive().CallMethod(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<IReadOnlyList<ServiceCallParameter>>());
	}

	[Test]
	[Description("A GET is sent without the user's confirmation: it does not change data.")]
	public void TestMethod_Should_Send_A_Get_Without_Confirmation() {
		// Arrange
		GivenRegisteredService(BingSchema());
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, "{}", null, "GET https://api.bing.example/search HTTP/1.1", null));

		// Act
		WebServiceTestResult get = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		get.ConfirmationRequired.Should().BeNull(because: "a GET does not change data");
		get.Success.Should().BeTrue(because: "the GET went out");
		_client.Received(1).CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>());
	}

	[TestCase("xml", "a POST method")]
	[TestCase("unknown verb", "a non-GET method")]
	[Description("A REST method with an XML body and a method of an unknown verb need the user's confirmation: a custom body builder can send the first, and the second may change data.")]
	public void TestMethod_Should_Ask_Before_An_Xml_Or_Unknown_Verb_Call(string kind, string named) {
		// Arrange
		ServiceSchemaDto schema = kind == "xml"
			? SchemaWithMethod(new WebServiceMethodArg("Search", "POST", "search", ContentType: "xml"))
			: SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search"));
		if (kind != "xml") {
			foreach (ServiceMethodDto method in schema.Methods.Items.Values) {
				method.Request.HttpMethodType = 0;
			}
		}
		GivenRegisteredService(schema);

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ConfirmationRequired.Should().BeTrue(because: $"a {kind} method may change data when it goes out");
		result.Error.Should().Contain(named, because: "the refusal names why the call needs a yes");
		_client.DidNotReceiveWithAnyArgs().CallMethod(default, default, default);
	}

	private ServiceSchemaDto OrdersSchema() => SchemaWithMethod(new WebServiceMethodArg("Search", "POST", "orders",
		ContentType: "json", RequestParameters: [
			new WebServiceParameterArg("UsrLines", "body", "Object", "lines", Array: true, Items: [
				new WebServiceParameterArg("UsrSku", Path: "sku"),
				new WebServiceParameterArg("UsrKey", Path: "key", DefaultSysSetting: "UsrErpKey")
			])
		]));

	[Test]
	[Description("An array of objects reaches the platform in nested, and a setting value the platform filled into every item and an API echoed is masked in the body and the parsed values.")]
	public void TestMethod_Should_Send_An_Object_Array_And_Mask_A_Setting_Value_Filled_Into_Items() {
		// Arrange
		GivenRegisteredService(OrdersSchema());
		IReadOnlyList<ServiceCallParameter> sent = null;
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Do<IReadOnlyList<ServiceCallParameter>>(p => sent = p))
			.Returns(new ServiceMethodCallResult(true, 200,
				"{\"json\":{\"lines\":[{\"sku\":\"S1\",\"key\":\"sk-erp-4711\"},{\"sku\":\"S2\",\"key\":\"sk-erp-4711\"}]}}", null,
				"POST https://api.bing.example/orders HTTP/1.1\r\nContent-Type: application/json",
				"{\"lines\":[{\"sku\":\"S1\",\"key\":\"sk-erp-4711\"},{\"sku\":\"S2\",\"key\":\"sk-erp-4711\"}]}",
				"{\"UsrEcho\":\"sk-erp-4711\"}"));
		var values = new Dictionary<string, JsonElement> {
			["UsrLines"] = JsonDocument.Parse("[{\"UsrSku\":\"S1\"},{\"UsrSku\":\"S2\"}]").RootElement
		};

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", values, userConfirmed: true);

		// Assert
		JsonSerializer.Serialize(sent).Should().Be(
			"[{\"code\":\"UsrLines\",\"nested\":[[{\"code\":\"UsrSku\",\"value\":\"S1\"}],[{\"code\":\"UsrSku\",\"value\":\"S2\"}]]}]",
			because: "an array of objects goes out one list of field values per item, as Studio's test request sends it");
		result.ResponseBody.Should().NotContain("sk-erp-4711", because: "the setting value of an item field is found by its name")
			.And.Contain("[masked]", because: "the echoed value is replaced, not removed");
		result.ResponseValues!.Value.GetRawText().Should().NotContain("sk-erp-4711",
			because: "the parsed values are masked like the body");
		result.SentSecretsMasked.Should().BeTrue(because: "the result says that something was masked");
	}

	[Test]
	[Description("An unknown field inside an array item is refused with its location before any request is sent.")]
	public void TestMethod_Should_Refuse_An_Unknown_Field_Inside_An_Item_Before_Calling() {
		// Arrange
		GivenRegisteredService(OrdersSchema());
		var values = new Dictionary<string, JsonElement> {
			["UsrLines"] = JsonDocument.Parse("[{\"UsrSku\":\"S1\",\"UsrPrice\":5}]").RootElement
		};

		// Act
		Action act = () => _sut.TestMethod("BingSearchApiCall", "Search", values);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the platform would drop an unknown field silently")
			.WithMessage("*parameters.UsrLines[0]: 'UsrPrice' is not a field of 'UsrLines' items; valid codes: UsrKey, UsrSku.*",
				because: "the refusal names the item and the codes it has");
		_client.DidNotReceive().CallMethod(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<IReadOnlyList<ServiceCallParameter>>());
	}

	[Test]
	[Description("A SOAP call that filled an array of objects but left its inner array out returns the masked response instead of withholding it, because nothing of the inner array was sent.")]
	public void TestMethod_Should_Return_A_Soap_Response_When_An_Inner_Array_Was_Left_Out() {
		// Arrange
		GivenRegisteredService(SoapServiceFixture.SchemaWithInnerArray(), "UsrSoapProbe");
		_client.CallMethod(Arg.Any<string>(), "UsrOp", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, $"<ok>{SoapServiceFixture.LineTokenValue}</ok>", null,
				SoapServiceFixture.SentRaw, SoapServiceFixture.SentWithItem));
		var values = new Dictionary<string, JsonElement> { ["UsrLines"] = JsonDocument.Parse("[{}]").RootElement };

		// Act
		WebServiceTestResult result = _sut.TestMethod("UsrSoapProbe", "UsrOp", values, userConfirmed: true);

		// Assert
		result.ResponseBody.Should().Be("<ok>[masked]</ok>",
			because: "the item token the platform filled is found and masked, and the unsent inner array withholds nothing");
		result.SentSecretsMasked.Should().BeTrue(because: "the result says that something was masked");
	}

	[Test]
	[Description("When the platform reports no request line and headers, a setting-bound header cannot be masked, so the body is withheld.")]
	public void TestMethod_Should_Withhold_When_The_Sent_Headers_Are_Not_Reported() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [
			new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key", DefaultSysSetting: "UsrBingKey")])));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
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
		schema.AuthInfo = WebServiceDefinitionBuilder.BuildAuth(
			new WebServiceAuthArg("basic", UsernameSysSetting: "UsrUser", PasswordSysSetting: "UsrPassword"), null,
			new WebServiceDefinitionBuilder.Errors());
		GivenRegisteredService(schema);
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, 401, "bad credentials for admin:hunter2", null));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseBody.Should().BeNull(because: "an echoed Authorization value could not be found to mask");
		result.StatusCode.Should().Be(401, because: "the status code carries no secret");
	}

	[Test]
	[Description("An OAuth call that fails before anything is sent is still withheld, but the message points at the OAuth application instead of system settings.")]
	public void TestMethod_Should_Explain_An_OAuth_Call_That_Sent_Nothing() {
		// Arrange
		ServiceSchemaDto schema = SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", UseAuth: true));
		schema.AuthInfo = new ServiceAuthInfoDto { AuthType = ServiceSchemaCodes.AuthOAuth20 };
		GivenRegisteredService(schema);
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, 0, null, "Access token cannot be retrieved: client_secret=s3cr3t"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.Error.Should().Contain("OAuth application", because: "the call failed while getting the token, not on a setting")
			.And.Contain("Send client credentials", because: "the measured cause is the client-credentials placement")
			.And.NotContain("system setting could not be located", because: "no system setting is involved")
			.And.NotContain("s3cr3t", because: "the token endpoint's error can quote the client secret, which clio cannot mask");
		result.ResponseBody.Should().BeNull(because: "nothing of the platform's answer is returned");
	}

	[TestCase(true, "Typical causes")]
	[TestCase(false, "system setting could not be located")]
	[Description("A withheld call names the right cause: nothing sent without OAuth lists typical causes; something sent keeps the system-setting wording.")]
	public void TestMethod_Should_Word_The_Withheld_Answer_By_What_Was_Sent(bool nothingSent, string expected) {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "POST", "search", RequestParameters: [
			new WebServiceParameterArg("UsrToken", Path: "token", DefaultSysSetting: "UsrBingToken")])));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, 0, null, "timeout", nothingSent ? null : "POST https://x/search HTTPS/1.1",
				nothingSent ? null : "token=plain text, neither JSON nor XML"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null, userConfirmed: true);

		// Assert
		result.ResponseBody.Should().BeNull(because: "the answer stays withheld in both cases");
		result.Error.Should().Contain(expected, because: "the message matches what the platform reported as sent");
	}

	[Test]
	[Description("A successful OAuth call with nothing reported as sent is withheld without claiming a failure or a token problem.")]
	public void TestMethod_Should_Not_Call_A_Successful_Withheld_Call_A_Failure() {
		// Arrange
		ServiceSchemaDto schema = SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", UseAuth: true));
		schema.AuthInfo = new ServiceAuthInfoDto { AuthType = ServiceSchemaCodes.AuthOAuth20 };
		GivenRegisteredService(schema);
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, "{\"token\":\"echo\"}", null));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseBody.Should().BeNull(because: "the credentials it carried could not be located to mask");
		result.Error.Should().Contain("the call succeeded", because: "the message stays true to what happened")
			.And.NotContain("access token could not be retrieved", because: "the token was retrieved for a call that worked");
	}

	[Test]
	[Description("An OAuth service's method that does not use the authentication gets no OAuth hint when it sent nothing.")]
	public void TestMethod_Should_Not_Blame_OAuth_For_A_Method_Without_Authentication() {
		// Arrange
		ServiceSchemaDto schema = SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", UseAuth: false,
			RequestParameters: [new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key", DefaultSysSetting: "UsrBingKey")]));
		schema.AuthInfo = new ServiceAuthInfoDto { AuthType = ServiceSchemaCodes.AuthOAuth20 };
		GivenRegisteredService(schema);
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, 0, null, "host unreachable"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.Error.Should().NotContain("OAuth application", because: "the method requests no token")
			.And.Contain("Typical causes", because: "the generic causes still apply");
	}

	[Test]
	[Description("setAuth none clears use-auth on every method after the usage guard, so Studio does not show a ticked, disabled flag.")]
	public void Modify_SetAuth_None_Should_Clear_Use_Auth() {
		// Arrange
		ServiceSchemaDto schema = SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", UseAuth: true));
		schema.AuthInfo = WebServiceDefinitionBuilder.BuildAuth(
			new WebServiceAuthArg("basic", UsernameSysSetting: "UsrUser", PasswordSysSetting: "UsrPassword"), null,
			new WebServiceDefinitionBuilder.Errors());
		GivenRegisteredService(schema);
		ServiceSchemaDto saved = null;
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => saved = call.Arg<ServiceSchemaDto>());

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("setAuth", Auth: new WebServiceAuthArg("none"))], null);

		// Assert
		result.Success.Should().BeTrue(because: "no process calls the service in this test");
		saved.Methods.Items.Values.Should().OnlyContain(m => !m.UseAuthInfo,
			because: "a service without authentication sends none, and the flag would show ticked and disabled in Studio");
		result.Notices.Should().Contain(n => n.Contains("use-auth was cleared on Search"),
			because: "the user learns which methods must be set again if authentication comes back");
	}

	[Test]
	[Description("Registering a method with use-auth but no authentication saves it without use-auth and names it in a notice.")]
	public void Register_Should_Clear_Use_Auth_Without_Authentication() {
		// Arrange
		_client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, "UsrErp").Returns(true);
		GivenSelect("SysPackage", new JsonObject { ["UId"] = PackageUId.ToString() });
		ServiceSchemaDto saved = null;
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => saved = call.Arg<ServiceSchemaDto>());
		_client.ReadSchema(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(_ => saved);

		// Act
		WebServiceToolResult result = _sut.Register("Custom", new WebServiceRegistration("UsrErp", null,
			"https://erp.example/", null, null, [new WebServiceMethodArg("UsrGetOrders", "GET", "orders", UseAuth: true)]));

		// Assert
		saved.Methods.Items.Values.Single().UseAuthInfo.Should().BeFalse(
			because: "a service without authentication sends none, so the flag would only show ticked and disabled in Studio");
		result.Notices.Should().Contain(n => n.Contains("use-auth was cleared on UsrGetOrders"),
			because: "the caller asked for use-auth and learns why it was not kept");
	}

	[Test]
	[Description("setDescription writes the new text into every culture the service already has, so no culture keeps the old one.")]
	public void Modify_SetDescription_Should_Replace_Every_Culture() {
		// Arrange
		ServiceSchemaDto schema = BingSchema();
		schema.Description = new Dictionary<string, string> { ["uk-UA"] = "Old text" };
		GivenRegisteredService(schema);
		ServiceSchemaDto saved = null;
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => saved = call.Arg<ServiceSchemaDto>());

		// Act
		_sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setDescription", Description: "New text")], null);

		// Assert
		saved.Description.Should().BeEquivalentTo(new Dictionary<string, string> { ["uk-UA"] = "New text", ["en-US"] = "New text" },
			because: "the platform keeps a culture the contract does not send, which would show the old text in that culture");
	}

	[Test]
	[Description("A change that does not remove the authentication leaves use-auth as it was.")]
	public void Modify_Should_Not_Clear_Use_Auth_Without_Removing_Authentication() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", UseAuth: true)));
		ServiceSchemaDto saved = null;
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => saved = call.Arg<ServiceSchemaDto>());

		// Act
		_sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setRetryCount", RetryCount: 1)], null);

		// Assert
		saved.Methods.Items.Values.Single().UseAuthInfo.Should().BeTrue(
			because: "only the call that removes the authentication clears the flags");
	}

	[Test]
	[Description("setDescription without a description is refused instead of clearing it by accident.")]
	public void Modify_SetDescription_Should_Refuse_A_Missing_Description() {
		// Arrange
		GivenRegisteredService(BingSchema());

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setDescription")], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "an omitted description is not a request to clear it")
			.WithMessage("*setDescription*", because: "the refusal names the operation");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("setDescription sets the service description and an empty one clears it.")]
	public void Modify_SetDescription_Should_Set_And_Clear_The_Description() {
		// Arrange
		GivenRegisteredService(BingSchema());
		var saved = new List<string>();
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>()))
			.Do(call => saved.Add(string.Join("|", call.Arg<ServiceSchemaDto>().Description.Values)));

		// Act
		_sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setDescription", Description: "Orders API of the ERP")], null);
		_sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setDescription", Description: "")], null);

		// Assert
		saved[0].Should().Be("Orders API of the ERP", because: "the description is stored like Studio's");
		saved[1].Should().BeEmpty(because: "an empty description clears it, sent as an empty value per culture");
	}

	[Test]
	[Description("A method that keeps use-auth after the service authentication was removed sends no Authorization, so nothing is withheld for it.")]
	public void TestMethod_Should_Not_Withhold_When_The_Service_Has_No_Authentication() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", UseAuth: true)));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, 404, "not found", null));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseBody.Should().Be("not found", because: "the platform sends no credentials when the service has none");
	}

	[Test]
	[Description("setBaseUri re-checks the methods the call keeps against the placeholders of the new base URI, whatever the operation order.")]
	public void Modify_SetBaseUri_Should_Recheck_Kept_Methods_Against_New_Placeholders() {
		// Arrange
		GivenRegisteredService(BingSchema());

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("setBaseUri", BaseUri: "https://api.bing.example/{version}/")], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "no kept method has a path parameter for {version}")
			.WithMessage("*{version}*", because: "the refusal names the placeholder");
	}

	[Test]
	[Description("setBaseUri that removes a base-URI placeholder a kept method's path parameter fills is refused, unless the method's own URI still has it.")]
	public void Modify_SetBaseUri_Should_Refuse_Removing_A_Placeholder_A_Kept_Method_Fills() {
		// Arrange
		ServiceSchemaDto schema = SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search",
			RequestParameters: [new WebServiceParameterArg("version", "path")]));
		schema.BaseUri = "https://api.bing.example/{version}/";
		ServiceSchemaDto ownUri = SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "{version}/search",
			RequestParameters: [new WebServiceParameterArg("version", "path")]));
		ownUri.BaseUri = "https://api.bing.example/{version}/";
		var removedErrors = new WebServiceDefinitionBuilder.Errors();
		var ownErrors = new WebServiceDefinitionBuilder.Errors();

		// Act
		WebServiceDefinitionBuilder.ValidateBaseUriChange(schema.Methods.Items.Values.Single(), schema.BaseUri,
			"https://api.bing.example/", removedErrors);
		WebServiceDefinitionBuilder.ValidateBaseUriChange(ownUri.Methods.Items.Values.Single(), ownUri.BaseUri,
			"https://api.bing.example/", ownErrors);

		// Assert
		removedErrors.Should().ContainSingle(because: "the path parameter would fill nothing and the call would go out without it")
			.Which.Should().Contain("{version}", because: "the refusal names the lost placeholder");
		ownErrors.Should().BeEmpty(because: "the method's own URI still carries the placeholder");
	}

	[Test]
	[Description("A kept method whose own placeholder differs in case from its path parameter does not block a setBaseUri that adds no placeholder.")]
	public void Modify_SetBaseUri_Should_Not_Recheck_A_Kept_Methods_Own_Uri() {
		// Arrange
		ServiceSchemaDto schema = SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "orders/{id}",
			RequestParameters: [new WebServiceParameterArg("id", "path")]));
		ServiceMethodDto legacy = schema.Methods.Items.Values.Single();
		legacy.Request.Uri = "orders/{Id}";
		GivenRegisteredService(schema);

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("setBaseUri", BaseUri: "https://api.bing.example/v2/")], null);

		// Assert
		result.Success.Should().BeTrue(because: "only placeholders the base-URI change adds or removes are checked");
	}

	[Test]
	[Description("A method added in the call that reuses a missing setting another method already referenced is a new reference and is refused.")]
	public void Modify_Should_Refuse_A_New_Reference_To_A_Missing_Setting_Another_Method_Had() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", RequestParameters: [
			new WebServiceParameterArg("XApiKey", "header", Path: "X-Api-Key", DefaultSysSetting: "UsrDeletedKey")])));
		_client.FindExistingSysSettingCodes(Arg.Any<IReadOnlyCollection<string>>()).Returns(Array.Empty<string>());

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("addMethod",
			new WebServiceMethodArg("UsrSuggest", "GET", "suggest", RequestParameters: [
				new WebServiceParameterArg("UsrKey", "header", Path: "X-Key", DefaultSysSetting: "UsrDeletedKey")]))], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the new method would go out without its key")
			.WithMessage("*UsrDeletedKey*", because: "the refusal names the setting");
	}

	[Test]
	[Description("A setting-bound body value in a body that is neither JSON nor XML cannot be located, so the body is withheld.")]
	public void TestMethod_Should_Withhold_When_The_Sent_Body_Is_Neither_Json_Nor_Xml() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "POST", "search", RequestParameters: [
			new WebServiceParameterArg("UsrToken", Path: "token", DefaultSysSetting: "UsrBingToken")])));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, 500, "<fault>bad token tok-secret-9</fault>", null,
				"POST https://api.bing.example/search HTTPS/1.1\r\nHost: api.bing.example",
				"token=tok-secret-9"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null, userConfirmed: true);

		// Assert
		result.ResponseBody.Should().BeNull(because: "a fault that echoes the body would carry the token");
		result.StatusCode.Should().Be(500, because: "the status code carries no secret");
	}

	[Test]
	[Description("A SOAP fault that echoes the WS-Security password from a system setting comes back with the password masked, instead of being withheld.")]
	public void TestMethod_Should_Mask_A_Setting_Value_Echoed_From_A_Soap_Envelope() {
		// Arrange
		GivenRegisteredService(SoapServiceFixture.Schema(), "UsrSoapProbe");
		_client.CallMethod(Arg.Any<string>(), "UsrOp", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, 500,
				"<soap:Fault><soap:Reason>bad password pw-s3cret&amp;&lt;9&gt; for inner-key-77</soap:Reason></soap:Fault>",
				null, SoapServiceFixture.SentRaw, SoapServiceFixture.SentEnvelope));

		// Act
		WebServiceTestResult result = _sut.TestMethod("UsrSoapProbe", "UsrOp", null, userConfirmed: true);

		// Assert
		result.ResponseBody.Should().Be("<soap:Fault><soap:Reason>bad password [masked] for [masked]</soap:Reason></soap:Fault>",
			because: "both setting values are found in the envelope and masked in the escaped and plain forms the fault uses");
		result.SentSecretsMasked.Should().BeTrue(because: "the result says that something was masked");
		result.StatusCode.Should().Be(500, because: "the status code is returned as the platform reported it");
	}

	[Test]
	[Description("The parsed response values are returned with sent secrets masked, and withheld together with the body when a secret cannot be located.")]
	public void TestMethod_Should_Return_Masked_Response_Values_And_Withhold_Them_With_The_Body() {
		// Arrange
		GivenRegisteredService(SoapServiceFixture.Schema(), "UsrSoapProbe");
		const string values = "{\"UsrResult\":\"echo inner-key-77\",\"UsrCount\":5,\"UsrMissing\":null}";
		_client.CallMethod(Arg.Any<string>(), "UsrOp", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(
				new ServiceMethodCallResult(true, 200, "<ok/>", null, SoapServiceFixture.SentRaw, SoapServiceFixture.SentEnvelope, values),
				new ServiceMethodCallResult(true, 200, "<ok/>", null, SoapServiceFixture.SentRaw, "not xml", values));

		// Act
		WebServiceTestResult located = _sut.TestMethod("UsrSoapProbe", "UsrOp", null, userConfirmed: true);
		WebServiceTestResult withheld = _sut.TestMethod("UsrSoapProbe", "UsrOp", null, userConfirmed: true);

		// Assert
		located.ResponseValues.Should().NotBeNull(because: "the platform parsed the response parameters");
		JsonElement parsed = located.ResponseValues!.Value;
		parsed.GetProperty("UsrResult").GetString().Should().Be("echo [masked]", because: "a parsed value is masked like the body");
		parsed.GetProperty("UsrCount").GetInt32().Should().Be(5, because: "a value without a secret stays readable and typed");
		parsed.GetProperty("UsrMissing").ValueKind.Should().Be(JsonValueKind.Null,
			because: "a null tells the agent the response path read nothing");
		located.SentSecretsMasked.Should().BeTrue(because: "a parsed value carried a secret");
		withheld.ResponseValues.Should().BeNull(because: "values come from the same response that is withheld");
		withheld.ResponseBody.Should().BeNull(because: "the sent value could not be located");
	}

	[Test]
	[Description("A JSONPath on a response parameter of an XML response is named in a notice by describe and by the test call, since it reads nothing.")]
	public void DescribeAndTest_Should_Notice_A_JsonPath_On_An_Xml_Response() {
		// Arrange
		ServiceSchemaDto schema = SoapServiceFixture.Schema();
		schema.Methods.Items.Values.Single().Response.Parameters.Items.Values.Single().Path = "$.UsrOpResult";
		GivenRegisteredService(schema, "UsrSoapProbe");
		_client.CallMethod(Arg.Any<string>(), "UsrOp", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, "<UsrOpResult>5</UsrOpResult>", null, SoapServiceFixture.SentRaw,
				SoapServiceFixture.SentEnvelope, "{\"UsrResult\":null}"));

		// Act
		WebServiceToolResult described = _sut.Describe("UsrSoapProbe", false);
		WebServiceTestResult tested = _sut.TestMethod("UsrSoapProbe", "UsrOp", null, userConfirmed: true);

		// Assert
		string notice = described.Notices.Should().ContainSingle(because: "one response path does not fit its XML response")
			.Subject;
		notice.Should().Contain("UsrResult", because: "the notice names the parameter");
		notice.Should().Contain("$.UsrOpResult", because: "the notice quotes the path that reads nothing");
		notice.Should().Contain("element path", because: "the notice names the path dialect the platform reads");
		notice.Should().Contain("modify-web-service setMethod", because: "a SOAP service's path is changed through modify-web-service");
		tested.Notices.Should().ContainSingle(because: "the null in response-values must not read like a failure of the call")
			.Which.Should().Contain("UsrResult", because: "the notice names the parameter that read nothing");
	}

	[Test]
	[Description("A JSONPath on a JSON response gets no notice.")]
	public void Describe_Should_Not_Notice_A_JsonPath_On_A_Json_Response() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", ResponseParameters: [
			new WebServiceParameterArg("UsrStatus", Path: "$.status")])));

		// Act
		WebServiceToolResult described = _sut.Describe("BingSearchApiCall", false);

		// Assert
		described.Notices.Should().BeNull(because: "a JSON response is read by JSONPath");
	}

	[Test]
	[Description("Parsed response values longer than the response-body limit are not returned and the result says so.")]
	public void TestMethod_Should_Omit_Response_Values_Over_The_Limit() {
		// Arrange
		GivenRegisteredService(SoapServiceFixture.Schema(), "UsrSoapProbe");
		string values = JsonSerializer.Serialize(new Dictionary<string, string> {
			["UsrResult"] = new string('x', Clio.Command.WebServiceDesigner.WebServiceDesigner.MaxResponseBodyLength)
		});
		_client.CallMethod(Arg.Any<string>(), "UsrOp", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, "<ok/>", null, SoapServiceFixture.SentRaw,
				SoapServiceFixture.SentEnvelope, values));

		// Act
		WebServiceTestResult result = _sut.TestMethod("UsrSoapProbe", "UsrOp", null, userConfirmed: true);

		// Assert
		result.ResponseValues.Should().BeNull(because: "a JSON tree past the limit cannot be cut like text");
		result.ResponseValuesOmitted.Should().BeTrue(because: "the agent learns the values exist but were too long");
		result.ResponseBody.Should().Be("<ok/>", because: "the body itself is still returned");
	}

	[Test]
	[Description("A REST xml method the platform refuses with Rest:Xml gets an error that names the RequestBody route.")]
	public void TestMethod_Should_Explain_The_Rest_Xml_Failure() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "POST", "search", ContentType: "xml")));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, null, null, "Rest:Xml"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null, userConfirmed: true);

		// Assert
		result.Success.Should().BeFalse(because: "the platform sent nothing");
		result.Error.Should().StartWith("Rest:Xml", because: "the platform's own message is kept");
		result.Error.Should().Contain("RequestBody", because: "the agent learns the only route that calls such a method");
	}

	[Test]
	[Description("The Rest:Xml explanation is returned, not withheld, when the method uses authentication and a setting-bound parameter: nothing was sent.")]
	public void TestMethod_Should_Explain_The_Rest_Xml_Failure_With_Auth_And_Settings() {
		// Arrange
		ServiceSchemaDto schema = SchemaWithMethod(new WebServiceMethodArg("Search", "POST", "search", ContentType: "xml",
			UseAuth: true, RequestParameters: [new WebServiceParameterArg("UsrKey", "header", Path: "X-Key",
				DefaultSysSetting: "UsrBingKey")]));
		schema.AuthInfo = new ServiceAuthInfoDto { AuthType = ServiceSchemaCodes.AuthBasic };
		GivenRegisteredService(schema);
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, null, null, "Rest:Xml"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null, userConfirmed: true);

		// Assert
		result.Error.Should().Contain("RequestBody", because: "with nothing sent there is no secret to locate");
		result.Error.Should().NotContain("withheld", because: "the fail-closed path is for responses to sent requests");
	}

	[Test]
	[Description("A failure with nothing reported as sent is withheld when a response default comes from a system setting, since the error can quote it.")]
	public void TestMethod_Should_Withhold_An_Error_That_Can_Quote_A_Response_Default() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", ResponseParameters: [
			new WebServiceParameterArg("UsrCount", "header", Type: "Integer", Path: "X-Absent", DefaultSysSetting: "UsrApiSecret")])));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, null, null, "The input string 'live-setting-value' was not in a correct format."));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.Error.Should().NotContain("live-setting-value", because: "the setting value was never sent, so nothing masks it");
		result.Error.Should().Contain("withheld", because: "the agent learns why the error is not shown");
	}

	[Test]
	[Description("A response parameter whose default comes from a system setting is masked in the parsed values, even when nothing sent carried it.")]
	public void TestMethod_Should_Mask_A_Response_Value_Filled_From_A_Setting_Default() {
		// Arrange
		GivenRegisteredService(SchemaWithMethod(new WebServiceMethodArg("Search", "GET", "search", ResponseParameters: [
			new WebServiceParameterArg("UsrToken", "header", Path: "X-Absent", DefaultSysSetting: "UsrApiSecret")])));
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(false, 401, "{}", null,
				"GET https://api.bing.example/search HTTPS/1.1\r\nHost: api.bing.example", null,
				"{\"UsrToken\":\"live-setting-value\"}"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseValues!.Value.GetProperty("UsrToken").GetString().Should().Be("[masked]",
			because: "the platform fills the setting's value when the API does not send the header");
		result.SentSecretsMasked.Should().BeTrue(because: "the result says that something was masked");
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
		unrelated.Notices.Should().NotContain(n => n.Contains("fills the values") && n.Contains("UsrDeletedKey"),
			because: "the user is not told to fill a setting that does not exist");
		adding.Should().Throw<ArgumentException>(because: "a new reference to a missing setting would be dropped at run time")
			.WithMessage("*UsrNewKey*", because: "the refusal names the setting");
	}

	[Test]
	[Description("When the SchemaNamePrefix setting cannot be read, register refuses instead of saving without the prefix rule.")]
	public void Register_Should_Refuse_When_The_Prefix_Cannot_Be_Read() {
		// Arrange
		_prefix.Read().Returns(_ => throw new InvalidOperationException("The SchemaNamePrefix system setting could not be read"));

		// Act
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrErp", null, "https://erp.example/", null, null,
			[new WebServiceMethodArg("GetOrders", "GET", "orders")]));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an unread prefix must not switch the rule off")
			.WithMessage("*SchemaNamePrefix*", because: "the message names what failed");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	private void GivenSettingsExist() =>
		_client.FindExistingSysSettingCodes(Arg.Any<IReadOnlyCollection<string>>())
			.Returns(ci => ci.Arg<IReadOnlyCollection<string>>().ToList());

	private void DidNotScanUsages() =>
		_client.DidNotReceive().Select("SysSchema", Arg.Any<IReadOnlyList<string>>(),
			Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>());

	// The Bing service with its Search method sending the service authentication (use-auth true).
	private static ServiceSchemaDto BingSchemaSendingAuth(bool withBasicAuth) {
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Values.Single().UseAuthInfo = true;
		if (withBasicAuth) {
			schema.AuthInfo = WebServiceDefinitionBuilder.BuildAuth(
				new WebServiceAuthArg("basic", "UsrBingUser", "UsrBingPassword"), null, new WebServiceDefinitionBuilder.Errors());
		}
		return schema;
	}

	private static WebServiceOperationArg SetBasicAuth() =>
		new("setAuth", Auth: new WebServiceAuthArg("basic", "UsrBingUser", "UsrBingPassword"));

	private static IEnumerable<TestCaseData> EveryCallChanges() {
		yield return new TestCaseData(new WebServiceOperationArg("setBaseUri", BaseUri: "https://api.other.example/"),
			"base URI").SetArgDisplayNames("setBaseUri");
		yield return new TestCaseData(SetBasicAuth(), "authentication").SetArgDisplayNames("setAuth");
	}

	[TestCaseSource(nameof(EveryCallChanges))]
	[Description("A new base URI, or new authentication of a method that sends it, changes every call a process makes, so it is refused, naming the process and what changes, until the user agrees.")]
	public void Modify_Should_Refuse_An_Every_Call_Change_Of_A_Called_Service(WebServiceOperationArg operation,
		string what) {
		// Arrange
		GivenRegisteredService(BingSchemaSendingAuth(withBasicAuth: false));
		GivenSelect("SysSchema", ProcessRow("BingSearchProcess", "Search"));
		GivenSettingsExist();

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [operation], null);

		// Assert
		result.Success.Should().BeFalse(because: $"a new {what} changes every call BingSearchProcess makes");
		result.Error.Should().Contain($"the new {what} changes every call",
			because: "the refusal says what changes, so the user can decide");
		result.Error.Should().NotContain("lose their parameters",
			because: "the element keeps its parameters; only the call changes");
		result.Usages.Should().ContainSingle(u => u.Process == "BingSearchProcess",
			because: "the refusal names the process to show the user");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[TestCaseSource(nameof(EveryCallChanges))]
	[Description("The same base URI or authentication change saves when the user acknowledged the named process.")]
	public void Modify_Should_Save_An_Every_Call_Change_When_Acknowledged(WebServiceOperationArg operation, string what) {
		// Arrange
		GivenRegisteredService(BingSchemaSendingAuth(withBasicAuth: false));
		GivenSelect("SysSchema", ProcessRow("BingSearchProcess", "Search"));
		GivenSettingsExist();

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [operation], ["BingSearchProcess"]);

		// Assert
		result.Success.Should().BeTrue(because: $"the user agreed that the {what} of BingSearchProcess's calls changes");
		result.Usages.Should().ContainSingle(u => u.Process == "BingSearchProcess",
			because: "the saved result still lists the affected process");
		_client.Received(1).SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("Removing the authentication a called method sends is a change of every call it makes.")]
	public void Modify_Should_Refuse_Removing_The_Authentication_Of_A_Called_Service() {
		// Arrange
		GivenRegisteredService(BingSchemaSendingAuth(withBasicAuth: true));
		GivenSelect("SysSchema", ProcessRow("BingSearchProcess", "Search"));

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("setAuth", Auth: new WebServiceAuthArg("none"))], null);

		// Assert
		result.Success.Should().BeFalse(because: "every call of BingSearchProcess would stop sending its credentials");
		result.Error.Should().Contain("the new authentication changes every call", because: "the refusal names what changes");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("New authentication of a service whose called method does not send it (use-auth false) changes no call, so it saves without looking for callers and a notice names that method.")]
	public void Modify_Should_Save_New_Authentication_When_No_Method_Sends_It() {
		// Arrange
		ServiceSchemaDto schema = BingSchema();
		GivenRegisteredService(schema);
		GivenSelect("SysSchema", ProcessRow("BingSearchProcess", "Search"));
		GivenSettingsExist();

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [SetBasicAuth()], null);

		// Assert
		result.Success.Should().BeTrue(because: "Search sends no authentication, so BingSearchProcess's calls stay the same");
		DidNotScanUsages();
		result.Notices.Should().Contain(n => n.Contains("Method 'Search' has use-auth false")
				&& n.Contains("Ask the user whether it should be; do not change use-auth yourself"),
			because: "the user must hear that the new authentication does not reach Search (measured) and decide it");
	}

	[Test]
	[Description("The base URI and authentication the service already has, sent back as describe returns them, change nothing and are not looked up in processes; no use-auth notice either.")]
	public void Modify_Should_Not_Scan_Usages_When_Base_Uri_And_Auth_Are_Unchanged() {
		// Arrange
		GivenRegisteredService(BingSchemaSendingAuth(withBasicAuth: true));
		GivenSettingsExist();

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [
			new WebServiceOperationArg("setBaseUri", BaseUri: "https://api.bing.example/"), SetBasicAuth()
		], null);

		// Assert
		result.Success.Should().BeTrue(because: "the same values change no call");
		DidNotScanUsages();
		(result.Notices ?? []).Should().NotContain(n => n.Contains("use-auth false"),
			because: "an unchanged authentication needs no notice");
		_client.Received(1).SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("A base URI changed and changed back in one call is no change: the guard compares the service before and after the whole call.")]
	public void Modify_Should_Not_Scan_Usages_When_A_Value_Is_Changed_And_Changed_Back() {
		// Arrange
		GivenRegisteredService(BingSchema());

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [
			new WebServiceOperationArg("setBaseUri", BaseUri: "https://api.other.example/"),
			new WebServiceOperationArg("setBaseUri", BaseUri: "https://api.bing.example/")
		], null);

		// Assert
		result.Success.Should().BeTrue(because: "the stored base URI ends as it was");
		DidNotScanUsages();
	}

	[Test]
	[Description("A new retry count changes no request the API receives, so it saves without looking for callers.")]
	public void Modify_Should_Not_Scan_Usages_For_A_New_Retry_Count() {
		// Arrange
		GivenRegisteredService(BingSchema());

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("setRetryCount", RetryCount: 3)], null);

		// Assert
		result.Success.Should().BeTrue(because: "retries change no request, so no caller has to agree");
		DidNotScanUsages();
		_client.Received(1).SaveSchema(Arg.Is<ServiceSchemaDto>(s => s.RetryCount == 3));
	}

	[Test]
	[Description("A base URI and authentication changed together are named together in the refusal.")]
	public void Modify_Should_Name_Every_Change_Together() {
		// Arrange
		GivenRegisteredService(BingSchemaSendingAuth(withBasicAuth: false));
		GivenSelect("SysSchema", ProcessRow("BingSearchProcess", "Search"));
		GivenSettingsExist();

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [
			new WebServiceOperationArg("setBaseUri", BaseUri: "https://api.other.example/"), SetBasicAuth()
		], null);

		// Assert
		result.Error.Should().Contain("the new base URI and authentication change every call",
			because: "the user must hear both changes before agreeing");
	}

	[Test]
	[Description("A call that changes a called method and the base URI names both: the method change and that every call goes elsewhere.")]
	public void Modify_Should_Name_The_Base_Uri_Change_Also_For_A_Changed_Method() {
		// Arrange
		GivenRegisteredService(BingSchema());
		GivenSelect("SysSchema", ProcessRow("BingSearchProcess", "Search"));

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [
			new WebServiceOperationArg("removeMethod", MethodCode: "Search"),
			new WebServiceOperationArg("setBaseUri", BaseUri: "https://api.other.example/")
		], null);

		// Assert
		result.Success.Should().BeFalse(because: "the process calls the removed method");
		result.Error.Should().Contain("lose their parameters", because: "the removed method breaks the caller");
		result.Error.Should().Contain("the new base URI changes every call",
			because: "the user must also hear that the calls go to another address before agreeing");
	}

	[Test]
	[Description("After setAuth none, no method is named: with no authentication there is nothing a method could fail to send.")]
	public void Modify_Should_Not_Name_Methods_When_SetAuth_Removes_The_Authentication() {
		// Arrange
		GivenRegisteredService(BingSchemaSendingAuth(withBasicAuth: true));
		GivenSelect("SysSchema");

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("setAuth", Auth: new WebServiceAuthArg("none"))], null);

		// Assert
		result.Success.Should().BeTrue(because: "no process calls the service");
		(result.Notices ?? []).Should().NotContain(n => n.Contains("use-auth false"),
			because: "a service without authentication sends none from any method");
	}

	[Test]
	[Description("A method this call defines with an explicit use-auth false is not named in the notice: that was the agent's choice.")]
	public void Modify_Should_Not_Name_A_Method_Defined_With_Explicit_Use_Auth() {
		// Arrange
		GivenRegisteredService(BingSchemaSendingAuth(withBasicAuth: false));
		GivenSelect("SysSchema");
		GivenSettingsExist();

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [
			SetBasicAuth(),
			new WebServiceOperationArg("addMethod", new WebServiceMethodArg("UsrHealth", "GET", "health", UseAuth: false))
		], null);

		// Assert
		result.Success.Should().BeTrue(because: "no process calls the service");
		(result.Notices ?? []).Should().NotContain(n => n.Contains("use-auth false"),
			because: "UsrHealth was defined without authentication on purpose, and Search sends it");
	}

	[Test]
	[Description("A method sent back with the use-auth it already has, as describe-web-service prints it, is still named in the notice after new authentication: keeping the stored value is not a choice.")]
	public void Modify_Should_Name_A_Method_Sent_Back_With_Its_Stored_Use_Auth() {
		// Arrange
		GivenRegisteredService(BingSchema());
		GivenSelect("SysSchema");
		GivenSettingsExist();
		var search = new WebServiceMethodArg("Search", "GET", "search",
			RequestParameters: [new WebServiceParameterArg("q", "query")], UseAuth: false);

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[SetBasicAuth(), new WebServiceOperationArg("setMethod", search)], null);

		// Assert
		result.Success.Should().BeTrue(because: "no process calls the service");
		result.Notices.Should().Contain(n => n.Contains("Method 'Search' has use-auth false"),
			because: "Search keeps the use-auth false it had, so the new authentication does not reach it");
	}

	[Test]
	[Description("A method removed and added again in one call with use-auth false is the agent's choice, so the notice after new authentication leaves it out.")]
	public void Modify_Should_Treat_A_Readded_Method_Use_Auth_As_Explicit() {
		// Arrange
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Values.Single().Name = "UsrSearch";
		GivenRegisteredService(schema);
		GivenSelect("SysSchema");
		GivenSettingsExist();
		var search = new WebServiceMethodArg("UsrSearch", "GET", "search",
			RequestParameters: [new WebServiceParameterArg("UsrQ", "query", Path: "q")], UseAuth: false);

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [
			SetBasicAuth(),
			new WebServiceOperationArg("removeMethod", MethodCode: "UsrSearch"),
			new WebServiceOperationArg("addMethod", search)
		], null);

		// Assert
		result.Success.Should().BeTrue(because: $"no process calls the service; error: {result.Error}");
		(result.Notices ?? []).Should().NotContain(n => n.Contains("use-auth false"),
			because: "addMethod defines the method anew, so its use-auth is a choice the agent made");
	}

	[Test]
	[Description("A setMethod that changes a method's use-auth to false together with new authentication is the agent's choice, so the notice leaves that method out.")]
	public void Modify_Should_Treat_A_Changed_Use_Auth_As_Explicit() {
		// Arrange
		GivenRegisteredService(BingSchemaSendingAuth(withBasicAuth: false));
		GivenSelect("SysSchema");
		GivenSettingsExist();
		var search = new WebServiceMethodArg("Search", "GET", "search",
			RequestParameters: [new WebServiceParameterArg("q", "query")], UseAuth: false);

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall",
			[SetBasicAuth(), new WebServiceOperationArg("setMethod", search)], null);

		// Assert
		result.Success.Should().BeTrue(because: $"no process calls the service; error: {result.Error}");
		(result.Notices ?? []).Should().NotContain(n => n.Contains("use-auth false"),
			because: "Search was stored with use-auth true, so false here is a choice the agent made");
	}

	[Test]
	[Description("describe-web-service masks the value of a credential-like query parameter in a stored base URI and method URI, and keeps the other query values.")]
	public void Describe_Should_Mask_A_Secret_Query_Value_Of_A_Stored_Uri() {
		// Arrange
		ServiceSchemaDto schema = BingSchema();
		schema.BaseUri = "https://api.bing.example/?sig=sig-0042-secret-key";
		schema.Methods.Items.Values.Single().Request.Uri = "search?api_key=sk-live-0042&format=json";
		GivenRegisteredService(schema);

		// Act
		WebServiceToolResult result = _sut.Describe("BingSearchApiCall", false);

		// Assert
		string view = JsonSerializer.Serialize(result.Service);
		view.Should().NotContain("sk-live-0042", because: "a key typed into a method URI in Studio is a credential");
		view.Should().NotContain("sig-0042-secret-key", because: "an Azure SAS signature in the base URI is a credential");
		result.Service.Methods.Single().Uri.Should().Be("search?api_key=[masked]&format=json",
			because: "only the secret value is hidden; the reader still sees which query parameters go out");
		result.Service.BaseUri.Should().Be("https://api.bing.example/?sig=[masked]",
			because: "the base URI is masked the same way");
	}

	[Test]
	[Description("list-web-services masks a credential-like query value of a stored base URI.")]
	public void BaseUriFromMetadata_Should_Mask_A_Secret_Query_Value() {
		// Arrange
		string metadata = """{"MetaData":{"Schema":{"IQ1":"https://erp.example/Orders.svc?code=fn-key-0042-0042-0042"}}}""";

		// Act
		string baseUri = Clio.Command.WebServiceDesigner.WebServiceDesigner.BaseUriFromMetadata(metadata);

		// Assert
		baseUri.Should().Be("https://erp.example/Orders.svc?code=[masked]",
			because: "an Azure Functions key in the base URI must not reach the list either");
	}

	[Test]
	[Description("A method sent back from describe-web-service with a key kept in its URI (shown as the mask) is refused without the key and nothing is saved: the tools never write such a key.")]
	public void Modify_Should_Refuse_A_Hidden_Query_Key_Sent_Back_From_Describe() {
		// Arrange
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Values.Single().Request.Uri = "search?sig=sig-0042-secret-key&format=json";
		GivenRegisteredService(schema);
		WebServiceMethodArg described = JsonSerializer.Deserialize<WebServiceMethodArg>(
			JsonSerializer.Serialize(WebServiceSchemaView.From(schema, "Custom").Methods.Single()));

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setMethod", described)], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the mask would be stored and sent as the key")
			.Where(e => !e.Message.Contains("sig-0042-secret-key"), "the refusal never shows the hidden key")
			.WithMessage("*cannot write that key back*", because: "the refusal says what the agent can do instead");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("A changed method URI that still carries the mask placeholder is refused and nothing is saved.")]
	public void Modify_Should_Refuse_The_Mask_Placeholder_In_A_Changed_Uri() {
		// Arrange
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Values.Single().Request.Uri = "search?sig=sig-0042-secret-key";
		GivenRegisteredService(schema);
		GivenSelect("SysSchema");
		var changed = new WebServiceMethodArg("Search", "GET", "find?sig=[masked]",
			RequestParameters: [new WebServiceParameterArg("q", "query")]);

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setMethod", changed)], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the placeholder would be stored and sent as the key")
			.WithMessage("*which describe-web-service shows for a key kept in that URI*",
				because: "the refusal names the mask");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[TestCase("search?code=fn-key-0042-0042-0042", false)]
	[TestCase("search?appid=0123456789abcdef0123", false)]
	[TestCase("search?format=json;api_key=sk-live-0042", false)]
	[TestCase("search?api_key=sk{x}", false)]
	[TestCase("search?ret=/cb?api_key=sk-live-0042", false)]
	[TestCase("search?a=1?api_key=sk-live-0042", false)]
	[TestCase("search?sign=0123456789abcdef", false)]
	[TestCase("search?api_key={key}", true)]
	[TestCase("rates?code=USD", true)]
	[TestCase("rates?code=USD;callback=handleResponse", true)]
	[TestCase("odata/Orders(OrderKey={key})/Items", true)]
	[Description("register refuses a key kept in a method URI by the same rule describe masks one with, ';' separators and partial placeholders included, and keeps an ordinary short value or a whole placeholder.")]
	public void Register_Should_Refuse_A_Key_In_A_Method_Uri(string uri, bool accepted) {
		// Arrange
		_client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, "UsrErp").Returns(true);
		GivenSelect("SysPackage", new JsonObject { ["UId"] = PackageUId.ToString() });
		GivenSettingsExist();
		ServiceSchemaDto saved = null;
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => saved = call.Arg<ServiceSchemaDto>());
		_client.ReadSchema(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(_ => saved);
		WebServiceParameterArg[] parameters = uri.Contains("{key}")
			? [new WebServiceParameterArg("UsrKey", "path", Path: "key", DefaultSysSetting: "UsrErpKey")]
			: [];

		// Act
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrErp", null, "https://erp.example/", null, null,
			[new WebServiceMethodArg("UsrSearch", "GET", uri, RequestParameters: parameters)]));

		// Assert
		if (accepted) {
			act.Should().NotThrow(because: "an ordinary short value or a placeholder filled per call is no stored key");
		} else {
			act.Should().Throw<ArgumentException>(because: "a key in a method URI would be a stored constant")
				.Where(e => !e.Message.Contains("0042") && !e.Message.Contains("0123456789"),
					"the refusal never echoes the value")
				.WithMessage("*looks like a credential*", because: "the refusal points at default-sys-setting");
		}
	}

	[TestCase("search?ret=/cb?sig=[masked]")]
	[TestCase("search?x=1?sig=%5Bmasked%5D")]
	[Description("A method URI that carries the mask anywhere in a pair, after a second '?' or URL-escaped, is refused like the mask itself, so the mask is never stored in place of a key.")]
	public void Modify_Should_Refuse_The_Mask_Inside_A_Query_Pair(string uri) {
		// Arrange
		GivenRegisteredService(BingSchema());
		var changed = new WebServiceMethodArg("Search", "GET", uri, RequestParameters: [new WebServiceParameterArg("q", "query")]);

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setMethod", changed)], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the mask would be sent as the key")
			.WithMessage("*query 'sig' holds*", because: "the refusal names the key before the mask");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[TestCase("appid", "0123456789abcdef0123", false)]
	[TestCase("code", "USD", true)]
	[Description("A query parameter's constant default follows the URI key rule: a key-length value under a hosting key name is refused, a short one is kept.")]
	public void Register_Should_Apply_The_Query_Key_Rule_To_A_Query_Parameter_Default(string path, string value, bool accepted) {
		// Arrange
		_client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, "UsrErp").Returns(true);
		GivenSelect("SysPackage", new JsonObject { ["UId"] = PackageUId.ToString() });
		ServiceSchemaDto saved = null;
		_client.When(c => c.SaveSchema(Arg.Any<ServiceSchemaDto>())).Do(call => saved = call.Arg<ServiceSchemaDto>());
		_client.ReadSchema(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(_ => saved);

		// Act
		Action act = () => _sut.Register("Custom", new WebServiceRegistration("UsrErp", null, "https://erp.example/", null, null,
			[new WebServiceMethodArg("UsrWeather", "GET", "weather", RequestParameters: [
				new WebServiceParameterArg("UsrUnits", "query", Path: path, DefaultValue: value)
			])]));

		// Assert
		if (accepted) {
			act.Should().NotThrow(because: "a short value under a hosting key name is ordinary");
		} else {
			act.Should().Throw<ArgumentException>(because: "an OpenWeatherMap key is a credential, sent in every URL")
				.Where(e => !e.Message.Contains(value), "the refusal never echoes the value")
				.WithMessage("*constant default is refused*", because: "the refusal points at default-sys-setting");
		}
	}

	[Test]
	[Description("A path parameter whose placeholder is a key's whole value in the method URI carries that key: a new secret constant on it is refused, and one stored is hidden by describe and kept when describe output comes back.")]
	public void Modify_Should_Treat_A_Path_Parameter_That_Fills_A_Key_Like_The_Key() {
		// Arrange
		ServiceSchemaDto schema = BingSchema();
		ServiceMethodDto stored = schema.Methods.Items.Values.Single();
		stored.Request.Uri = "search?api_key={k}";
		var filler = new ServiceParameterDto {
			UId = Guid.NewGuid(), Name = "k", Path = "k", Type = ServiceSchemaCodes.Placements["path"],
			DataValueTypeName = "Text", Caption = new() { ["en-US"] = "k" },
			DefValue = new ServiceParameterValueDto { Value = "sk-live-0042", Source = ServiceSchemaCodes.ValueSourceConst }
		};
		stored.Request.Parameters.Items[filler.UId.ToString()] = filler;
		GivenRegisteredService(schema);
		WebServiceView view = WebServiceSchemaView.From(schema, "Custom");
		WebServiceMethodArg described = JsonSerializer.Deserialize<WebServiceMethodArg>(JsonSerializer.Serialize(view.Methods.Single()));
		var literal = new WebServiceMethodArg("Search", "GET", "search?api_key={k}", RequestParameters: [
			new WebServiceParameterArg("q", "query"), new WebServiceParameterArg("k", "path", Path: "k", DefaultValue: "sk-live-0042")
		]);

		// Act
		Action echo = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setMethod", described)], null);
		Action write = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setMethod", literal)], null);

		// Assert
		JsonSerializer.Serialize(view).Should().NotContain("sk-live-0042", because: "the constant is the key of the URI");
		echo.Should().NotThrow(because: "the hidden constant is kept, as other hidden constants are");
		_client.Received(1).SaveSchema(Arg.Is<ServiceSchemaDto>(s => s.Methods.Items.Values.Single().Request.Parameters
			.Items.Values.Single(p => p.Name == "k").DefValue.Value == "sk-live-0042"));
		write.Should().Throw<ArgumentException>(because: "a constant there is a key written into the URL")
			.Where(e => !e.Message.Contains("sk-live-0042"), "the refusal never echoes the value")
			.WithMessage("*fills a key in the uri query and has a constant default*", because: "the refusal names the parameter");
	}

	[TestCase("https://erp.example/svc;jsessionid=0123456789abcdef-token")]
	[TestCase("https://erp.example/svc;token=SECRET-42")]
	[Description("setBaseUri refuses a key in a ;name=value path parameter of the base URI, which describe would hide.")]
	public void Modify_Should_Refuse_A_Key_In_The_Base_Uri_Path(string baseUri) {
		// Arrange
		GivenRegisteredService(BingSchema());

		// Act
		Action act = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setBaseUri", BaseUri: baseUri)], null);

		// Assert
		act.Should().Throw<ArgumentException>(because: "a key in the base URI would be a stored constant")
			.Where(e => !e.Message.Contains("SECRET-42") && !e.Message.Contains("0123456789"), "the value is never echoed")
			.WithMessage("*base-uri query*looks like a credential*", because: "the refusal names the key");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	[Test]
	[Description("A test call masks a secret query value of the stored method URI when the API echoes it.")]
	public void TestMethod_Should_Mask_An_Echoed_Secret_Query_Value_Of_The_Stored_Uri() {
		// Arrange
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Values.Single().Request.Uri = "search?sig=sig-0042-secret-key";
		GivenRegisteredService(schema);
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200,
				"""{"url":"https://api.bing.example/search?sig=sig-0042-secret-key"}""", null,
				"GET https://api.bing.example/search?sig=sig-0042-secret-key HTTPS/1.1\r\nHost: api.bing.example"));

		// Act
		WebServiceTestResult result = _sut.TestMethod("BingSearchApiCall", "Search", null);

		// Assert
		result.ResponseBody.Should().NotContain("sig-0042-secret-key", because: "describe hides the value, so an echo must too");
		result.SentSecretsMasked.Should().BeTrue(because: "the result says that something was masked");
	}

	// The Bing service whose Search method fills api_key from path parameter k, with a constant describe hides.
	private ServiceSchemaDto GivenKeyPlaceholderService() {
		ServiceSchemaDto schema = BingSchema();
		ServiceMethodDto stored = schema.Methods.Items.Values.Single();
		stored.Request.Uri = "search?api_key={k}";
		var filler = new ServiceParameterDto {
			UId = Guid.NewGuid(), Name = "k", Path = "k", Type = ServiceSchemaCodes.Placements["path"],
			DataValueTypeName = "Text", Caption = new() { ["en-US"] = "k" },
			DefValue = new ServiceParameterValueDto { Value = "sk-live-0042", Source = ServiceSchemaCodes.ValueSourceConst }
		};
		stored.Request.Parameters.Items[filler.UId.ToString()] = filler;
		GivenRegisteredService(schema);
		return schema;
	}

	[Test]
	[Description("default-value \"\" clears the hidden constant of a path parameter that fills a key.")]
	public void Modify_Should_Clear_A_Hidden_Key_Placeholder_Constant() {
		// Arrange
		GivenKeyPlaceholderService();
		var clear = new WebServiceMethodArg("Search", "GET", "search?api_key={k}", RequestParameters: [
			new WebServiceParameterArg("q", "query"), new WebServiceParameterArg("k", "path", Path: "k", DefaultValue: "")
		]);

		// Act
		WebServiceToolResult result = _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setMethod", clear)], null);

		// Assert
		result.Success.Should().BeTrue(because: "an explicit empty default clears the constant, as the refusals offer");
		_client.Received(1).SaveSchema(Arg.Is<ServiceSchemaDto>(s => string.IsNullOrEmpty(s.Methods.Items.Values.Single()
			.Request.Parameters.Items.Values.Single(p => p.Name == "k").DefValue.Value)));
	}

	[Test]
	[Description("A new uri that would show the kept hidden constant of a path parameter is refused, not saved.")]
	public void Modify_Should_Refuse_A_Uri_That_Would_Show_A_Hidden_Key_Placeholder_Constant() {
		// Arrange
		GivenKeyPlaceholderService();
		var shown = new WebServiceMethodArg("Search", "GET", "search/{k}", RequestParameters: [
			new WebServiceParameterArg("q", "query"), new WebServiceParameterArg("k", "path", Path: "k")
		]);

		// Act
		Action moved = () => _sut.Modify("BingSearchApiCall", [new WebServiceOperationArg("setMethod", shown)], null);

		// Assert
		moved.Should().Throw<ArgumentException>(because: "the kept constant would be a visible path segment")
			.Where(e => !e.Message.Contains("sk-live-0042"), "the refusal never echoes the value")
			.WithMessage("*would show in the new uri*", because: "the refusal says why and how to clear it");
		_client.DidNotReceive().SaveSchema(Arg.Any<ServiceSchemaDto>());
	}

	// A REST service with count methods UsrM00..UsrMnn, each with six query parameters and two response parameters.
	private static ServiceSchemaDto ManyMethodsSchema(int count) {
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Clear();
		var errors = new WebServiceDefinitionBuilder.Errors();
		for (int i = 0; i < count; i++) {
			ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg($"UsrM{i:00}", "GET",
				$"orders/m{i}",
				RequestParameters: [.. Enumerable.Range(0, 6).Select(p => new WebServiceParameterArg($"UsrQuery{p}", "query"))],
				ResponseParameters: [new WebServiceParameterArg("UsrStatus", Path: "$.status"),
					new WebServiceParameterArg("UsrTotal", Path: "$.total")]), null, false, errors);
			schema.Methods.Items[method.UId.ToString()] = method;
		}
		return schema;
	}

	[Test]
	[Description("A service too large to describe inline returns as many methods in full as fit, in stored order, and lists the rest by code with their parameter counts.")]
	public void Describe_Should_List_The_Methods_That_Do_Not_Fit_By_Code() {
		// Arrange
		GivenRegisteredService(ManyMethodsSchema(40));

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(_sut.Describe("BingSearchApiCall", false));

		// Assert
		result.Success.Should().BeTrue(because: $"the service exists; error: {result.Error}");
		McpResultSize.Of(result).Should().BeLessThanOrEqualTo(McpResultSize.InlineReplyBudgetBytes,
			because: "a reply over the budget is written to a file and read back in pieces");
		int shown = result.Service.Methods.Count;
		shown.Should().BeInRange(1, 39, because: "some methods fit and some do not");
		result.Service.Methods.Select(m => m.Code).Should().Equal(Enumerable.Range(0, shown).Select(i => $"UsrM{i:00}"),
			because: "the methods shown are the first ones in stored order");
		result.Service.MethodsNotShown.Should().HaveCount(40 - shown, because: "every other method is listed by code");
		result.Service.MethodsNotShown[0].Should().Be(new WebServiceMethodSummary($"UsrM{shown:00}", $"UsrM{shown:00}", 6, 2),
			because: "a method listed by code carries its parameter counts");
		result.Notices.Should().Contain(n => n.Contains("methods-not-shown") && n.Contains("describe-web-service methods")
				&& n.Contains("never send an entry of methods-not-shown back as a method"),
			because: "the agent is told how to read the rest, and not to send a code-only entry back");
	}

	[Test]
	[Description("The designer itself never cuts a service: read-wsdl with service-code reads every existing method through it.")]
	public void Describe_Should_Return_Every_Method_To_Internal_Readers() {
		// Arrange
		GivenRegisteredService(ManyMethodsSchema(40));

		// Act
		WebServiceToolResult result = _sut.Describe("BingSearchApiCall", false);

		// Assert
		result.Service.Methods.Should().HaveCount(40, because: "a method left out here would be invisible to read-wsdl's code matching");
		result.Service.MethodsNotShown.Should().BeNull(because: "only the MCP tool fits the reply it returns to the agent");
	}

	[Test]
	[Description("A single method larger than the reply budget is still returned in full, so describing it by code can always show it.")]
	public void Describe_Should_Keep_One_Method_In_Full_When_It_Alone_Passes_The_Budget() {
		// Arrange
		ServiceSchemaDto schema = BingSchema();
		schema.Methods.Items.Clear();
		var errors = new WebServiceDefinitionBuilder.Errors();
		ServiceMethodDto big = WebServiceDefinitionBuilder.BuildMethod(new WebServiceMethodArg("UsrBig", "GET", "big",
			RequestParameters: [.. Enumerable.Range(0, 120).Select(p => new WebServiceParameterArg($"UsrQueryParameter{p:000}",
				"query", Path: $"query_parameter_{p:000}"))]), null, false, errors);
		schema.Methods.Items[big.UId.ToString()] = big;
		GivenRegisteredService(schema);

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(_sut.Describe("BingSearchApiCall", false, ["UsrBig"]));

		// Assert
		McpResultSize.Of(result).Should().BeGreaterThan(McpResultSize.InlineReplyBudgetBytes,
			because: "the fixture must not fit the budget");
		result.Service.Methods.Select(m => m.Code).Should().Equal(["UsrBig"],
			because: "hiding the only method would send the agent round in a loop");
		result.Service.MethodsNotShown.Should().BeNull(because: "nothing was left out");
	}

	[Test]
	[Description("describe-web-service with methods returns only the named methods, matched ignoring case, in full.")]
	public void Describe_Should_Return_Only_The_Named_Methods() {
		// Arrange
		GivenRegisteredService(ManyMethodsSchema(40));

		// Act
		WebServiceToolResult result = _sut.Describe("BingSearchApiCall", false, ["UsrM37", "usrm05"]);

		// Assert
		result.Service.Methods.Select(m => m.Code).Should().Equal(["UsrM05", "UsrM37"],
			because: "only the named methods are returned, in stored order");
		result.Service.MethodsNotShown.Should().BeNull(because: "the narrowed reply fits whole");
		result.Notices.Should().BeNull(because: "nothing was left out");
	}

	[Test]
	[Description("describe-web-service refuses a method code the service does not have and names the codes it has.")]
	public void Describe_Should_Refuse_A_Method_The_Service_Does_Not_Have() {
		// Arrange
		GivenRegisteredService(ManyMethodsSchema(3));

		// Act
		Action act = () => _sut.Describe("BingSearchApiCall", false, ["UsrM01", "UsrNope"]);

		// Assert
		act.Should().Throw<ArgumentException>(because: "the method does not exist")
			.WithMessage("*'UsrNope' not found*UsrM00, UsrM01, UsrM02*", because: "the refusal names the codes the service has");
	}

	[Test]
	[Description("A change to a large service returns the methods the call built in full, whatever their place in stored order.")]
	public void Modify_Should_Keep_The_Built_Method_In_Full_When_The_Reply_Does_Not_Fit() {
		// Arrange
		GivenRegisteredService(ManyMethodsSchema(40));

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(_sut.Modify("BingSearchApiCall",
			[new WebServiceOperationArg("addMethod", new WebServiceMethodArg("UsrZzNew", "GET", "orders/new"))], null), ["UsrZzNew"]);

		// Assert
		result.Success.Should().BeTrue(because: $"an added method is saved; error: {result.Error}");
		result.Service.Methods.Should().Contain(m => m.Code == "UsrZzNew",
			because: "the method the call built is the one the agent checks, so it is kept in full");
		result.Service.MethodsNotShown.Should().NotBeEmpty(because: "the rest of the large service is listed by code");
		McpResultSize.Of(result).Should().BeLessThanOrEqualTo(McpResultSize.InlineReplyBudgetBytes,
			because: "the reply is returned inline");
	}

	[Test]
	[Description("A test call whose body would pass the reply budget returns the body cut to fit and says so.")]
	public void TestMethod_Should_Cut_The_Body_To_The_Reply_Budget() {
		// Arrange
		GivenRegisteredService(SoapServiceFixture.Schema(), "UsrSoapProbe");
		string body = "<r>" + new string('x', Clio.Command.WebServiceDesigner.WebServiceDesigner.MaxResponseBodyLength) + "</r>";
		_client.CallMethod(Arg.Any<string>(), "UsrOp", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, body, null, SoapServiceFixture.SentRaw,
				SoapServiceFixture.SentEnvelope, "{\"UsrResult\":\"ok\"}"));

		// Act
		WebServiceTestResult result = WebServiceReplyBudget.Fit(_sut.TestMethod("UsrSoapProbe", "UsrOp", null, userConfirmed: true));

		// Assert
		result.ResponseBodyTruncated.Should().BeTrue(because: "the body was cut");
		result.ResponseBody.Should().StartWith("<r>xxx", because: "the start of the body is kept");
		result.ResponseValues.Should().NotBeNull(because: "the small parsed values still fit beside the cut body");
		McpResultSize.Of(result).Should().BeLessThanOrEqualTo(McpResultSize.InlineReplyBudgetBytes,
			because: "the reply is returned inline");
	}

	[TestCase('\u0416')]
	[TestCase('\u4E2D')]
	[Description("A body of characters that take more than one byte is cut to what fits, not emptied.")]
	public void TestMethod_Should_Cut_A_Non_Ascii_Body_To_What_Fits(char character) {
		// Arrange
		GivenRegisteredService(SoapServiceFixture.Schema(), "UsrSoapProbe");
		_client.CallMethod(Arg.Any<string>(), "UsrOp", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, new string(character, 20_000), null, SoapServiceFixture.SentRaw,
				SoapServiceFixture.SentEnvelope, null));

		// Act
		WebServiceTestResult result = WebServiceReplyBudget.Fit(_sut.TestMethod("UsrSoapProbe", "UsrOp", null, userConfirmed: true));

		// Assert
		McpResultSize.Of(result).Should().BeLessThanOrEqualTo(McpResultSize.InlineReplyBudgetBytes,
			because: "the reply is returned inline");
		McpResultSize.Of(result).Should().BeGreaterThan(McpResultSize.InlineReplyBudgetBytes - 16,
			because: "the body is cut to the longest start that fits, not cut by characters for bytes");
	}

	[Test]
	[Description("A key the API echoes at the very point the reply is cut is masked first, so no part of it survives the cut.")]
	public void TestMethod_Should_Mask_An_Echoed_Key_Before_The_Reply_Is_Cut() {
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
		const string key = "sk-live-0042-abcdefghijklmnop";
		string body = "{\"lines\":[" + string.Join(",", Enumerable.Range(0, 700).Select(i => $"\"line {i:000} {key}\"")) + "]}";
		_client.CallMethod("BingSearchApiCall", "Search", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, body, null,
				$"GET https://api.bing.example/search HTTPS/1.1\r\nX-Api-Key: {key}\r\nHost: api.bing.example"));

		// Act
		WebServiceTestResult result = WebServiceReplyBudget.Fit(_sut.TestMethod("BingSearchApiCall", "Search", null));

		// Assert
		result.ResponseBodyTruncated.Should().BeTrue(because: "the echoed body is longer than the reply budget");
		result.ResponseBody.Should().NotContain(key[..4], because: "the key is masked before the body is cut, so no prefix of it is left");
		result.SentSecretsMasked.Should().BeTrue(because: "the result says that something was masked");
	}

	[Test]
	[Description("Parsed response values that alone leave no room for the body are left out, and the body is returned in full when it then fits.")]
	public void TestMethod_Should_Omit_Values_That_Leave_No_Room() {
		// Arrange
		GivenRegisteredService(SoapServiceFixture.Schema(), "UsrSoapProbe");
		string values = JsonSerializer.Serialize(new Dictionary<string, string> { ["UsrResult"] = new string('v', 19_000) });
		_client.CallMethod(Arg.Any<string>(), "UsrOp", Arg.Any<IReadOnlyList<ServiceCallParameter>>())
			.Returns(new ServiceMethodCallResult(true, 200, "<ok/>", null, SoapServiceFixture.SentRaw,
				SoapServiceFixture.SentEnvelope, values));

		// Act
		WebServiceTestResult result = WebServiceReplyBudget.Fit(_sut.TestMethod("UsrSoapProbe", "UsrOp", null, userConfirmed: true));

		// Assert
		result.ResponseValues.Should().BeNull(because: "the values alone do not fit the reply");
		result.ResponseValuesOmitted.Should().BeTrue(because: "the agent learns the values exist but were left out");
		result.ResponseBody.Should().Be("<ok/>", because: "the body fits once the values are left out");
		result.ResponseBodyTruncated.Should().BeFalse(because: "nothing of the body was cut");
	}

	[TestCase("erp", new[] { "UsrErpOrders", "UsrInvoices" })]
	[TestCase("ORDERS", new[] { "UsrErpOrders" })]
	[TestCase(" ", new[] { "UsrErpOrders", "UsrInvoices", "UsrWeather" })]
	[Description("list-web-services keeps the services whose code, caption or package contains the filter, ignoring case; a blank filter keeps all.")]
	public void List_Should_Filter_By_Code_Caption_Or_Package(string filter, string[] expected) {
		// Arrange
		JsonObject Row(string name, string caption, string package) => new() {
			["UId"] = Guid.NewGuid().ToString(), ["PackageUId"] = PackageUId.ToString(), ["Name"] = name,
			["Caption"] = caption, ["TypeName"] = "REST", ["SysPackageName"] = package
		};
		GivenSelect("VwWebServiceV2", Row("UsrErpOrders", "Orders", "Custom"), Row("UsrInvoices", "Invoices", "UsrErpLink"),
			Row("UsrWeather", "Weather", "Custom"));

		// Act
		IReadOnlyList<WebServiceListItem> listed = _sut.List(filter);

		// Assert
		listed.Select(s => s.Code).Should().Equal(expected, because: "the filter matches the code, caption or package");
	}
}
