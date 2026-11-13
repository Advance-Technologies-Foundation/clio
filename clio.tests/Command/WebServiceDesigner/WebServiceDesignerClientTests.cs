using System.Collections.Generic;
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

	private static ServiceMethodCallResult Call(string reply) => Call(reply, null, out _);

	/// <summary>Calls the method through a client whose transport answers <paramref name="reply"/>.</summary>
	/// <param name="posted">The request body the client posted.</param>
	private static ServiceMethodCallResult Call(string reply, IReadOnlyList<ServiceCallParameter> parameters,
		out string posted) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://stand/rest/CallServiceSchemaService/Execute");
		string body = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(sent => body = sent), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>()).Returns(reply);
		var client = new WebServiceDesignerClient(applicationClient, urlBuilder);
		ServiceMethodCallResult result = client.CallMethod("UsrSvc", "UsrOp", parameters);
		posted = body;
		return result;
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
		ServiceCallParameter[] parameters = [
			new() { Code = "UsrA", Value = "x" },
			new() { Code = "UsrTags", Value = new[] { "a", "b" } },
			new() { Code = "UsrLines", Nested = [[new ServiceCallParameter { Code = "UsrSku", Value = "S1" }]] }
		];

		// Act
		Call("{\"ExecuteResult\":{\"success\":true,\"StatusCode\":200}}", parameters, out string posted);

		// Assert
		posted.Should().Be("{\"serviceName\":\"UsrSvc\",\"methodName\":\"UsrOp\",\"parameters\":["
			+ "{\"code\":\"UsrA\",\"value\":\"x\"},{\"code\":\"UsrTags\",\"value\":[\"a\",\"b\"]},"
			+ "{\"code\":\"UsrLines\",\"nested\":[[{\"code\":\"UsrSku\",\"value\":\"S1\"}]]}]}",
			because: "the platform reads value for text and arrays of scalars and nested for arrays of objects");
	}
}
