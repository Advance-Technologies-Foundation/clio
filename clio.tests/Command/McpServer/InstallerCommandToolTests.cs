using System.Collections.Generic;
using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using ConsoleTables;
using FluentAssertions;
using FluentValidation.Results;
using k8s;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class InstallerCommandToolTests
{
	private const string ScheduledMaintenanceMessage =
		"Infrastructure temporarily unavailable due to scheduled maintenance. Please try again later.";

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable deploy-creatio MCP tool name so unit and end-to-end tests track the same contract identifier.")]
	public void DeployCreatio_Should_Advertise_Stable_Tool_Name()
	{
		// Arrange

		// Act
		string toolName = InstallerCommandTool.DeployCreatioToolName;

		// Assert
		toolName.Should().Be("deploy-creatio",
			because: "the MCP contract should keep a stable deploy-creatio tool name");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks deploy-creatio as destructive and embeds the required preflight guidance in the MCP description.")]
	public void DeployCreatio_Should_Expose_Destructive_Metadata_And_Preflight_Guidance()
	{
		// Arrange
		McpServerToolAttribute attribute = GetDeployCreatioAttribute();
		System.ComponentModel.DescriptionAttribute description = GetDeployCreatioDescription();

		// Act
		bool destructive = attribute.Destructive;
		string text = description.Description;

		// Assert
		destructive.Should().BeTrue(
			because: "deploy-creatio changes infrastructure and must be advertised as destructive");
		text.Should().Contain("assert-infrastructure",
			because: "the tool description should direct agents to run the full infrastructure assertion first");
		text.Should().Contain("show-passing-infrastructure",
			because: "the tool description should direct agents to run passing-infrastructure discovery before deployment");
		text.Should().Contain("find-empty-iis-port",
			because: "the tool description should tell agents how to choose a safe local IIS sitePort");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the reduced deploy-creatio MCP argument contract into PfInstallerOptions and forces silent mode for non-interactive MCP execution.")]
	public void DeployCreatio_Should_Map_Allowed_Arguments_And_Force_Silent_Mode()
	{
		// Arrange
		TestLogger logger = new();
		FakeInstallerCommand command = new(logger, exitCode: 7);
		InstallerCommandTool tool = new(command, logger);
		DeployCreatioArgs args = new(
			SiteName: "creatio-app",
			ZipFile: @"C:\temp\creatio.zip",
			SitePort: 8080,
			DbServerName: "sql-main",
			RedisServerName: "redis-main");

		// Act
		CommandExecutionResult result = tool.DeployCreatio(args);

		// Assert
		result.ExitCode.Should().Be(7,
			because: "the MCP tool should return the real command execution result instead of a stubbed maintenance response");
		command.ReceivedOptions.Should().NotBeNull(
			because: "the deploy-creatio MCP tool should execute the installer command with mapped options");
		command.ReceivedOptions!.Environment.Should().BeNull(
			because: "the reduced MCP tool contract should no longer accept an environment argument");
		command.ReceivedOptions.SiteName.Should().Be("creatio-app",
			because: "site-name should map directly into PfInstallerOptions");
		command.ReceivedOptions.ZipFile.Should().Be(@"C:\temp\creatio.zip",
			because: "zip-file should map directly into PfInstallerOptions");
		command.ReceivedOptions.SitePort.Should().Be(8080,
			because: "site-port should map directly into PfInstallerOptions");
		command.ReceivedOptions.DbServerName.Should().Be("sql-main",
			because: "local DB server selection should be forwarded when provided");
		command.ReceivedOptions.RedisServerName.Should().Be("redis-main",
			because: "local Redis server selection should be forwarded when provided");
		command.ReceivedOptions.RedisDb.Should().Be(-1,
			because: "the reduced MCP contract should keep automatic Redis DB detection");
		command.ReceivedOptions.DisableResetPassword.Should().BeTrue(
			because: "the MCP wrapper should preserve the CLI default and disable forced password reset unless explicitly changed in code");
		command.ReceivedOptions.DB.Should().BeNull(
			because: "the reduced MCP contract should let the installer detect the database type from the build");
		command.ReceivedOptions.DropIfExists.Should().BeTrue(
			because: "the reduced MCP contract should use drop-if-exists overrides");
		command.ReceivedOptions.DeploymentMethod.Should().BeNull(
			because: "the reduced MCP contract should no longer expose deployment-method overrides");
		command.ReceivedOptions.AutoRun.Should().BeTrue(
			because: "the MCP wrapper should still apply the CLI auto-run default even though the argument is no longer caller-configurable");
		command.ReceivedOptions.Uri.Should().BeNull(
			because: "the reduced MCP contract should no longer expose inherited auth arguments");
		command.ReceivedOptions.IsSilent.Should().BeTrue(
			because: "MCP execution must never block on interactive console input");
		result.Output.Should().ContainSingle(
			message => (string?)message.Value == "real installer command path",
			because: "the command-backed MCP result should preserve actual execution logs");
		result.Output.Should().NotContain(
			message => (string?)message.Value == ScheduledMaintenanceMessage,
			because: "the maintenance stub must be removed so agents receive real command output");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps db-server-name optional and limits the deploy-creatio MCP argument type to the five approved fields.")]
	public void DeployCreatio_Should_Keep_DbServerName_Optional_And_Expose_Only_Approved_Fields()
	{
		// Arrange
		TestLogger logger = new();
		FakeInstallerCommand command = new(logger, exitCode: 0);
		InstallerCommandTool tool = new(command, logger);
		DeployCreatioArgs args = new(
			SiteName: "creatio-app",
			ZipFile: @"C:\temp\creatio.zip",
			SitePort: 5000,
			DbServerName: null,
			RedisServerName: null);

		// Act
		tool.DeployCreatio(args);

		// Assert
		command.ReceivedOptions.Should().NotBeNull(
			because: "the deploy-creatio tool should still execute when db-server-name is omitted");
		command.ReceivedOptions!.DbServerName.Should().BeNull(
			because: "db-server-name must remain optional so Kubernetes can stay the default deployment path");
		command.ReceivedOptions.RedisDb.Should().Be(-1,
			because: "redis-db should default to auto-detection when omitted");
		typeof(DeployCreatioArgs).GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
			["SiteName", "ZipFile", "SitePort", "DbServerName", "RedisServerName"],
			because: "the MCP deploy-creatio argument type should expose only the five approved arguments");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for deploy-creatio tells the agent to run assert-infrastructure first and show-passing-infrastructure second before deployment.")]
	public void DeployCreatioPrompt_Should_Mention_Preflight_Order()
	{
		// Arrange

		// Act
		string prompt = DeployCreatioPrompt.Prompt("site-a", @"C:\temp\creatio.zip", 8080);

		// Assert
		prompt.Should().Contain("assert-infrastructure",
			because: "the prompt should direct the agent to inspect full infrastructure before deployment");
		prompt.Should().Contain("show-passing-infrastructure",
			because: "the prompt should direct the agent to retrieve deployable recommendations before deployment");
		prompt.Should().Contain("find-empty-iis-port",
			because: "the prompt should direct the agent to discover a safe local IIS port when sitePort selection matters");
		prompt.Should().Contain("deploy-creatio",
			because: "the prompt should conclude with the actual deployment call");
	}

	private static McpServerToolAttribute GetDeployCreatioAttribute()
	{
		return (McpServerToolAttribute)typeof(InstallerCommandTool)
			.GetMethod(nameof(InstallerCommandTool.DeployCreatio))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();
	}

	private static System.ComponentModel.DescriptionAttribute GetDeployCreatioDescription()
	{
		return (System.ComponentModel.DescriptionAttribute)typeof(InstallerCommandTool)
			.GetMethod(nameof(InstallerCommandTool.DeployCreatio))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Single();
	}

	private sealed class FakeInstallerCommand : InstallerCommand
	{
		private readonly TestLogger _logger;
		private readonly int _exitCode;

		public FakeInstallerCommand(TestLogger logger, int exitCode)
			: base(Substitute.For<ICreatioInstallerService>(), logger, Substitute.For<IKubernetes>())
		{
			_logger = logger;
			_exitCode = exitCode;
		}

		public PfInstallerOptions? ReceivedOptions { get; private set; }

		public override int Execute(PfInstallerOptions options)
		{
			ReceivedOptions = options;
			_logger.WriteInfo("real installer command path");
			return _exitCode;
		}
	}

	private sealed class TestLogger : ILogger
	{
		List<LogMessage> ILogger.LogMessages => LogMessages;
		bool ILogger.PreserveMessages { get; set; }
		internal List<LogMessage> LogMessages { get; } = [];

		public void ClearMessages() => LogMessages.Clear();
		public void Start(string logFilePath = "") { }
		public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) { }
		public void StartWithStream() { }
		public void Stop() { }
		public void Write(string value) => LogMessages.Add(new UndecoratedMessage(value));
		public void WriteLine() => LogMessages.Add(new UndecoratedMessage(string.Empty));
		public void WriteLine(string value) => LogMessages.Add(new UndecoratedMessage(value));
		public void WriteWarning(string value) => LogMessages.Add(new WarningMessage(value));
		public void WriteError(string value) => LogMessages.Add(new ErrorMessage(value));
		public void WriteInfo(string value) => LogMessages.Add(new InfoMessage(value));
		public void WriteDebug(string value) => LogMessages.Add(new DebugMessage(value));
		public void PrintTable(ConsoleTable table) { }
		public void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors) { }
	}
}
