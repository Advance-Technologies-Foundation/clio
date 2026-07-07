using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// HTTP-layer tests for <see cref="ListUserTasksService"/>: the resolved ListUserTasks route, the empty wrapped
/// body of the parameterless operation, and each response branch. The command tests substitute the service, so
/// this is the only coverage of the actual clio→server contract for list-user-tasks.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ListUserTasksServiceTests {

	private const string Env = "sandbox";
	private const string ListUrl = "http://sandbox/0/rest/ProcessDesignService/ListUserTasks";

	private static ListUserTasksService CreateService(IApplicationClient client) {
		EnvironmentSettings env = new() { Uri = "http://sandbox", Login = "Supervisor", Password = "Supervisor" };
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns(env);
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		factory.CreateEnvironmentClient(env).Returns(client);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ListUserTasks, env).Returns(ListUrl);
		return new ListUserTasksService(settings, factory, urlBuilder, Substitute.For<ILogger>());
	}

	[Test]
	[Description("Posts an empty wrapped body to the resolved ListUserTasks route and projects the returned tasks (name + uid).")]
	public void GetUserTasks_ShouldPostToListRoute_AndProjectTasks_OnSuccess() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(ListUrl, Arg.Any<string>()).Returns(
			"{\"ListUserTasksResult\":{\"success\":true,\"userTasks\":[{\"name\":\"ActivityUserTask\",\"uid\":\"b5c726f2-af5b-4381-bac6-913074144308\"},{\"name\":\"ReadDataUserTask\",\"uid\":\"cb455b6f-78ff-4b1e-b241-c2bbc0b37e9f\"}]}}");
		ListUserTasksService service = CreateService(client);

		// Act
		IReadOnlyList<UserTaskInfoResult> tasks = service.GetUserTasks(Env);

		// Assert
		tasks.Select(t => t.Name).Should().Equal(
			new[] { "ActivityUserTask", "ReadDataUserTask" }, (actual, expected) => actual == expected,
			because: "the service projects the server's task list in order");
		client.Received(1).ExecutePostRequest(ListUrl, "{}");
	}

	[Test]
	[Description("Surfaces the server's errorMessage when the ListUserTasks result reports success=false.")]
	public void GetUserTasks_ShouldThrowWithServerMessage_WhenSuccessFalse() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(ListUrl, Arg.Any<string>()).Returns(
			"{\"ListUserTasksResult\":{\"success\":false,\"errorMessage\":\"You don't have permission.\"}}");
		ListUserTasksService service = CreateService(client);

		Action act = () => service.GetUserTasks(Env);

		act.Should().Throw<InvalidOperationException>(because: "a server-reported failure must not be swallowed")
			.WithMessage("*You don't have permission*");
	}

	[Test]
	[Description("Throws a clear error when the response envelope has no ListUserTasksResult payload.")]
	public void GetUserTasks_ShouldThrow_WhenResponseShapeUnexpected() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(ListUrl, Arg.Any<string>()).Returns("{}");
		ListUserTasksService service = CreateService(client);

		Action act = () => service.GetUserTasks(Env);

		act.Should().Throw<InvalidOperationException>(because: "a missing result payload is an unexpected server response")
			.WithMessage("*unexpected response shape*");
	}

	[Test]
	[Description("Returns an empty list (not null) when the environment exposes no user tasks.")]
	public void GetUserTasks_ShouldReturnEmpty_WhenNoTasks() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(ListUrl, Arg.Any<string>()).Returns(
			"{\"ListUserTasksResult\":{\"success\":true,\"userTasks\":[]}}");
		ListUserTasksService service = CreateService(client);

		service.GetUserTasks(Env).Should().BeEmpty(because: "an empty palette projects to an empty list, not null");
	}

	[Test]
	[Description("Throws (without calling the server) when the target environment is not registered.")]
	public void GetUserTasks_ShouldThrow_WhenEnvironmentNotFound() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.FindEnvironment(Env).Returns((EnvironmentSettings)null);
		var service = new ListUserTasksService(settings,
			Substitute.For<IApplicationClientFactory>(), Substitute.For<IServiceUrlBuilder>(), Substitute.For<ILogger>());

		Action act = () => service.GetUserTasks(Env);

		act.Should().Throw<InvalidOperationException>(because: "an unknown environment cannot be targeted");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}
}
