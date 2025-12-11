using System;
using Clio.Common;
using Clio.Query;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Query;

[TestFixture]
public class CallServiceCommandDeleteTests : Command.BaseCommandTests<CallServiceCommandOptions>
{
	[Test]
	[Description("Executes DELETE when method is delete (case-insensitive) and passes body")]
	public void Execute_Should_Call_Delete_When_Method_Delete()
	{
		// Arrange
		var applicationClient = Substitute.For<IApplicationClient>();
		var settings = new EnvironmentSettings();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		var command = new CallServiceCommand(applicationClient, settings, serviceUrlBuilder, fileSystem);
		var options = new CallServiceCommandOptions {
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
	public void Execute_Should_Default_To_Post_When_Method_Not_Provided()
	{
		// Arrange
		var applicationClient = Substitute.For<IApplicationClient>();
		var settings = new EnvironmentSettings();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		var command = new CallServiceCommand(applicationClient, settings, serviceUrlBuilder, fileSystem);
		var options = new CallServiceCommandOptions {
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
	public void Execute_Should_Throw_For_Unsupported_Method()
	{
		// Arrange
		var applicationClient = Substitute.For<IApplicationClient>();
		var settings = new EnvironmentSettings();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var fileSystem = Substitute.For<IFileSystem>();
		serviceUrlBuilder.Build("svc").Returns("http://host/svc");

		var command = new CallServiceCommand(applicationClient, settings, serviceUrlBuilder, fileSystem);
		var options = new CallServiceCommandOptions {
			ServicePath = "svc",
			HttpMethodName = "patch",
			RequestBody = "{}"
		};

		// Act
		var action = () => command.Execute(options);

		// Assert
		action.Should()
			.Throw<ArgumentException>("because only GET/POST/DELETE are supported")
			.WithParameterName("httpMethod")
			.WithMessage("Unsupported HTTP method 'patch'*");
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
		applicationClient.DidNotReceiveWithAnyArgs().ExecuteDeleteRequest(default!, default!, default, default, default);
		applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default!, default, default, default);
	}
}
