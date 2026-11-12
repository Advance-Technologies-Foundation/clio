using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

	private static (WebServiceDesignerClient Client, IApplicationClient ApplicationClient) DownloadClient(string reply) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DownloadWsdlSchemaFiles)
			.Returns("http://stand/0/rest/DownloadWsdlEndpoint/GetXmlSchemaFilesContent");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>()).Returns(reply);
		return (new WebServiceDesignerClient(applicationClient, urlBuilder), applicationClient);
	}

	[Test]
	[Description("The WSDL download posts the URL array to the platform endpoint and turns each file's ASCII codes into a file keyed by its URL or schemaLocation.")]
	public void DownloadWsdl_Should_Read_The_Platform_Files() {
		// Arrange
		(WebServiceDesignerClient client, IApplicationClient applicationClient) = DownloadClient(
			"{\"success\":true,\"errorInfo\":null,\"files\":[{\"Key\":\"http://x/a?wsdl\",\"Value\":[60,97,47,62]},"
			+ "{\"Key\":\"types.xsd\",\"Value\":[60,98,47,62]}]}");

		// Act
		IReadOnlyList<WsdlSourceFile> files = client.DownloadWsdl("http://x/a?wsdl");

		// Assert
		files.Select(f => (f.Key, Encoding.ASCII.GetString(f.Content), f.AsciiText)).Should().Equal(
			[("http://x/a?wsdl", "<a/>", true), ("types.xsd", "<b/>", true)],
			because: "an import finds its file by the schemaLocation the platform keys it by");
		applicationClient.Received(1).ExecutePostRequest("http://stand/0/rest/DownloadWsdlEndpoint/GetXmlSchemaFilesContent",
			"[\"http://x/a?wsdl\"]", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("A failed download reports the platform's message, redacted, and never its stack trace.")]
	public void DownloadWsdl_Should_Report_The_Platform_Error_Without_Its_Stack_Trace() {
		// Arrange
		(WebServiceDesignerClient client, _) = DownloadClient(
			"{\"success\":false,\"errorInfo\":{\"message\":\"Error downloading schema http://x/a?wsdl password=hunter2\","
			+ "\"stackTrace\":\"at Terrasoft.Configuration.DownloadWsdlEndpoint.GetUriContent()\"},\"files\":null}");

		// Act
		Action act = () => client.DownloadWsdl("http://x/a?wsdl");

		// Assert
		InvalidOperationException error = act.Should().Throw<InvalidOperationException>(because: "the platform refused").Which;
		error.Message.Should().Contain("Error downloading schema", because: "the platform's reason is passed on");
		error.Message.Should().NotContain("Terrasoft.Configuration", because: "the stack trace is never returned");
		error.Message.Should().NotContain("hunter2", because: "the platform's text is redacted");
	}

	private sealed class CappedClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder)
		: CreatioServiceClient(applicationClient, urlBuilder) {
		public object Post(long? max) => PostAndDeserialize<object>(ServiceUrlBuilder.KnownRoute.DownloadWsdlSchemaFiles,
			new { }, new CreatioRequestOptions(), max, "too large");
	}

	[Test]
	[Description("A reply longer than the cap a caller sets is refused before it is deserialized; without a cap nothing changes.")]
	public void PostAndDeserialize_Should_Refuse_A_Reply_Over_The_Cap() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://stand/x");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>()).Returns("{\"value\":\"0123456789\"}");
		var client = new CappedClient(applicationClient, urlBuilder);

		// Act / Assert
		((Action)(() => client.Post(10))).Should().Throw<InvalidOperationException>(because: "the reply is over the cap")
			.WithMessage("too large");
		client.Post(null).Should().NotBeNull(because: "no cap leaves the reply as it was");
	}

	[Test]
	[Description("A file value outside byte range, or a download with no file, is refused.")]
	public void DownloadWsdl_Should_Refuse_What_Is_Not_A_Text_File() {
		// Arrange
		(WebServiceDesignerClient notText, _) = DownloadClient(
			"{\"success\":true,\"files\":[{\"Key\":\"http://x\",\"Value\":[60,1000]}]}");
		(WebServiceDesignerClient empty, _) = DownloadClient("{\"success\":true,\"files\":[]}");

		// Act / Assert
		((Action)(() => notText.DownloadWsdl("http://x"))).Should().Throw<InvalidOperationException>(
			because: "the endpoint sends byte codes only").WithMessage("*not text*");
		((Action)(() => empty.DownloadWsdl("http://x"))).Should().Throw<InvalidOperationException>(
			because: "there is nothing to read").WithMessage("*no file*");
	}
}
