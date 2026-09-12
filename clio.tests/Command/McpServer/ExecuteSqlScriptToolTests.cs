using System;
using System.Linq;
using System.Reflection;
using Clio.Command.McpServer.Tools;
using Clio.Command.SqlScriptCommand;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>Contract and per-environment mapping tests for SQL exports.</summary>
[Property("Module", "McpServer")]
public sealed class ExecuteSqlScriptToolTests : BaseClioModuleTests {
	private IToolCommandResolver _resolver;
	private SqlScriptCommand _resolved;
	private ExecuteSqlScriptTool _tool;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolved = Substitute.For<SqlScriptCommand>(Substitute.For<IApplicationClient>(),
			EnvironmentSettings, Substitute.For<ISqlScriptExecutor>(), Substitute.For<ILogger>());
		_resolver.Resolve<SqlScriptCommand>(Arg.Any<EnvironmentOptions>()).Returns(_resolved);
		_resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<IRequiredPackageChecker>());
		services.AddSingleton(_resolver);
		services.AddTransient<ExecuteSqlScriptTool>();
	}

	public override void Setup() {
		base.Setup();
		_tool = Container.GetRequiredService<ExecuteSqlScriptTool>();
	}

	public override void TearDown() {
		_resolver.ClearReceivedCalls();
		_resolved.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase(null, null, false)]
	[TestCase("table", "result.txt", false)]
	[TestCase("json", null, false)]
	[TestCase("json", "result.json", true)]
	[TestCase("csv", "result.csv", true)]
	[TestCase("xlsx", "result.xlsx", true)]
	[TestCase("JSON", "result.json", false)]
	[Description("Maps format, destination and silent options into a command resolved for the requested environment.")]
	public void Execute_ShouldMapOptions_WhenRequestIsValid(string view, string destination, bool silent) {
		// Arrange
		ExecuteSqlScriptArgs args = new("sql-env", Script: "SELECT 1", View: view,
			DestinationPath: destination, Silent: silent);

		// Act
		CommandExecutionResult result = _tool.Execute(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "the environment-scoped command succeeds");
		ExecuteSqlScriptOptions options = _resolved.ReceivedCalls().Single(call =>
			call.GetMethodInfo().Name == nameof(SqlScriptCommand.Execute)).GetArguments()[0]
			as ExecuteSqlScriptOptions;
		options.Should().BeEquivalentTo(new ExecuteSqlScriptOptions {
			Environment = "sql-env", Script = "SELECT 1", ViewType = view?.ToLowerInvariant() ?? "table",
			DestPath = destination, IsSilent = silent
		}, because: "every output option must reach the resolved command without CLI parser defaults");
	}

	[TestCase(null)]
	[TestCase(" ")]
	[Description("Maps a SQL input file while preserving default table output and non-silent execution.")]
	public void Execute_ShouldMapFile_WhenScriptIsOmitted(string script) {
		// Arrange
		ExecuteSqlScriptArgs args = new("sql-env", Script: script, File: "query.sql");

		// Act
		CommandExecutionResult result = _tool.Execute(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "a file is a supported alternative SQL input");
		_resolved.ReceivedCalls().Should().ContainSingle(call => call.GetArguments().OfType<ExecuteSqlScriptOptions>()
			.Any(options => options.File == "query.sql" && options.Script == null && options.ViewType == "table"
				&& !options.IsSilent), because: "file input and omitted output defaults must be forwarded");
	}

	[TestCase(null, null, "table", null, "script or file")]
	[TestCase("SELECT 1", "query.sql", "json", null, "script or file")]
	[TestCase("SELECT 1", null, "yaml", null, "view")]
	[TestCase("SELECT 1", null, "csv", null, "destination-path")]
	[TestCase("SELECT 1", null, "xlsx", " ", "destination-path")]
	[Description("Rejects ambiguous or incomplete arguments before command resolution or SQL execution.")]
	public void Execute_ShouldRejectBeforeResolution_WhenArgumentsAreInvalid(
		string script, string file, string view, string destination, string diagnostic) {
		// Arrange
		ExecuteSqlScriptArgs args = new("sql-env", script, file, view, destination);

		// Act
		CommandExecutionResult result = _tool.Execute(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "invalid arguments are actionable input errors");
		result.Output.Should().Contain(message => message is ErrorMessage && message.Value.ToString().Contains(diagnostic),
			because: "the result must name the invalid argument");
		_resolver.ReceivedCalls().Should().BeEmpty(because: "invalid output options must never reach Creatio");
	}

	[Test]
	[Description("Advertises SQL execution as destructive with a clear 40-character display limit and full-file export guidance.")]
	public void Execute_ShouldAdvertiseSafetyAndExportGuidance_WhenDiscovered() {
		// Arrange
		MethodInfo method = typeof(ExecuteSqlScriptTool).GetMethod(nameof(ExecuteSqlScriptTool.Execute));

		// Act
		McpServerToolAttribute annotation = method.GetCustomAttribute<McpServerToolAttribute>();
		string description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description;

		// Assert
		annotation.Name.Should().Be(ExecuteSqlScriptTool.ToolName, because: "discovery uses the stable tool name");
		annotation.Destructive.Should().BeTrue(because: "arbitrary SQL and file exports can change data");
		annotation.ReadOnly.Should().BeFalse(because: "the tool accepts more than SELECT queries");
		annotation.Idempotent.Should().BeFalse(because: "repeating SQL can apply a write twice");
		description.Should().ContainAll(["40", "destination-path", "view=json", "silent=true"],
			because: "agents need the complete-content export route before executing");
	}
}
