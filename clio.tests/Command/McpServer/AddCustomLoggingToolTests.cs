using System;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using FluentValidation;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class AddCustomLoggingToolTests {
	[Test]
	[Description("Publishes stable mutating metadata for the add-custom-logging MCP contract.")]
	public void AddCustomLogging_ShouldExposeStableMetadata_WhenToolIsReflected() {
		// Arrange
		McpServerToolAttribute attribute = typeof(AddCustomLoggingTool)
			.GetMethod(nameof(AddCustomLoggingTool.AddCustomLogging))!
			.GetCustomAttribute<McpServerToolAttribute>()!;

		// Act
		(string name, bool destructive, bool idempotent, bool readOnly) =
			(attribute.Name, attribute.Destructive, attribute.Idempotent, attribute.ReadOnly);

		// Assert
		name.Should().Be("add-custom-logging", because: "tool discovery depends on the stable command-aligned name");
		destructive.Should().BeTrue(because: "the tool replaces local configuration files and may restart Creatio");
		idempotent.Should().BeFalse(because: "the optional restart branch has a repeatable external side effect");
		readOnly.Should().BeFalse(because: "the tool mutates local NLog configuration");
	}

	[Test]
	[Description("Keeps every MCP argument in kebab-case and maps it to environment-aware command options.")]
	public void AddCustomLogging_ShouldMapKebabCaseArguments_WhenAllArgumentsAreProvided() {
		// Arrange
		StubCommand fallbackCommand = new();
		StubCommand resolvedCommand = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<AddCustomLoggingCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		AddCustomLoggingTool tool = new(fallbackCommand, new TestLogger(), resolver);
		AddCustomLoggingArgs args = new("dev", "MyPackage", "Debug", "custom.log", true);

		// Act
		CommandExecutionResult result = tool.AddCustomLogging(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "the environment-resolved command completed successfully");
		resolvedCommand.Options.Should().BeEquivalentTo(new AddCustomLoggingOptions {
			Environment = "dev",
			PackageName = "MyPackage",
			MinLevel = "Debug",
			FileName = "custom.log",
			RestartEnvironment = true
		}, because: "the MCP adapter must preserve every caller-controlled command option");
		JsonName(nameof(AddCustomLoggingArgs.EnvironmentName)).Should().Be("environment-name",
			because: "environment-name is part of the stable MCP contract");
		JsonName(nameof(AddCustomLoggingArgs.PackageName)).Should().Be("package-name",
			because: "package-name is part of the stable MCP contract");
		JsonName(nameof(AddCustomLoggingArgs.MinLevel)).Should().Be("min-level",
			because: "min-level must follow the repository kebab-case rule");
		JsonName(nameof(AddCustomLoggingArgs.FileName)).Should().Be("file-name",
			because: "file-name must follow the repository kebab-case rule");
		JsonName(nameof(AddCustomLoggingArgs.RestartEnvironment)).Should().Be("restart-environment",
			because: "restart-environment must follow the repository kebab-case rule");
	}

	[Test]
	[Description("Applies command defaults when optional MCP arguments are omitted.")]
	public void AddCustomLogging_ShouldApplyDefaults_WhenOptionalArgumentsAreOmitted() {
		// Arrange
		StubCommand fallbackCommand = new();
		StubCommand resolvedCommand = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<AddCustomLoggingCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		AddCustomLoggingTool tool = new(fallbackCommand, new TestLogger(), resolver);

		// Act
		CommandExecutionResult result = tool.AddCustomLogging(new AddCustomLoggingArgs("dev", "MyPackage"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the environment-resolved command completed successfully");
		resolvedCommand.Options.MinLevel.Should().Be("Info", because: "Info is the documented default minimum level");
		resolvedCommand.Options.FileName.Should().BeNull(because: "the command derives the default file name");
		resolvedCommand.Options.RestartEnvironment.Should().BeFalse(because: "restart must remain explicit");
	}

	private static string JsonName(string propertyName) => typeof(AddCustomLoggingArgs)
		.GetProperty(propertyName)!.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name;

	private sealed class StubCommand : AddCustomLoggingCommand {
		public StubCommand() : base(
			Substitute.For<IValidator<AddCustomLoggingOptions>>(),
			Substitute.For<Clio.UserEnvironment.ISettingsRepository>(),
			Substitute.For<ICustomLoggingConfigurator>(),
			Substitute.For<IEnvironmentRestartService>(),
			Substitute.For<ILogger>()) { }

		internal AddCustomLoggingOptions Options { get; private set; }

		public override int Execute(AddCustomLoggingOptions options) {
			Options = options;
			return 0;
		}
	}

	private sealed class TestLogger : ILogger {
		List<LogMessage> ILogger.LogMessages => LogMessages;
		bool ILogger.PreserveMessages { get; set; }
		private List<LogMessage> LogMessages { get; } = [];
		public void ClearMessages() => LogMessages.Clear();
		public IDisposable BeginScopedFileSink(string logFilePath) => new NoopScope();
		public void Start(string logFilePath = "") { }
		public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) { }
		public void StartWithStream() { }
		public void Stop() { }
		public void Write(string value) { }
		public void WriteLine() { }
		public void WriteLine(string value) { }
		public void WriteWarning(string value) { }
		public void WriteError(string value) { }
		public void WriteInfo(string value) { }
		public void WriteDebug(string value) { }
		public void PrintTable(ConsoleTables.ConsoleTable table) { }
		public void PrintValidationFailureErrors(IEnumerable<FluentValidation.Results.ValidationFailure> errors) { }
		private sealed class NoopScope : IDisposable { public void Dispose() { } }
	}
}
