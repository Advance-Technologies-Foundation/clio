using System;
using Clio.Command;
using Clio.Common;
using Clio.Common.OperatorBootstrap;
using Clio.Common.RuntimeAttachment;
using CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture, Category("Unit"), Property("Module", "Command")]
public class RuntimeCommandRoutingTests {
	[Test]
	[Description("Nested attach parses and routes all workspace and runtime identity options.")]
	public void AttachRoutesToAttachmentService() {
		// Arrange
		var attachment = Substitute.For<IRuntimeAttachmentService>();
		using var container = new ServiceCollection().AddSingleton(attachment)
			.AddSingleton(Substitute.For<IOperatorRuntimeService>()).AddSingleton(Substitute.For<ILogger>())
			.AddTransient<RuntimeCommand>().BuildServiceProvider();
		using var parser = new Parser();
		var result = parser.ParseArguments(["runtime", "attach", "demo", "--workspace", "workspace", "--ssh-alias", "demo-ssh", "--context", "cluster"], typeof(RuntimeOptions));
		// Act
		int code = result.MapResult(options => container.GetRequiredService<RuntimeCommand>().Execute((RuntimeOptions)options), _ => -1);
		// Assert
		code.Should().Be(0, because: "nested attach is the supported command form");
		attachment.Received(1).Attach(Arg.Is<AttachOptions>(o => o.Environment == "demo" && o.Context == "cluster" && o.Workspace == "workspace" && o.SshAlias == "demo-ssh"));
	}

	[Test]
	[Description("Nested detach uses its receipt and does not require a cluster context.")]
	public void DetachNeedsOnlyWorkspace() {
		// Arrange
		var attachment = Substitute.For<IRuntimeAttachmentService>();
		using var container = new ServiceCollection().AddSingleton(attachment)
			.AddSingleton(Substitute.For<IOperatorRuntimeService>()).AddSingleton(Substitute.For<ILogger>())
			.AddTransient<RuntimeCommand>().BuildServiceProvider();
		using var parser = new Parser();
		var result = parser.ParseArguments(["runtime", "detach", "--workspace", "workspace"], typeof(RuntimeOptions));
		// Act
		int code = result.MapResult(options => container.GetRequiredService<RuntimeCommand>().Execute((RuntimeOptions)options), _ => -1);
		// Assert
		code.Should().Be(0, because: "detach gets its original cluster from the receipt");
		attachment.Received(1).Detach("workspace");
	}

	[TestCase(typeof(RuntimeOptions))]
	[TestCase(typeof(InstallOperatorOptions))]
	[Description("All runtime entry points are rejected by the shared gate when disabled.")]
	public void RuntimeEntryPointsAreGated(Type optionsType) {
		// Arrange
		var flags = Substitute.For<IFeatureToggleService>();
		flags.IsEnabled(optionsType).Returns(false);
		// Act
		bool blocked = Clio.Program.TryGetDisabledFeatureName(Activator.CreateInstance(optionsType), flags, out string key);
		// Assert
		blocked.Should().BeTrue(because: "runtime features require explicit opt-in");
		key.Should().Be("runtime", because: "all runtime commands share one flag");
	}
}
