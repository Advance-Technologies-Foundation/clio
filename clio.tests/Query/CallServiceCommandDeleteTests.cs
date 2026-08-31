using System;
using Clio.Common;
using Clio.Query;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Query;

[TestFixture]
[Property("Module", "Query")]
public class CallServiceCommandDeleteTests : BaseCommandTests<CallServiceCommandOptions>{
	#region Methods: Public

	[Test]
	[Description("Executes DELETE when method is delete (case-insensitive) and passes body")]
	public void Execute_Should_Call_Delete_When_Method_Delete() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem);
		CallServiceCommandOptions options = new() {
			ServicePath = "svc",
			HttpMethodName = "delete",
			RequestBody = "{\"id\":1}"
		};

		// Act
		command.Execute(options);

		// Assert
		applicationClient
			.Received(1)
			.ExecuteDeleteRequest("http://host/svc", "{\"id\":1}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient
			.DidNotReceive()
			.ExecutePostRequest("http://host/svc", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Defaults to POST when method is not provided")]
	public void Execute_Should_Default_To_Post_When_Method_Not_Provided() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem);
		CallServiceCommandOptions options = new() {
			ServicePath = "svc",
			RequestBody = "{}"
		};

		// Act
		command.Execute(options);

		// Assert
		applicationClient
			.Received(1)
			.ExecutePostRequest("http://host/svc", "{}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient
			.DidNotReceive()
			.ExecuteDeleteRequest("http://host/svc", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Throws on unsupported HTTP method to avoid silent defaulting")]
	public void Execute_Should_Throw_For_Unsupported_Method() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem);
		CallServiceCommandOptions options = new() {
			ServicePath = "svc",
			HttpMethodName = "patch",
			RequestBody = "{}"
		};

		// Act
		Func<int> action = () => command.Execute(options);

		// Assert
		action.Should()
			  .Throw<ArgumentException>("because only GET/POST/DELETE are supported")
			  .WithParameterName("httpMethod")
			  .WithMessage("Unsupported HTTP method 'patch'*");
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient.DidNotReceiveWithAnyArgs().ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient.DidNotReceiveWithAnyArgs()
							 .ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[TestCase("/0/odata/BulkEmailCategory")]
	[TestCase("0/odata/BulkEmailCategory")]
	[TestCase("/odata/BulkEmailCategory")]
	[Description("Normalizes application-root service paths before URL construction")]
	public void Execute_ShouldNormalizeServicePath_WhenOptionalApplicationRootIsProvided(string servicePath) {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem);
		CallServiceCommandOptions options = new() { ServicePath = servicePath, HttpMethodName = "GET" };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "a normalized service path should execute successfully");
		serviceUrlBuilder.Received(1).Build("odata/BulkEmailCategory");
		applicationClient.Received(1).ExecuteGetRequest("http://host/0/odata/BulkEmailCategory",
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Returns a non-zero result and does not save a Creatio error envelope")]
	public void Execute_ShouldFailWithoutSaving_WhenCreatioReturnsErrorEnvelope() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Code\":-1,\"Exception\":\"request failed\"}");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem) {
			Logger = Substitute.For<ILogger>()
		};
		CallServiceCommandOptions options = new() {
			ServicePath = "odata/BulkEmailCategory",
			HttpMethodName = "GET",
			ResultFileName = "result.json"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "a Creatio error envelope is not a successful service response");
		fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Returns a non-zero result and does not save an IIS HTML error page")]
	public void Execute_ShouldFailWithoutSaving_WhenIisReturnsHtmlErrorPage() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<!DOCTYPE html><html><head><title>404 - File or directory not found.</title></head></html>");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem) {
			Logger = Substitute.For<ILogger>()
		};
		CallServiceCommandOptions options = new() {
			ServicePath = "odata/BulkEmailCategory",
			HttpMethodName = "GET",
			ResultFileName = "result.json"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "an IIS error page is not a successful service response");
		fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Classifies a Creatio error envelope on the POST branch too, not only on GET")]
	public void Execute_ShouldFailWithoutSaving_WhenPostReturnsErrorEnvelope() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Code\":-1,\"Exception\":\"request failed\"}");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem) {
			Logger = Substitute.For<ILogger>()
		};
		CallServiceCommandOptions options = new() {
			ServicePath = "odata/BulkEmailCategory",
			HttpMethodName = "POST",
			RequestBody = "{}",
			ResultFileName = "result.json"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1,
			"the error classification must fire on the POST dispatch branch, not only on GET");
		fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Classifies an IIS HTML error page on the DELETE branch too, not only on GET")]
	public void Execute_ShouldFailWithoutSaving_WhenDeleteReturnsHtmlErrorPage() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<!DOCTYPE html><html><head><title>500 - Internal server error.</title></head></html>");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem) {
			Logger = Substitute.For<ILogger>()
		};
		CallServiceCommandOptions options = new() {
			ServicePath = "odata/BulkEmailCategory",
			HttpMethodName = "DELETE",
			RequestBody = "{}",
			ResultFileName = "result.json"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1,
			"the error classification must fire on the DELETE dispatch branch, not only on GET");
		fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[TestCase("0/0/odata/BulkEmailCategory")]
	[TestCase("/0/0/odata/BulkEmailCategory")]
	[Description("Strips every application-root layer, not just the first - one surviving 0/ would be double-added by ServiceUrlBuilder on .NET Framework")]
	public void Execute_ShouldStripEveryApplicationRootLayer_WhenPrefixIsRepeated(string servicePath) {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem);
		CallServiceCommandOptions options = new() { ServicePath = servicePath, HttpMethodName = "GET" };

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "a fully normalized service path should execute successfully");
		serviceUrlBuilder.Received(1).Build("odata/BulkEmailCategory");
	}


	// Every shape below reached #1220's false-success path: the body was written to --destination and
	// the command exited 0. The expectation is the same for all of them - exit 1, nothing written,
	// no "Result saved" message.
	[TestCase("{\"error\":{\"message\":\"The query specified in the URI is not valid.\"}}",
		TestName = "OData v4 error envelope")]
	[TestCase("{\"Message\":\"An error has occurred.\",\"ExceptionType\":\"System.NullReferenceException\","
		+ "\"StackTrace\":\"   at Terrasoft.Core\"}", TestName = "ASP.NET exception envelope")]
	[TestCase("{\"Message\":\"No HTTP resource was found that matches the request URI.\","
		+ "\"MessageDetail\":\"No type was found that matches the controller named 'UsrThing'.\"}",
		TestName = "ASP.NET routing error")]
	[TestCase("{\"Code\":1,\"Message\":\"Unauthorized\"}", TestName = "authentication rejection")]
	[TestCase("\uFEFF<!DOCTYPE html><html><head><title>500 - Internal server error.</title></head></html>",
		TestName = "HTML page behind a byte-order mark")]
	[TestCase("<?xml version=\"1.0\" encoding=\"utf-8\"?><!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01//EN\">"
		+ "<html><head><title>Request Error</title></head><body>Service Unavailable</body></html>",
		TestName = "Creatio/IIS Request Error page behind an XML declaration")]
	[Description("Recognizes every Creatio error envelope and error-page preamble the platform actually returns, not only {Code,Exception} and a body that starts exactly with a doctype (issue 1220)")]
	public void Execute_ShouldFailWithoutSaving_WhenCreatioReturnsAKnownErrorShape(string responseBody) {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseBody);

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem) {
			Logger = logger
		};
		CallServiceCommandOptions options = new() {
			ServicePath = "odata/BulkEmailCategory",
			HttpMethodName = "GET",
			ResultFileName = "result.json"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "the response is an error, not a payload");
		fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
		logger.DidNotReceive().WriteInfo(Arg.Is<string>(m => m.Contains("Result saved")));
	}

	// The counterpart of the cases above: a payload that merely resembles an error envelope must
	// still be saved, otherwise the fix trades false success for false failure.
	[TestCase("{\"value\":[{\"Id\":\"1\",\"Code\":\"UsrCode\"}]}", TestName = "collection with a Code column")]
	[TestCase("{\"Code\":0,\"Exception\":\"\"}", TestName = "successful DataService envelope")]
	[TestCase("{\"Message\":\"ok\",\"value\":[]}", TestName = "payload carrying both Message and data")]
	[Description("A successful payload is still saved even when it carries members that look like error keys (issue 1220)")]
	public void Execute_ShouldSaveResponse_WhenPayloadOnlyResemblesAnErrorEnvelope(string responseBody) {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(responseBody);

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem) {
			Logger = Substitute.For<ILogger>()
		};
		CallServiceCommandOptions options = new() {
			ServicePath = "odata/BulkEmailCategory",
			HttpMethodName = "GET",
			ResultFileName = "result.json"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "the response is a payload");
		fileSystem.Received(1).WriteAllTextToFile("result.json", Arg.Any<string>());
	}

	[Test]
	[Description("The saved body is the indented form of the same document that was classified, with no character escaping introduced by the JSON writer (issue 1220)")]
	public void Execute_ShouldSaveIndentedResponse_WithoutEscapingPayloadCharacters() {
		// Arrange
		string saved = null;
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Name\":\"a+b <c> \\u00fc\"}");
		fileSystem.When(fs => fs.WriteAllTextToFile("result.json", Arg.Any<string>()))
			.Do(ci => saved = ci.ArgAt<string>(1));

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem) {
			Logger = Substitute.For<ILogger>()
		};
		CallServiceCommandOptions options = new() {
			ServicePath = "odata/BulkEmailCategory",
			HttpMethodName = "GET",
			ResultFileName = "result.json"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0);
		saved.Should().Contain("\n", because: "the response has to be written indented, as before");
		saved.Should().Contain("a+b <c> \u00fc",
			because: "the writer must not escape `+`, `<`, `>` or non-ASCII characters that the payload legitimately contains");
	}

	[Test]
	[Description("With --silent and no --destination nothing consumes the indented text, so it is never produced (issue 1220)")]
	public void Execute_ShouldNotRenderResponse_WhenSilentWithoutDestination() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("odata/BulkEmailCategory").Returns("http://host/0/odata/BulkEmailCategory");
		applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem) {
			Logger = logger
		};
		CallServiceCommandOptions options = new() {
			ServicePath = "odata/BulkEmailCategory",
			HttpMethodName = "GET",
			IsSilent = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0);
		logger.DidNotReceiveWithAnyArgs().WriteLine(Arg.Any<string>());
		fileSystem.DidNotReceiveWithAnyArgs().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	#endregion
}
