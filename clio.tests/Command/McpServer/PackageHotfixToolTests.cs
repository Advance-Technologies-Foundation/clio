using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class PackageHotfixToolTests {

	[Test]
	[Description("UnlockForHotfix resolves command for requested environment and sets Enable=true.")]
	[Category("Unit")]
	public void UnlockForHotfix_Should_Resolve_Command_And_Set_Enable_True() {
		ConsoleLogger.Instance.ClearMessages();
		FakePackageHotFixCommand defaultCommand = new();
		FakePackageHotFixCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PackageHotFixCommand>(Arg.Any<PackageHotFixCommandOptions>()).Returns(resolvedCommand);
		PackageHotfixTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.UnlockForHotfix(new PackageHotfixArgs("CrtNUI", "dev"));

		result.ExitCode.Should().Be(0, because: "UnlockForHotfix should succeed");
		commandResolver.Received(1).Resolve<PackageHotFixCommand>(Arg.Is<PackageHotFixCommandOptions>(o =>
			o.PackageName == "CrtNUI" &&
			o.Enable == true &&
			o.Environment == "dev"));
		defaultCommand.CapturedOptions.Should().BeNull(because: "resolved command instance must be used");
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions!.PackageName.Should().Be("CrtNUI");
		resolvedCommand.CapturedOptions!.Enable.Should().BeTrue();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("FinishHotfix resolves command for requested environment and sets Enable=false.")]
	[Category("Unit")]
	public void FinishHotfix_Should_Resolve_Command_And_Set_Enable_False() {
		ConsoleLogger.Instance.ClearMessages();
		FakePackageHotFixCommand defaultCommand = new();
		FakePackageHotFixCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PackageHotFixCommand>(Arg.Any<PackageHotFixCommandOptions>()).Returns(resolvedCommand);
		PackageHotfixTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.FinishHotfix(new PackageHotfixArgs("CrtNUI", "dev"));

		result.ExitCode.Should().Be(0, because: "FinishHotfix should succeed");
		commandResolver.Received(1).Resolve<PackageHotFixCommand>(Arg.Is<PackageHotFixCommandOptions>(o =>
			o.PackageName == "CrtNUI" &&
			o.Enable == false &&
			o.Environment == "dev"));
		defaultCommand.CapturedOptions.Should().BeNull(because: "resolved command instance must be used");
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions!.PackageName.Should().Be("CrtNUI");
		resolvedCommand.CapturedOptions!.Enable.Should().BeFalse();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("FinishHotfix preserves the package name and environment from args.")]
	[Category("Unit")]
	public void FinishHotfix_Should_Forward_PackageName_And_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakePackageHotFixCommand defaultCommand = new();
		FakePackageHotFixCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PackageHotFixCommand>(Arg.Any<PackageHotFixCommandOptions>()).Returns(resolvedCommand);
		PackageHotfixTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		tool.FinishHotfix(new PackageHotfixArgs("MyPackage", "production"));

		resolvedCommand.CapturedOptions!.Environment.Should().Be("production",
			because: "environment name must be forwarded from args");
		resolvedCommand.CapturedOptions!.PackageName.Should().Be("MyPackage",
			because: "package name must be forwarded from args");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("UnlockForHotfix returns non-zero ExitCode when the command fails.")]
	[Category("Unit")]
	public void UnlockForHotfix_Should_Return_NonZero_ExitCode_When_Command_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakePackageHotFixCommand defaultCommand = new();
		FakePackageHotFixCommand resolvedCommand = new(exitCode: 1);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PackageHotFixCommand>(Arg.Any<PackageHotFixCommandOptions>()).Returns(resolvedCommand);
		PackageHotfixTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.UnlockForHotfix(new PackageHotfixArgs("CrtNUI", "dev"));

		result.ExitCode.Should().NotBe(0, because: "failed command must propagate non-zero exit code");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakePackageHotFixCommand : PackageHotFixCommand {
		private readonly int _exitCode;
		public PackageHotFixCommandOptions? CapturedOptions { get; private set; }

		public FakePackageHotFixCommand(int exitCode = 0)
			: base(Substitute.For<IPackageEditableMutator>(), new EnvironmentSettings()) {
			_exitCode = exitCode;
		}

		public override int Execute(PackageHotFixCommandOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}
}
