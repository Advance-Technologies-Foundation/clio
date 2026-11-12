using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
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
	[Description("test-web-service-method answers a guessed method or service argument name with the canonical name, before touching any environment.")]
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
		result.Error.Should().Contain("Valid: environment-name, code, usages.", because: "the valid fields are listed");
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
	[Description("read-wsdl refuses an unknown key with the valid argument list, and a bare code is not taken for service-code.")]
	public void ReadWsdl_Should_Refuse_Unknown_Keys() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		var tool = new ReadWsdlTool(resolver);
		var args = new ReadWsdlArgs("dev", "http://x/a?wsdl") { ExtensionData = Extra("code") };

		// Act
		WsdlReadResult result = tool.ReadWsdl(args);

		// Assert
		result.Error.Should().Contain("Unknown args: 'code'", because: "code could mean the new service's code, not a target");
		result.Error.Should().Contain("Valid: environment-name, wsdl-url, wsdl-files, port, service-code, operations.",
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
}
