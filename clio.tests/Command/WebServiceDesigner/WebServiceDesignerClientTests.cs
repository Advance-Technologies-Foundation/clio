using Clio.Command.WebServiceDesigner;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.WebServiceDesigner;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WebServiceDesignerClientTests {

	private static ServiceMethodCallResult Call(string reply) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://stand/rest/CallServiceSchemaService/Execute");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>()).Returns(reply);
		var client = new WebServiceDesignerClient(applicationClient, urlBuilder);
		return client.CallMethod("UsrSvc", "UsrOp", null);
	}

	[TestCase("\"{\\\"UsrResult\\\":5}\"", "{\"UsrResult\":5}", Description = "a JSON text, as measured")]
	[TestCase("{\"UsrResult\":5}", "{\"UsrResult\":5}", Description = "a JSON object")]
	[TestCase("null", null, Description = "null")]
	[Description("ParameterValues is read whether the platform sends it as a JSON text or as an object, and null stays null.")]
	public void CallMethod_Should_Read_Parameter_Values_In_Either_Shape(string wire, string expected) {
		// Arrange
		string reply = "{\"ExecuteResult\":{\"success\":true,\"StatusCode\":200,\"ResponseBody\":\"<ok/>\","
			+ $"\"ParameterValues\":{wire}}}}}";

		// Act
		ServiceMethodCallResult result = Call(reply);

		// Assert
		result.ParameterValues.Should().Be(expected, because: "the parsed outputs reach the masker as JSON text");
		result.ResponseBody.Should().Be("<ok/>", because: "the shape of ParameterValues does not affect the rest of the reply");
	}

	[Test]
	[Description("The test call posts a scalar as code and value, an array of scalars as a JSON array in value, and an array of objects in nested with no value member, the shape Studio's test request sends.")]
	public void CallMethod_Should_Post_Values_And_Nested_Items_In_The_Platform_Shape() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.CallServiceSchemaMethod)
			.Returns("http://stand/rest/CallServiceSchemaService/Execute");
		string posted = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(body => posted = body), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>()).Returns("{\"ExecuteResult\":{\"success\":true,\"StatusCode\":200}}");
		var client = new WebServiceDesignerClient(applicationClient, urlBuilder);
		ServiceCallParameter[] parameters = [
			new() { Code = "UsrA", Value = "x" },
			new() { Code = "UsrTags", Value = new[] { "a", "b" } },
			new() { Code = "UsrLines", Nested = [[new ServiceCallParameter { Code = "UsrSku", Value = "S1" }]] }
		];

		// Act
		client.CallMethod("UsrSvc", "UsrOp", parameters);

		// Assert
		posted.Should().Be("{\"serviceName\":\"UsrSvc\",\"methodName\":\"UsrOp\",\"parameters\":["
			+ "{\"code\":\"UsrA\",\"value\":\"x\"},{\"code\":\"UsrTags\",\"value\":[\"a\",\"b\"]},"
			+ "{\"code\":\"UsrLines\",\"nested\":[[{\"code\":\"UsrSku\",\"value\":\"S1\"}]]}]}",
			because: "the platform reads value for text and arrays of scalars and nested for arrays of objects");
	}
}
