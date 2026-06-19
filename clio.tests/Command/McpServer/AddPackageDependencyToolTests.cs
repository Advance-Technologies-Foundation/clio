using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using ConsoleTables;
using FluentAssertions;
using FluentValidation.Results;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class AddPackageDependencyToolTests {

	private static McpServerToolAttribute GetToolAttribute() =>
		typeof(AddPackageDependencyTool)
			.GetMethod(nameof(AddPackageDependencyTool.AddPackageDependency))!
			.GetCustomAttribute<McpServerToolAttribute>()!;

	[Test]
	[Description("Advertises the stable add-package-dependency MCP tool name so coverage tracks the production contract.")]
	public void AddPackageDependency_ShouldAdvertiseStableToolName() {
		// Arrange

		// Act
		string toolName = AddPackageDependencyTool.AddPackageDependencyToolName;

		// Assert
		toolName.Should().Be("add-package-dependency",
			because: "the add-package-dependency MCP contract must keep a stable tool identifier");
	}

	[Test]
	[Description("Marks the add-package-dependency tool as idempotent and non-destructive because re-adding a dependency is a no-op.")]
	[Ignore("ENG-90312 Phase 2: add-package-dependency folded into clio-run; the per-method [McpServerTool] safety flags now live on clio-run itself, so the legacy attribute reflection no longer applies.")]
	public void AddPackageDependency_ShouldExposeIdempotentNonDestructiveMetadata() {
		// Arrange
		McpServerToolAttribute attribute = GetToolAttribute();

		// Act
		(bool idempotent, bool destructive, bool readOnly) = (attribute.Idempotent, attribute.Destructive, attribute.ReadOnly);

		// Assert
		idempotent.Should().BeTrue(because: "adding an already-present dependency is a no-op");
		destructive.Should().BeFalse(because: "adding a dependency does not delete or replace existing data");
		readOnly.Should().BeFalse(because: "the tool mutates the package dependency list");
	}

	[Test]
	[Description("Keeps the kebab-case JSON argument names that form the stable MCP contract.")]
	public void AddPackageDependencyArgs_ShouldExposeKebabCaseJsonNames() {
		// Arrange
		string EnvName(string property) => typeof(AddPackageDependencyRunArgs)
			.GetProperty(property)!.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name;
		string DepName(string property) => typeof(PackageDependencyArg)
			.GetProperty(property)!.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name;

		// Act
		(string environment, string package, string dependencies) =
			(EnvName(nameof(AddPackageDependencyRunArgs.EnvironmentName)),
				EnvName(nameof(AddPackageDependencyRunArgs.PackageName)),
				EnvName(nameof(AddPackageDependencyRunArgs.Dependencies)));

		// Assert
		environment.Should().Be("environment-name", because: "the environment argument name is part of the stable contract");
		package.Should().Be("package-name", because: "the package argument name is part of the stable contract");
		dependencies.Should().Be("dependencies", because: "the dependencies argument name is part of the stable contract");
		DepName(nameof(PackageDependencyArg.Name)).Should().Be("name", because: "the dependency name field is part of the contract");
		DepName(nameof(PackageDependencyArg.Version)).Should().Be("version", because: "the dependency version field is part of the contract");
	}

	[Test]
	[Description("Resolves the environment-aware command and composes each dependency into a name or name:version token.")]
	public void AddPackageDependency_ShouldResolveCommandAndMapArguments() {
		// Arrange
		TestLogger logger = new();
		StubAddPackageDependencyCommand resolvedCommand = new(exitCode: 0);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<AddPackageDependencyCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		AddPackageDependencyTool tool = new(resolvedCommand, logger, commandResolver);
		AddPackageDependencyRunArgs args = new("dev", "MyApp", [
			new PackageDependencyArg("CrtLeadOppMgmtApp", "8.2.1.123"),
			new PackageDependencyArg("CrtCase", null)
		]);

		// Act
		CommandExecutionResult result = tool.AddPackageDependency(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "the resolved command reported success");
		commandResolver.Received(1).Resolve<AddPackageDependencyCommand>(Arg.Is<AddPackageDependencyOptions>(options =>
			options.Environment == "dev" &&
			options.PackageName == "MyApp" &&
			options.Dependencies.SequenceEqual(new[] { "CrtLeadOppMgmtApp:8.2.1.123", "CrtCase" })));
	}

	#region Test doubles

	private sealed class StubAddPackageDependencyCommand(int exitCode)
		: AddPackageDependencyCommand(Substitute.For<IPackageDependencyManager>(), new EnvironmentSettings()) {
		public override int Execute(AddPackageDependencyOptions options) => exitCode;
	}

	private sealed class TestLogger : ILogger {
		List<LogMessage> ILogger.LogMessages => LogMessages;
		bool ILogger.PreserveMessages { get; set; }
		internal List<LogMessage> LogMessages { get; } = [];

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
		public void PrintTable(ConsoleTable table) { }
		public void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors) { }

		private sealed class NoopScope : IDisposable {
			public void Dispose() { }
		}
	}

	#endregion

}
