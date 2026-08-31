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
	[Description("Executes PATCH when method is patch and passes body")]
	public void Execute_Should_Call_Patch_When_Method_Patch() {
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
			RequestBody = "{\"id\":1}",
			TimeOut = 12_345,
			MaxAttempts = 7,
			RetryDelay = 3
		};

		// Act
		command.Execute(options);

		// Assert
		applicationClient
			.Received(1)
			.ExecutePatchRequest("http://host/svc", "{\"id\":1}", 12_345, 7, 3);
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient.DidNotReceiveWithAnyArgs().ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Executes PUT when method is put and passes body")]
	public void Execute_Should_Call_Put_When_Method_Put() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings settings = new();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		CallServiceCommand command = new(applicationClient, settings, serviceUrlBuilder, fileSystem);
		CallServiceCommandOptions options = new() {
			ServicePath = "svc",
			HttpMethodName = "put",
			RequestBody = "{\"id\":1}",
			TimeOut = 54_321,
			MaxAttempts = 5,
			RetryDelay = 2
		};

		// Act
		command.Execute(options);

		// Assert
		applicationClient
			.Received(1)
			.ExecutePutRequest("http://host/svc", "{\"id\":1}", 54_321, 5, 2);
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
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
			HttpMethodName = "options",
			RequestBody = "{}"
		};

		// Act
		Func<int> action = () => command.Execute(options);

		// Assert
		action.Should()
			  .Throw<ArgumentException>("because only GET/POST/DELETE/PATCH/PUT are supported")
			  .WithParameterName("httpMethod")
			  .WithMessage("Unsupported HTTP method 'options'*");
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient.DidNotReceiveWithAnyArgs().ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient.DidNotReceiveWithAnyArgs()
							 .ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePutRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	#endregion
	[Test]
	[Description("Without --timeout the call-service default of 60000 ms and the option defaults for retries reach the client - the verb must not fall back to the interface defaults of no timeout and a single attempt")]
	public void Execute_Should_Pass_CommandDefaultTimeout_When_TimeoutNotProvided() {
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
			RequestBody = "{\"id\":1}"
		};

		// Act
		command.Execute(options);

		// Assert
		applicationClient
			.Received(1)
			.ExecutePatchRequest("http://host/svc", "{\"id\":1}", 60_000, 3, 1);
	}

}
