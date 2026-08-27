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

	#endregion
}
