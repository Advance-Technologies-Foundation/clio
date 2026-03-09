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
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the full deploy-creatio MCP argument contract into PfInstallerOptions and forces silent mode for non-interactive MCP execution.")]
	public void DeployCreatio_Should_Map_All_Arguments_And_Force_Silent_Mode()
	{
		// Arrange
		TestLogger logger = new();
		FakeInstallerCommand command = new(logger, exitCode: 7);
		InstallerCommandTool tool = new(command, logger);
		DeployCreatioArgs args = new(
			Environment: "sandbox",
			SiteName: "creatio-app",
			ZipFile: @"C:\temp\creatio.zip",
			SitePort: 8080,
			Db: "mssql",
			DbServerName: "sql-main",
			RedisServerName: "redis-main",
			RedisDb: 5,
			DropIfExists: true,
			DisableResetPassword: false,
			Platform: "net6",
			Product: "Studio",
			DeploymentMethod: "dotnet",
			NoIis: true,
			AppPath: @"C:\inetpub\creatio-app",
			UseHttps: true,
			CertificatePath: @"C:\certs\site.pfx",
			CertificatePassword: "pfx-secret",
			AutoRun: false,
			Uri: "https://sandbox.example",
			Login: "Supervisor",
			Password: "Password1!",
			ClientId: "client-id",
			ClientSecret: "client-secret",
			AuthAppUri: "https://auth.example",
			IsNetCore: true);

		// Act
		CommandExecutionResult result = tool.DeployCreatio(args);

		// Assert
		result.ExitCode.Should().Be(7,
			because: "the MCP tool should return the real command execution result instead of a stubbed maintenance response");
		command.ReceivedOptions.Should().NotBeNull(
			because: "the deploy-creatio MCP tool should execute the installer command with mapped options");
		command.ReceivedOptions!.Environment.Should().Be("sandbox",
			because: "registered environment selection should flow through to the underlying command");
		command.ReceivedOptions.SiteName.Should().Be("creatio-app",
			because: "site-name should map directly into PfInstallerOptions");
		command.ReceivedOptions.ZipFile.Should().Be(@"C:\temp\creatio.zip",
			because: "zip-file should map directly into PfInstallerOptions");
		command.ReceivedOptions.SitePort.Should().Be(8080,
			because: "site-port should map directly into PfInstallerOptions");
		command.ReceivedOptions.DB.Should().Be("mssql",
			because: "db should preserve the caller-selected database engine");
		command.ReceivedOptions.DbServerName.Should().Be("sql-main",
			because: "local DB server selection should be forwarded when provided");
		command.ReceivedOptions.RedisServerName.Should().Be("redis-main",
			because: "local Redis server selection should be forwarded when provided");
		command.ReceivedOptions.RedisDb.Should().Be(5,
			because: "the selected Redis database index should be forwarded when provided");
		command.ReceivedOptions.DropIfExists.Should().BeTrue(
			because: "drop-if-exists should preserve explicit destructive intent from the caller");
		command.ReceivedOptions.DisableResetPassword.Should().BeFalse(
			because: "disable-reset-password should preserve explicit overrides");
		command.ReceivedOptions.Platform.Should().Be("net6",
			because: "platform should map directly into PfInstallerOptions");
		command.ReceivedOptions.Product.Should().Be("Studio",
			because: "product should map directly into PfInstallerOptions");
		command.ReceivedOptions.DeploymentMethod.Should().Be("dotnet",
			because: "deployment method should map directly into PfInstallerOptions");
		command.ReceivedOptions.NoIIS.Should().BeTrue(
			because: "no-iis should map directly into PfInstallerOptions");
		command.ReceivedOptions.AppPath.Should().Be(@"C:\inetpub\creatio-app",
			because: "app-path should map directly into PfInstallerOptions");
		command.ReceivedOptions.UseHttps.Should().BeTrue(
			because: "use-https should map directly into PfInstallerOptions");
		command.ReceivedOptions.CertificatePath.Should().Be(@"C:\certs\site.pfx",
			because: "certificate-path should map directly into PfInstallerOptions");
		command.ReceivedOptions.CertificatePassword.Should().Be("pfx-secret",
			because: "certificate-password should map directly into PfInstallerOptions");
		command.ReceivedOptions.AutoRun.Should().BeFalse(
			because: "auto-run should preserve explicit overrides");
		command.ReceivedOptions.Uri.Should().Be("https://sandbox.example",
			because: "inherited URI auth settings should remain available through MCP");
		command.ReceivedOptions.Login.Should().Be("Supervisor",
			because: "inherited login settings should remain available through MCP");
		command.ReceivedOptions.Password.Should().Be("Password1!",
			because: "inherited password settings should remain available through MCP");
		command.ReceivedOptions.ClientId.Should().Be("client-id",
			because: "inherited OAuth client ID should remain available through MCP");
		command.ReceivedOptions.ClientSecret.Should().Be("client-secret",
			because: "inherited OAuth client secret should remain available through MCP");
		command.ReceivedOptions.AuthAppUri.Should().Be("https://auth.example",
			because: "inherited OAuth app URI should remain available through MCP");
		command.ReceivedOptions.IsNetCore.Should().BeTrue(
			because: "inherited runtime selection should remain available through MCP");
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
	[Description("Keeps db-server-name optional and applies CLI defaults that preserve the Kubernetes-first deployment path when local selections are omitted.")]
	public void DeployCreatio_Should_Keep_DbServerName_Optional_And_Apply_Cli_Defaults()
	{
		// Arrange
		TestLogger logger = new();
		FakeInstallerCommand command = new(logger, exitCode: 0);
		InstallerCommandTool tool = new(command, logger);
		DeployCreatioArgs args = new(
			Environment: null,
			SiteName: "creatio-app",
			ZipFile: @"C:\temp\creatio.zip",
			SitePort: 5000,
			Db: null,
			DbServerName: null,
			RedisServerName: null,
			RedisDb: null,
			DropIfExists: null,
			DisableResetPassword: null,
			Platform: null,
			Product: null,
			DeploymentMethod: null,
			NoIis: null,
			AppPath: null,
			UseHttps: null,
			CertificatePath: null,
			CertificatePassword: null,
			AutoRun: null,
			Uri: null,
			Login: null,
			Password: null,
			ClientId: null,
			ClientSecret: null,
			AuthAppUri: null,
			IsNetCore: null);

		// Act
		tool.DeployCreatio(args);

		// Assert
		command.ReceivedOptions.Should().NotBeNull(
			because: "the deploy-creatio tool should still execute when db-server-name is omitted");
		command.ReceivedOptions!.DbServerName.Should().BeNull(
			because: "db-server-name must remain optional so Kubernetes can stay the default deployment path");
		command.ReceivedOptions.RedisDb.Should().Be(-1,
			because: "redis-db should default to auto-detection when omitted");
		command.ReceivedOptions.DisableResetPassword.Should().BeTrue(
			because: "disable-reset-password should keep the CLI default when omitted");
		command.ReceivedOptions.DeploymentMethod.Should().Be("auto",
			because: "deployment method should keep the CLI default when omitted");
		command.ReceivedOptions.NoIIS.Should().BeFalse(
			because: "no-iis should default to false when omitted");
		command.ReceivedOptions.UseHttps.Should().BeFalse(
			because: "use-https should default to false when omitted");
		command.ReceivedOptions.AutoRun.Should().BeTrue(
			because: "auto-run should keep the CLI default when omitted");
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
