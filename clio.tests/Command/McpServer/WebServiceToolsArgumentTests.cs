using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.WebServiceDesigner;
using Microsoft.AspNetCore.Http;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WebServiceToolsArgumentTests {

	private static Dictionary<string, JsonElement> Extra(string key) =>
		new() { [key] = JsonSerializer.SerializeToElement("UsrValue") };

	[TestCase("method-code", "method")]
	[TestCase("methodCode", "method")]
	[TestCase("service-code", "code")]
	[TestCase("Service-Code", "code")]
	[TestCase("confirmed", "user-confirmed")]
	[TestCase("userConfirmed", "user-confirmed")]
	[Description("test-web-service-method answers a guessed method, service or confirmation argument name with the canonical name, before touching any environment, so a confirmation under another name is never taken as the user's yes.")]
	public void TestWebServiceMethod_Should_Hint_The_Canonical_Argument(string guessed, string canonical) {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		var tool = new TestWebServiceMethodTool(resolver);
		var args = new TestWebServiceMethodArgs("dev", "UsrErp", null) { ExtensionData = Extra(guessed) };

		// Act
		WebServiceTestResult result = tool.TestWebServiceMethod(args);

		// Assert
		result.Success.Should().BeFalse(because: "a guessed argument name is refused, not bound");
		result.Error.Should().Contain($"'{guessed}' -> '{canonical}'", because: "the refusal names the argument to use");
		resolver.DidNotReceiveWithAnyArgs().Resolve<IWebServiceDesigner>(default);
	}

	[Test]
	[Description("test-web-service-method passes parameters to the designer as given, arrays and arrays of objects included.")]
	public void TestWebServiceMethod_Should_Pass_Array_Values_To_The_Designer() {
		// Arrange
		IWebServiceDesigner designer = Substitute.For<IWebServiceDesigner>();
		TestWebServiceMethodTool tool = TestMethodTool(designer);
		Dictionary<string, JsonElement> parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
			"""{"UsrTags": ["a", "b"], "UsrLines": [{"UsrSku": "S1", "UsrSubs": [{"UsrNote": "n"}]}]}""");

		// Act
		WebServiceTestResult result = tool.TestWebServiceMethod(new TestWebServiceMethodArgs("dev", "UsrErp", "UsrGet", parameters));

		// Assert
		result.Success.Should().BeTrue(because: "arrays are checked against the method by the designer, not refused by the tool");
		designer.Received(1).TestMethod("UsrErp", "UsrGet", Arg.Is<IReadOnlyDictionary<string, JsonElement>>(
			p => ReferenceEquals(p, parameters)), false);
	}

	[TestCase(null, false)]
	[TestCase(false, false)]
	[TestCase(true, true)]
	[Description("test-web-service-method passes the user's confirmation to the designer only when user-confirmed is true.")]
	public void TestWebServiceMethod_Should_Pass_User_Confirmed_Only_When_True(bool? userConfirmed, bool expected) {
		// Arrange
		IWebServiceDesigner designer = Substitute.For<IWebServiceDesigner>();
		TestWebServiceMethodTool tool = TestMethodTool(designer);

		// Act
		WebServiceTestResult result = tool.TestWebServiceMethod(
			new TestWebServiceMethodArgs("dev", "UsrErp", "UsrPost", null, UserConfirmed: userConfirmed));

		// Assert
		result.Success.Should().BeTrue(because: "the designer's answer is returned as it is");
		designer.Received(1).TestMethod("UsrErp", "UsrPost", null, expected);
	}

	[Test]
	[Description("describe-web-service passes the method codes to the designer for the environment of the call.")]
	public void DescribeWebService_Should_Pass_The_Method_Codes() {
		// Arrange
		IWebServiceDesigner designer = Substitute.For<IWebServiceDesigner>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IWebServiceDesigner>(Arg.Any<EnvironmentOptions>()).Returns(designer);
		designer.Describe(default, default, default).ReturnsForAnyArgs(new WebServiceToolResult(true));
		string[] methods = ["UsrGetOrders"];

		// Act
		WebServiceToolResult result = new DescribeWebServiceTool(resolver).DescribeWebService(
			new DescribeWebServiceArgs("dev", "UsrErp", Methods: methods));

		// Assert
		result.Success.Should().BeTrue(because: "the designer's answer is returned as it is");
		designer.Received(1).Describe("UsrErp", false, Arg.Is<IReadOnlyList<string>>(m => ReferenceEquals(m, methods)));
	}

	[Test]
	[Description("list-web-services passes the filter to the designer and cuts a list over the reply budget, with a notice to narrow it.")]
	public void ListWebServices_Should_Pass_The_Filter_And_Cut_A_Long_List() {
		// Arrange
		IWebServiceDesigner designer = Substitute.For<IWebServiceDesigner>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IWebServiceDesigner>(Arg.Any<EnvironmentOptions>()).Returns(designer);
		List<WebServiceListItem> services = Enumerable.Range(0, 400).Select(i => new WebServiceListItem(Guid.NewGuid(),
			$"UsrService{i:000}", $"Service number {i}", "REST", "Custom", $"https://api{i}.example.org/v1")).ToList();
		designer.List("Usr").Returns(services);

		// Act
		WebServiceListResult result = new ListWebServicesTool(resolver).ListWebServices(new ListWebServicesArgs("dev", "Usr"));

		// Assert
		result.Success.Should().BeTrue(because: "the list is read");
		result.Services.Count.Should().BeInRange(1, 399, because: "400 services do not fit the reply");
		result.Services.Select(s => s.Code).Should().Equal(services.Take(result.Services.Count).Select(s => s.Code),
			because: "the services kept are the first ones, in order");
		result.Notices.Should().ContainSingle(n => n.Contains($"{400 - result.Services.Count} of the 400 services")
			&& n.Contains("filter"), because: "the agent is told how many were left out and how to narrow the list");
		McpResultSize.Of(result).Should().BeLessThanOrEqualTo(McpResultSize.InlineReplyBudgetBytes,
			because: "the cut list is returned inline");
	}

	[Test]
	[Description("modify-web-service keeps the methods its operations add or replace in full when the reply is cut, whatever their place in stored order.")]
	public void ModifyWebService_Should_Keep_The_Changed_Methods_In_Full() {
		// Arrange
		IWebServiceDesigner designer = Substitute.For<IWebServiceDesigner>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IWebServiceDesigner>(Arg.Any<EnvironmentOptions>()).Returns(designer);
		List<WebServiceMethodView> methods = Enumerable.Range(0, 60).Select(i => new WebServiceMethodView($"UsrM{i:00}",
			$"Method {i}", "GET", $"orders/{i}", 5000, "json", false,
			[.. Enumerable.Range(0, 6).Select(p => new WebServiceParameterView($"UsrQuery{p}", $"Query {p}", "query",
				$"query_{p}", "Text", false, false, null, null, false, []))], [])).ToList();
		designer.Modify(default, default, default).ReturnsForAnyArgs(new WebServiceToolResult(true,
			Service: new WebServiceView(Guid.NewGuid(), "UsrErp", "Erp", "Custom", "REST", "https://erp.example/", 0, null, methods)));

		// Act
		WebServiceToolResult result = new ModifyWebServiceTool(resolver).ModifyWebService(new ModifyWebServiceArgs("dev", "UsrErp",
			[new WebServiceOperationArg("setMethod", new WebServiceMethodArg("UsrM59", "GET", "orders/59"))]));

		// Assert
		result.Service.Methods.Should().Contain(m => m.Code == "UsrM59", because: "the method the call replaced is the one the agent checks");
		result.Service.MethodsNotShown.Should().NotBeEmpty(because: "60 methods do not fit the reply");
		McpResultSize.Of(result).Should().BeLessThanOrEqualTo(McpResultSize.InlineReplyBudgetBytes,
			because: "the reply is returned inline");
	}

	private static TestWebServiceMethodTool TestMethodTool(IWebServiceDesigner designer) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IWebServiceDesigner>(Arg.Any<EnvironmentOptions>()).Returns(designer);
		designer.TestMethod(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, JsonElement>>(),
			Arg.Any<bool>()).Returns(new WebServiceTestResult(true));
		return new TestWebServiceMethodTool(resolver);
	}

	[Test]
	[Description("register-web-service passes type and namespaces to the designer unchanged.")]
	public void RegisterWebService_Should_Pass_Type_And_Namespaces() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IWebServiceDesigner designer = Substitute.For<IWebServiceDesigner>();
		resolver.Resolve<IWebServiceDesigner>(Arg.Any<EnvironmentOptions>()).Returns(designer);
		designer.Register(Arg.Any<string>(), Arg.Any<WebServiceRegistration>()).Returns(new WebServiceToolResult(true));
		WebServiceNamespaceArg[] namespaces = [new("tem", "http://tempuri.org/")];
		var args = new RegisterWebServiceArgs("dev", "Custom", "UsrCalc", "http://x.example/svc.asmx", [],
			Type: "soap12", Namespaces: namespaces);
		var tool = new RegisterWebServiceTool(resolver);

		// Act
		tool.RegisterWebService(args);

		// Assert
		designer.Received(1).Register("Custom", Arg.Is<WebServiceRegistration>(r =>
			r.Type == "soap12" && ReferenceEquals(r.Namespaces, namespaces) && r.Code == "UsrCalc"));
	}

	[Test]
	[Description("describe-web-service answers service-code with the canonical code argument and keeps listing a truly unknown key.")]
	public void DescribeWebService_Should_Hint_Code_And_Keep_Unknown_Keys() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		var tool = new DescribeWebServiceTool(resolver);
		Dictionary<string, JsonElement> extra = Extra("service-code");
		extra["colour"] = JsonSerializer.SerializeToElement("x");
		var args = new DescribeWebServiceArgs("dev", null) { ExtensionData = extra };

		// Act
		WebServiceToolResult result = tool.DescribeWebService(args);

		// Assert
		result.Success.Should().BeFalse(because: "a guessed argument name is refused, not bound");
		result.Error.Should().Contain("'service-code' -> 'code'", because: "a known guess gets a rename hint");
		result.Error.Should().Contain("Unknown args: 'colour'", because: "an unrelated key is still reported as unknown");
		result.Error.Should().Contain("Valid: environment-name, code, usages, methods.", because: "the valid fields are listed");
		resolver.DidNotReceiveWithAnyArgs().Resolve<IWebServiceDesigner>(default);
	}

	[Test]
	[Description("modify-web-service keeps a top-level method-code as an unknown key: there it belongs inside an operation, not at the top.")]
	public void ModifyWebService_Should_Keep_Top_Level_Method_Code_Unknown() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		var tool = new ModifyWebServiceTool(resolver);
		var args = new ModifyWebServiceArgs("dev", "UsrErp", []) { ExtensionData = Extra("method-code") };

		// Act
		WebServiceToolResult result = tool.ModifyWebService(args);

		// Assert
		result.Success.Should().BeFalse(because: "an unknown key is refused");
		result.Error.Should().Contain("Unknown args: 'method-code'",
			because: "renaming it to method would be wrong for modify, whose methods sit inside operations");
		result.Error.Should().NotContain("->", because: "no rename is suggested");
	}

	[TestCase("url", "wsdl-url")]
	[TestCase("wsdl", "wsdl-url")]
	[TestCase("wsdlUrl", "wsdl-url")]
	[TestCase("files", "wsdl-files")]
	[TestCase("wsdl-path", "wsdl-files")]
	[TestCase("environment", "environment-name")]
	[Description("read-wsdl answers a guessed source argument name with the canonical one, before touching any environment.")]
	public void ReadWsdl_Should_Hint_The_Canonical_Argument(string guessed, string canonical) {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		var tool = new ReadWsdlTool(resolver);
		var args = new ReadWsdlArgs("dev") { ExtensionData = Extra(guessed) };

		// Act
		WsdlReadResult result = tool.ReadWsdl(args);

		// Assert
		result.Success.Should().BeFalse(because: "a guessed argument name is refused, not bound");
		result.Error.Should().Contain($"'{guessed}' -> '{canonical}'", because: "the refusal names the argument to use");
		resolver.DidNotReceiveWithAnyArgs().Resolve<IWsdlReader>(default);
	}

	[Test]
	[Description("read-wsdl refuses an unknown key with the valid argument list.")]
	public void ReadWsdl_Should_Refuse_Unknown_Keys() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		var tool = new ReadWsdlTool(resolver);
		var args = new ReadWsdlArgs("dev", "http://x/a?wsdl") { ExtensionData = Extra("service-name") };

		// Act
		WsdlReadResult result = tool.ReadWsdl(args);

		// Assert
		result.Error.Should().Contain("Unknown args: 'service-name'", because: "an argument read-wsdl does not have is refused");
		result.Error.Should().Contain("Valid: environment-name, wsdl-url, wsdl-files, port, code, service-code, operations.",
			because: "the valid fields are listed");
	}

	[Test]
	[Description("read-wsdl resolves the reader for the environment of the call and passes every argument through.")]
	public void ReadWsdl_Should_Pass_The_Request_To_The_Environment_Reader() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IWsdlReader reader = Substitute.For<IWsdlReader>();
		resolver.Resolve<IWsdlReader>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev")).Returns(reader);
		reader.Read(Arg.Any<WsdlReadRequest>()).Returns(new WsdlReadResult(true));
		var tool = new ReadWsdlTool(resolver);

		// Act
		WsdlReadResult result = tool.ReadWsdl(new ReadWsdlArgs("dev", null, ["/tmp/a.wsdl"], "Soap", "UsrCalc", ["Add"]));

		// Assert
		result.Success.Should().BeTrue(because: "the reader's result is returned");
		reader.Received(1).Read(Arg.Is<WsdlReadRequest>(r => r.EnvironmentName == "dev" && r.WsdlUrl == null
			&& r.WsdlFiles.Single() == "/tmp/a.wsdl" && r.Port == "Soap" && r.ServiceCode == "UsrCalc"
			&& r.Operations.Single() == "Add"));
	}

	[Test]
	[Description("read-wsdl passes the code the user named for a new service to the reader.")]
	public void ReadWsdl_Should_Pass_The_New_Service_Code() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IWsdlReader reader = Substitute.For<IWsdlReader>();
		resolver.Resolve<IWsdlReader>(Arg.Any<EnvironmentOptions>()).Returns(reader);
		reader.Read(Arg.Any<WsdlReadRequest>()).Returns(new WsdlReadResult(true));
		var tool = new ReadWsdlTool(resolver);

		// Act
		WsdlReadResult result = tool.ReadWsdl(new ReadWsdlArgs("dev", "http://x/a?wsdl") { Code = "UsrTcCalcW" });

		// Assert
		result.Success.Should().BeTrue(because: "the reader's result is returned");
		reader.Received(1).Read(Arg.Is<WsdlReadRequest>(r => r.Code == "UsrTcCalcW" && r.ServiceCode == null));
	}

	[Test]
	[Description("A reader failure is returned as success false with the redacted message, not thrown.")]
	public void ReadWsdl_Should_Return_A_Failure_As_An_Error() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IWsdlReader reader = Substitute.For<IWsdlReader>();
		resolver.Resolve<IWsdlReader>(Arg.Any<EnvironmentOptions>()).Returns(reader);
		reader.Read(Arg.Any<WsdlReadRequest>()).Returns(_ => throw new InvalidOperationException(
			"The platform refused to download the WSDL: Error downloading schema http://x"));
		var tool = new ReadWsdlTool(resolver);

		// Act
		WsdlReadResult result = tool.ReadWsdl(new ReadWsdlArgs("dev", "http://x"));

		// Assert
		result.Success.Should().BeFalse(because: "the download failed");
		result.Error.Should().Contain("Error downloading schema", because: "the platform's reason reaches the agent");
	}

	[Test]
	[Description("Over HTTP, read-wsdl refuses wsdl-files, which would read files on the machine running clio.")]
	public void ReadWsdl_Should_Refuse_Local_Files_Over_Http() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(new DefaultHttpContext());
		var tool = new ReadWsdlTool(resolver, accessor);

		// Act
		WsdlReadResult result = tool.ReadWsdl(new ReadWsdlArgs("dev", null, ["/etc/hosts"]));

		// Assert
		result.Success.Should().BeFalse(because: "an HTTP client cannot point at the server's files");
		result.Error.Should().Contain("wsdl-url", because: "the refusal names the way that works over HTTP");
		resolver.DidNotReceiveWithAnyArgs().Resolve<IWsdlReader>(default);
	}

	[Test]
	[Description("A bare service key is not renamed to code, because an agent may have wrapped the whole service in it.")]
	public void DescribeWebService_Should_Not_Rename_A_Bare_Service_Key() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		var tool = new DescribeWebServiceTool(resolver);
		var args = new DescribeWebServiceArgs("dev", "UsrErp") { ExtensionData = Extra("service") };

		// Act
		WebServiceToolResult result = tool.DescribeWebService(args);

		// Assert
		result.Error.Should().Contain("Unknown args: 'service'", because: "the key is reported for the agent to fix itself");
		result.Error.Should().NotContain("->", because: "a rename to code could put an object or a caption into code");
	}

	[Test]
	[Description("register-web-service sends an agent that has a WSDL to read-wsdl first, so it does not hand-build a SOAP service or infer a SOAP action from one.")]
	public void RegisterWebService_Description_Should_Send_A_Wsdl_To_ReadWsdl() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(RegisterWebServiceTool).GetMethod(nameof(RegisterWebServiceTool.RegisterWebService));

		// Act
		string description = System.Reflection.CustomAttributeExtensions
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(method!)!.Description;

		// Assert
		description.Should().Contain("call read-wsdl first",
			because: "agents on a stand registered a hand-built service from a WSDL when nothing pointed them at read-wsdl");
		description.Should().Contain("never hand-build what read-wsdl returned or infer a soap-action",
			because: "an inferred SOAP action is wrong for any operation that does not follow the pattern");
	}

	[Test]
	[Description("read-wsdl runs in the host process: its refusal of wsdl-files over HTTP reads the host's HttpContext, which a call relayed to a worker would not have.")]
	public void ReadWsdl_Should_Run_In_Process() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(ReadWsdlTool).GetMethod(nameof(ReadWsdlTool.ReadWsdl));

		// Act
		McpToolExecutionAttribute execution = System.Reflection.CustomAttributeExtensions
			.GetCustomAttribute<McpToolExecutionAttribute>(method!);

		// Assert
		execution!.Location.Should().Be(McpToolExecutionLocation.InProcess,
			because: "relayed to a worker, the HTTP guard would not see the caller and a remote client could read local paths");
	}
}
