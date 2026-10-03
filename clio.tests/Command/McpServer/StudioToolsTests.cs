using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.Studio;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture, Category("Unit"), Property("Module", "McpServer"), NonParallelizable]
public class StudioToolsTests {
	[Test, Description("Host opt-out rejects all three operations before files or services are accessed.")]
	public void HostOptOut_ShouldRejectEveryOperation() {
		var checkout = Substitute.For<IStudioCheckout>(); var deployment = Substitute.For<IStudioDeploymentService>();
		var logger = ConsoleLogger.Instance;
		var tool = new StudioTools(new StudioCommand(checkout, deployment, logger), logger, Substitute.For<IRuntimeMcpHostPolicy>(), null);
		tool.Deploy("missing.json", "cluster").ExitCode.Should().Be(1, "host opt-in is required before reading the profile");
		tool.Checkout("missing.json", "workspace").ExitCode.Should().Be(1, "checkout also changes the host");
		tool.Status("name", "cluster", "namespace").ExitCode.Should().Be(1, "cluster credentials belong to the opted-in host");
		checkout.DidNotReceive().Checkout(Arg.Any<JObject>(), Arg.Any<string>());
		deployment.DidNotReceive().Deploy(Arg.Any<JObject>(), Arg.Any<StudioOptions>());
		deployment.DidNotReceive().Status(Arg.Any<StudioOptions>());
	}

	[Test, Description("Status forwards the explicit context, namespace and name through the CLI service.")]
	public void Status_ShouldUseCliService() {
		var deployment = Substitute.For<IStudioDeploymentService>(); deployment.Status(Arg.Any<StudioOptions>()).Returns(new JObject { ["state"] = "Ready" });
		var host = Substitute.For<IRuntimeMcpHostPolicy>(); host.Enabled.Returns(true);
		var logger = ConsoleLogger.Instance;
		var tool = new StudioTools(new StudioCommand(Substitute.For<IStudioCheckout>(), deployment, logger), logger, host, null);
		tool.Status("demo", "local", "studio").ExitCode.Should().Be(0, "a successful service read is returned to the agent");
		deployment.Received().Status(Arg.Is<StudioOptions>(o => o.Name == "demo" && o.Context == "local" && o.Namespace == "studio"));
	}
}
