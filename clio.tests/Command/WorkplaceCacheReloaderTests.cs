using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class WorkplaceCacheReloaderTests {

	private static (WorkplaceCacheReloader reloader, IApplicationClient applicationClient) CreateReloader(
		bool isNetCore = false){
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IApplicationClientFactory factory = Substitute.For<IApplicationClientFactory>();
		EnvironmentSettings settings = new() {
			Uri = "http://localhost",
			IsNetCore = isNetCore
		};
		factory.CreateClient(settings).Returns(applicationClient);
		return (new WorkplaceCacheReloader(settings, factory, new ServiceUrlBuilder(settings)), applicationClient);
	}

	[Test]
	[Category("Unit")]
	[Description("Posts an empty JSON body to the cliogate ReloadWorkplaces route and returns without throwing when the gate reports success.")]
	public void Reload_ShouldPostToTheGateRoute_WhenTheGateReportsSuccess(){
		// Arrange
		(WorkplaceCacheReloader reloader, IApplicationClient applicationClient) = CreateReloader();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"success\":true}");

		// Act
		Action reload = () => reloader.Reload();

		// Assert
		reload.Should().NotThrow(
			because: "a successful gate response is the only case where the caller may promise that a refresh suffices");
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/rest/CreatioApiGateway/ReloadWorkplaces",
			"{}");
	}

	[Test]
	[Category("Unit")]
	[Description("Omits the WebApp prefix on a .NET Core host, so the same route resolves correctly on both runtimes.")]
	public void Reload_ShouldOmitTheWebAppPrefix_OnNetCoreHosts(){
		// Arrange
		(WorkplaceCacheReloader reloader, IApplicationClient applicationClient) = CreateReloader(isNetCore: true);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"success\":true}");

		// Act
		reloader.Reload();

		// Assert
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/rest/CreatioApiGateway/ReloadWorkplaces",
			"{}");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws and names cliogate when the environment answers with an empty body, instead of silently reporting a successful publish.")]
	public void Reload_ShouldThrow_WhenTheResponseIsEmpty(){
		// Arrange
		(WorkplaceCacheReloader reloader, IApplicationClient applicationClient) = CreateReloader();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(string.Empty);

		// Act
		Action reload = () => reloader.Reload();

		// Assert
		reload.Should().Throw<InvalidOperationException>(
				because: "an empty body proves nothing was reloaded, so it must not read as success")
			.WithMessage("*empty response*install-gate*",
				because: "an empty body usually means the endpoint is missing, so the message must point at cliogate");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws and says the response was not JSON when the environment returns an HTML error page.")]
	public void Reload_ShouldThrow_WhenTheResponseIsAnHtmlErrorPage(){
		// Arrange
		(WorkplaceCacheReloader reloader, IApplicationClient applicationClient) = CreateReloader();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("<html><body>Endpoint not found.</body></html>");

		// Act
		Action reload = () => reloader.Reload();

		// Assert
		reload.Should().Throw<InvalidOperationException>(
				because: "an HTML error page is the observed symptom of an outdated cliogate and must not pass as success")
			.WithMessage("*non-JSON response*",
				because: "naming the shape of the response is what tells the operator to check the installed gate version");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces the gate's own failure reason verbatim so the caller can fall back to prescribing a re-login for the right cause.")]
	public void Reload_ShouldSurfaceTheGateReason_WhenTheGateReportsFailure(){
		// Arrange
		const string reason = "You don't have permission for operation CanManageSolution.";
		(WorkplaceCacheReloader reloader, IApplicationClient applicationClient) = CreateReloader();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns($"{{\"success\":false,\"errorInfo\":{{\"message\":\"{reason}\"}}}}");

		// Act
		Action reload = () => reloader.Reload();

		// Assert
		reload.Should().Throw<InvalidOperationException>(
				because: "a gate failure means the caches are still stale")
			.WithMessage($"*{reason}*",
				because: "the reason decides the advice given to the user, so a generic message is not enough")
			.And.Message.Should().Contain("log out and back in",
				because: "when publishing failed the fallback instruction has to reach the user");
	}

	[Test]
	[Category("Unit")]
	[Description("Still throws with the re-login fallback when the gate reports failure without any message.")]
	public void Reload_ShouldThrow_WhenTheGateReportsFailureWithoutAReason(){
		// Arrange
		(WorkplaceCacheReloader reloader, IApplicationClient applicationClient) = CreateReloader();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns("{\"success\":false}");

		// Act
		Action reload = () => reloader.Reload();

		// Assert
		reload.Should().Throw<InvalidOperationException>(
				because: "a missing reason does not make a failed reload a success")
			.WithMessage("*log out and back in*",
				because: "the user still needs the fallback instruction even when the gate said nothing useful");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats an unrecognised success-less envelope as a failure, so a future wire-shape change cannot be mistaken for a successful publish.")]
	public void Reload_ShouldThrow_WhenTheEnvelopeShapeIsUnrecognised(){
		// Arrange
		(WorkplaceCacheReloader reloader, IApplicationClient applicationClient) = CreateReloader();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"ReloadWorkplacesResult\":{\"success\":true}}");

		// Act
		Action reload = () => reloader.Reload();

		// Assert
		reload.Should().Throw<InvalidOperationException>(
			because: "a wrapped envelope this code cannot read must fail closed rather than promise a refresh that will not work");
	}

}
