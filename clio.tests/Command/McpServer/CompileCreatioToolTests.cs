using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class CompileCreatioToolTests
{
	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for Creatio compilation.")]
	public void CompileCreatio_Should_Advertise_Stable_Tool_Name()
	{
		// Arrange

		// Act
		string toolName = CompileCreatioTool.CompileCreatioToolName;

		// Assert
		toolName.Should().Be("compile-creatio",
			because: "clients and tests should share one stable tool name for full and package-only compilation");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves CompileConfigurationCommand with All=true when package-name is omitted.")]
	public void CompileCreatio_Should_Use_Full_Compilation_When_Package_Name_Is_Omitted()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		FakeCompileConfigurationCommand resolvedCommand = new();
		commandResolver.Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>())
			.Returns(resolvedCommand);
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver);

		try
		{
			// Act
			CommandExecutionResult result = tool.CompileCreatio(new CompileCreatioArgs("sandbox", null));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "omitting package-name should invoke the full compilation path");
			commandResolver.Received(1).Resolve<CompileConfigurationCommand>(Arg.Is<CompileConfigurationOptions>(options =>
				options.Environment == "sandbox" &&
				options.All));
			commandResolver.DidNotReceive().Resolve<CompilePackageCommand>(Arg.Any<CompilePackageOptions>());
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved full compile command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.All.Should().BeTrue(
				because: "the MCP tool should set All=true for the full compilation path");
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves CompilePackageCommand with the exact package-name when package-only compilation is requested.")]
	public void CompileCreatio_Should_Use_Package_Compilation_When_Package_Name_Is_Provided()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		FakeCompilePackageCommand resolvedCommand = new();
		commandResolver.Resolve<CompilePackageCommand>(Arg.Any<CompilePackageOptions>())
			.Returns(resolvedCommand);
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver);

		try
		{
			// Act
			CommandExecutionResult result = tool.CompileCreatio(new CompileCreatioArgs("sandbox", "MyPackage"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "providing package-name should invoke the package-only compilation path");
			commandResolver.Received(1).Resolve<CompilePackageCommand>(Arg.Is<CompilePackageOptions>(options =>
				options.Environment == "sandbox" &&
				options.PackageName == "MyPackage"));
			commandResolver.DidNotReceive().Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>());
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved package compile command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.PackageName.Should().Be("MyPackage",
				because: "the MCP tool should preserve the exact requested package name");
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects comma-separated package lists so the MCP contract remains limited to one package.")]
	public void CompileCreatio_Should_Reject_Comma_Separated_Package_Names()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver);

		try
		{
			// Act
			CommandExecutionResult result = tool.CompileCreatio(new CompileCreatioArgs("sandbox", "PkgA,PkgB"));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "the MCP contract allows only one package name");
			result.Output.Should().Contain(message =>
				message.GetType() == typeof(ErrorMessage) &&
				Equals(message.Value, "`package-name` must contain exactly one package name. Comma-separated package lists are not supported by `compile-creatio`."),
				because: "the failure should explain why comma-separated package names are rejected");
			commandResolver.DidNotReceive().Resolve<CompilePackageCommand>(Arg.Any<CompilePackageOptions>());
			commandResolver.DidNotReceive().Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>());
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes non-read-only MCP metadata for compilation because the command mutates build state but is not destructive.")]
	public void CompileCreatio_Should_Expose_Expected_Mcp_Metadata()
	{
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CompileCreatioTool)
			.GetMethod(nameof(CompileCreatioTool.CompileCreatio))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act

		// Assert
		attribute.Name.Should().Be(CompileCreatioTool.CompileCreatioToolName,
			because: "the metadata should reuse the production tool-name constant");
		attribute.ReadOnly.Should().BeFalse(
			because: "compilation changes target environment build state");
		attribute.Destructive.Should().BeFalse(
			because: "compilation should not be classified as a destructive operation");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for compile-creatio references the exact tool name for both full and package-only compilation.")]
	public void CompileCreatioPrompt_Should_Reference_Exact_Tool_Name()
	{
		// Arrange

		// Act
		string fullPrompt = FsmAndCompilePrompt.CompileCreatio("sandbox");
		string packagePrompt = FsmAndCompilePrompt.CompileCreatio("sandbox", "MyPackage");

		// Assert
		fullPrompt.Should().Contain(CompileCreatioTool.CompileCreatioToolName,
			because: "the prompt should reference the exact MCP tool name for full compilation");
		fullPrompt.Should().Contain("--all",
			because: "the full compilation prompt should explain the corresponding CLI behavior");
		packagePrompt.Should().Contain("MyPackage",
			because: "the package compilation prompt should preserve the requested package name");
	}

	private sealed class FakeCompileConfigurationCommand : CompileConfigurationCommand
	{
		public CompileConfigurationOptions? CapturedOptions { get; private set; }

		public FakeCompileConfigurationCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<ATF.Repository.Providers.IDataProvider>(),
				Substitute.For<ILogger>())
		{
		}

		public override int Execute(CompileConfigurationOptions options)
		{
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeCompilePackageCommand : CompilePackageCommand
	{
		public CompilePackageOptions? CapturedOptions { get; private set; }

		public FakeCompilePackageCommand()
			: base(Substitute.For<Clio.Package.IPackageBuilder>(), Substitute.For<ILogger>())
		{
		}

		public override int Execute(CompilePackageOptions options)
		{
			CapturedOptions = options;
			return 0;
		}
	}
}
