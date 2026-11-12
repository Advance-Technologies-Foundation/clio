using System.Collections.Generic;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.WebServiceDesigner;
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
