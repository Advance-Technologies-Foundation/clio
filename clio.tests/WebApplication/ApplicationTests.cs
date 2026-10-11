namespace Clio.Tests.WebApplication;

using System.Linq;
using System.Threading;
using Clio.Common;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
internal sealed class ApplicationTests {

	[TestCase(true, "/ServiceModel/AppInstallerService.svc/RestartApp")]
	[TestCase(false, "/ServiceModel/AppInstallerService.svc/UnloadAppDomain")]
	[Description("ENG-102757: a restart through a given session sends the runtime-specific restart request through that session, not through the application's own client.")]
	public void Restart_ShouldSendRestartThroughGivenSession_WhenSessionIsSupplied(bool isNetCore,
		string expectedServicePath) {
		// Arrange
		EnvironmentSettings environmentSettings = new() { IsNetCore = isNetCore };
		IApplicationClient ownClient = Substitute.For<IApplicationClient>();
		IApplicationClient session = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>(), environmentSettings)
			.Returns(callInfo => callInfo.ArgAt<string>(0));
		Application application = new(new EnvironmentSettings(), ownClient, serviceUrlBuilder,
			Substitute.For<System.IO.Abstractions.IFileSystem>(), Substitute.For<ILogger>());

		// Act
		application.Restart(session, environmentSettings);

		// Assert
		session.ReceivedCalls()
			.Select(call => call.GetArguments().Take(3))
			.Should().ContainSingle(because: "the restart is one request through the given session")
			.Which.Should().Equal([expectedServicePath, "{}", Timeout.Infinite],
				because: "each runtime has its own restart endpoint and the restart may take as long as it needs");
		ownClient.ReceivedCalls().Should().BeEmpty(
			because: "on a web farm only the installation session is routed to the node that installed the package");
	}

}
