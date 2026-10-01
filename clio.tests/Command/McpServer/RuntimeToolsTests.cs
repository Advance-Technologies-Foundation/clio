using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.OperatorBootstrap;
using Clio.Common.RuntimeAttachment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture, Category("Unit"), Property("Module", "McpServer"), NonParallelizable]
public class RuntimeToolsTests {
	[Test, Description("Runtime methods reuse CLI services and preserve every supplied option.")]
	public void Methods_ShouldMapOptionsToExistingCommands() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var service = Substitute.For<IOperatorRuntimeService>();
		var attachment = Substitute.For<IRuntimeAttachmentService>();
		var policy = Substitute.For<IRuntimeMcpHostPolicy>(); policy.Enabled.Returns(true);
		var tool = new RuntimeTools(new RuntimeCommand(service, attachment, logger), logger, policy, null);
		// Act
		tool.Images("omen", "operator-ns"); tool.List("omen", "dev"); tool.Status("instance", "omen", "dev");
		tool.Create("instance", "omen", "registry/creatio-dev:tag", "dev"); tool.Build("F:/build.zip", "rancher-desktop");
		tool.Attach("instance", "F:/workspace", "alias", "omen", "dev", "operator-kubernetes", "develop"); tool.Detach("F:/workspace");
		// Assert
		service.Received().Execute(Arg.Is<RuntimeOptions>(o => o.Action == "images" && o.Context == "omen" && o.OperatorNamespace == "operator-ns" && o.Json));
		service.Received().Execute(Arg.Is<RuntimeOptions>(o => o.Action == "list" && o.Context == "omen" && o.Namespace == "dev"));
		service.Received().Execute(Arg.Is<RuntimeOptions>(o => o.Action == "status" && o.Name == "instance" && o.Context == "omen" && o.Namespace == "dev"));
		service.Received().Execute(Arg.Is<RuntimeOptions>(o => o.Action == "create" && o.Name == "instance" && o.Context == "omen" && o.Image == "registry/creatio-dev:tag" && o.Namespace == "dev"));
		service.Received().Execute(Arg.Is<RuntimeOptions>(o => o.Action == "build" && o.Source == "F:/build.zip" && o.Context == "rancher-desktop"));
		attachment.Received().Attach(Arg.Is<AttachOptions>(o => o.Environment == "instance" && o.Workspace == "F:/workspace" && o.SshAlias == "alias" && o.Context == "omen" && o.Namespace == "dev" && o.Provider == "operator-kubernetes" && o.Mode == "develop"));
		attachment.Received().Detach("F:/workspace");
	}
	[Test, Description("Missing host opt-in rejects runtime work before command execution.")]
	public void Tools_ShouldRejectWithoutHostOptIn() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var service = Substitute.For<IOperatorRuntimeService>();
		var attachment = Substitute.For<IRuntimeAttachmentService>();
		var policy = Substitute.For<IRuntimeMcpHostPolicy>();
		var tool = new RuntimeTools(new RuntimeCommand(service, attachment, logger), logger, policy, null);
		// Act
		var result = tool.Images("omen");
		// Assert
		result.ExitCode.Should().Be(1, because: "the runtime flag alone does not authorize this server as a developer host");
		result.Output.Should().ContainSingle(m => m is ErrorMessage, because: "the refusal must be actionable");
		service.DidNotReceive().Execute(Arg.Any<RuntimeOptions>());
	}
	[Test, Description("Operator installation preserves optional defaults and explicit overrides.")]
	public void Install_ShouldMapOptionsAndDefaults() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var installer = Substitute.For<IOperatorInstaller>();
		var policy = Substitute.For<IRuntimeMcpHostPolicy>(); policy.Enabled.Returns(true);
		var tool = new InstallOperatorTool(new InstallOperatorCommand(installer, logger), logger, policy, null);
		// Act
		tool.Install("rancher-desktop"); tool.Install("rancher-desktop", "local", "registry/operator:tag");
		// Assert
		installer.Received().Install(Arg.Is<InstallOperatorOptions>(o => o.Target == "rancher-desktop" && o.Context == "rancher-desktop" && o.Image == null));
		installer.Received().Install(Arg.Is<InstallOperatorOptions>(o => o.Target == "rancher-desktop" && o.Context == "local" && o.Image == "registry/operator:tag"));
	}
	[Test, Description("Both MCP classes use the runtime feature gate and default option values match the CLI.")]
	public void Tools_ShouldPreserveFeatureGateAndDefaults() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var service = Substitute.For<IOperatorRuntimeService>();
		var attachment = Substitute.For<IRuntimeAttachmentService>();
		var policy = Substitute.For<IRuntimeMcpHostPolicy>(); policy.Enabled.Returns(true);
		var tool = new RuntimeTools(new RuntimeCommand(service, attachment, logger), logger, policy, null);
		// Act
		tool.Images("cluster"); tool.List("cluster"); tool.Status("name", "cluster");
		tool.Create("name", "cluster", "image:tag"); tool.Attach("name", "workspace", "alias", "cluster");
		// Assert
		foreach (var type in new[] { typeof(RuntimeTools), typeof(InstallOperatorTool) }) {
			var attribute = (FeatureToggleAttribute)System.Attribute.GetCustomAttribute(type, typeof(FeatureToggleAttribute));
			attribute.Feature.Should().Be(ExperimentalFeature.Runtime, because: "CLI and MCP must share the runtime gate");
		}
		service.Received().Execute(Arg.Is<RuntimeOptions>(o => o.Action == "images" && o.OperatorNamespace == "creatio-system"));
		service.Received(3).Execute(Arg.Is<RuntimeOptions>(o => o.Action != "images" && o.Namespace == "creatio-runtimes"));
		attachment.Received().Attach(Arg.Is<AttachOptions>(o => o.Namespace == "creatio-runtimes" && o.Provider == "operator-kubernetes" && o.Mode == "develop"));
	}
	[Test, Description("Credential passthrough cannot invoke developer-host capabilities.")]
	public void Tools_ShouldRejectPassthroughBeforeServices() {
		// Arrange
		var logger = ConsoleLogger.Instance;
		var runtime = Substitute.For<IOperatorRuntimeService>();
		var installer = Substitute.For<IOperatorInstaller>();
		var policy = Substitute.For<IRuntimeMcpHostPolicy>(); policy.Enabled.Returns(true);
		var guard = Substitute.For<ICredentialPassthroughToolGuard>();
		guard.IsPassthroughActive.Returns(true);
		guard.BuildUnsupportedMessage(Arg.Any<string>(), Arg.Any<string>()).Returns("Unsupported under credential passthrough.");
		var tool = new RuntimeTools(new RuntimeCommand(runtime, Substitute.For<IRuntimeAttachmentService>(), logger), logger, policy, guard);
		var install = new InstallOperatorTool(new InstallOperatorCommand(installer, logger), logger, policy, guard);
		// Act
		var result = tool.Images("cluster");
		var installResult = install.Install("rancher-desktop");
		// Assert
		result.ExitCode.Should().Be(1, because: "passthrough authorizes a Creatio identity, not host credentials");
		installResult.ExitCode.Should().Be(1, because: "the installation surface uses the same boundary");
		runtime.DidNotReceive().Execute(Arg.Any<RuntimeOptions>());
		installer.DidNotReceive().Install(Arg.Any<InstallOperatorOptions>());
	}
}
