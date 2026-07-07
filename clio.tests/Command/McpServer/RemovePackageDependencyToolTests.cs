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
public sealed class RemovePackageDependencyToolTests {

	private static McpServerToolAttribute GetToolAttribute() =>
		typeof(RemovePackageDependencyTool)
			.GetMethod(nameof(RemovePackageDependencyTool.RemovePackageDependency))!
			.GetCustomAttribute<McpServerToolAttribute>()!;

	[Test]
	[Description("Advertises the stable remove-package-dependency MCP tool name so coverage tracks the production contract.")]
	public void RemovePackageDependency_ShouldAdvertiseStableToolName() {
		// Arrange

		// Act
		string toolName = RemovePackageDependencyTool.RemovePackageDependencyToolName;

		// Assert
		toolName.Should().Be("remove-package-dependency",
			because: "the remove-package-dependency MCP contract must keep a stable tool identifier");
	}

	[Test]
	[Description("Marks the remove-package-dependency tool as idempotent and destructive because it trims an existing dependency.")]
	public void RemovePackageDependency_ShouldExposeIdempotentDestructiveMetadata() {
		// Arrange
		McpServerToolAttribute attribute = GetToolAttribute();

		// Act
		(bool idempotent, bool destructive, bool readOnly) = (attribute.Idempotent, attribute.Destructive, attribute.ReadOnly);

		// Assert
		idempotent.Should().BeTrue(because: "removing an absent dependency is a no-op");
		destructive.Should().BeTrue(because: "removing a dependency drops an existing entry and can break the schema designer");
		readOnly.Should().BeFalse(because: "the tool mutates the package dependency list");
	}

	[Test]
	[Description("Keeps the kebab-case JSON argument names that form the stable MCP contract.")]
	public void RemovePackageDependencyArgs_ShouldExposeKebabCaseJsonNames() {
		// Arrange
		string ArgName(string property) => typeof(RemovePackageDependencyArgs)
			.GetProperty(property)!.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name;

		// Act
		(string environment, string package, string dependencies) =
			(ArgName(nameof(RemovePackageDependencyArgs.EnvironmentName)),
				ArgName(nameof(RemovePackageDependencyArgs.PackageName)),
				ArgName(nameof(RemovePackageDependencyArgs.Dependencies)));

		// Assert
		environment.Should().Be("environment-name", because: "the environment argument name is part of the stable contract");
		package.Should().Be("package-name", because: "the package argument name is part of the stable contract");
		dependencies.Should().Be("dependencies", because: "the dependencies argument name is part of the stable contract");
	}

	[Test]
	[Description("Resolves the environment-aware command and forwards the dependency names to remove.")]
	public void RemovePackageDependency_ShouldResolveCommandAndMapArguments() {
		// Arrange
		TestLogger logger = new();
		StubRemovePackageDependencyCommand resolvedCommand = new(exitCode: 0);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RemovePackageDependencyCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		RemovePackageDependencyTool tool = new(resolvedCommand, logger, commandResolver);
		RemovePackageDependencyArgs args = new("dev", "MyApp", ["CrtLeadOppMgmtApp", "CrtCase"]);

		// Act
		CommandExecutionResult result = tool.RemovePackageDependency(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "the resolved command reported success");
		commandResolver.Received(1).Resolve<RemovePackageDependencyCommand>(Arg.Is<RemovePackageDependencyOptions>(options =>
			options.Environment == "dev" &&
			options.PackageName == "MyApp" &&
			options.Dependencies.SequenceEqual(new[] { "CrtLeadOppMgmtApp", "CrtCase" })));
	}

	#region Test doubles

	private sealed class StubRemovePackageDependencyCommand(int exitCode)
		: RemovePackageDependencyCommand(Substitute.For<IPackageDependencyManager>(), new EnvironmentSettings()) {
		public override int Execute(RemovePackageDependencyOptions options) => exitCode;
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
