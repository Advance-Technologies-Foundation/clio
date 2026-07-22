using System;
using System.IO.Abstractions.TestingHelpers;
using Clio;
using Clio.Common;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ILogger = Clio.Common.ILogger;

namespace Clio.Tests.Common;

/// <summary>
/// Covers the Phase 1 (D3) run-mode service wiring: <see cref="IRuntimeMode"/> is registered as a
/// singleton and resolving <see cref="ILogger"/> back-fills the process-wide <see cref="ConsoleLogger"/>
/// singleton's run-mode from that service, replacing the former <c>Program.IsMcpServerMode</c> static read.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
[NonParallelizable]
public sealed class RuntimeModeRegistrationTests {

	[TearDown]
	public void ResetRuntimeMode() {
		// ConsoleLogger is a process-wide singleton; clear the run-mode back-filled by these tests so it
		// never leaks console-output suppression into another fixture.
		((ConsoleLogger)ConsoleLogger.Instance).RuntimeMode = null;
	}

	[Test]
	[Description("Registers IRuntimeMode as a singleton so the same instance resolves across the container lifetime.")]
	public void Register_ShouldRegisterRuntimeModeAsSingleton_WhenContainerIsBuilt() {
		// Arrange
		((ConsoleLogger)ConsoleLogger.Instance).RuntimeMode = null;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();

		// Act
		IServiceProvider provider = new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: false);
		IRuntimeMode first = provider.GetRequiredService<IRuntimeMode>();
		IRuntimeMode second = provider.GetRequiredService<IRuntimeMode>();

		// Assert
		first.Should().NotBeNull(
			because: "the Core-owned run-mode abstraction must be registered in the DI container");
		second.Should().BeSameAs(first,
			because: "IRuntimeMode is registered as a singleton, so every resolve must return the same instance");
	}

	[Test]
	[Description("Resolving ILogger back-fills the ConsoleLogger singleton's run-mode from the registered IRuntimeMode when the entry point never set it.")]
	public void Resolve_ShouldBackfillLoggerRuntimeMode_FromRegisteredService_WhenProgramNeverSetIt() {
		// Arrange
		((ConsoleLogger)ConsoleLogger.Instance).RuntimeMode = null;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IServiceProvider provider = new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: false);

		// Act
		ILogger logger = provider.GetRequiredService<ILogger>();

		// Assert
		logger.Should().BeSameAs(ConsoleLogger.Instance,
			because: "the container must return the process-wide ConsoleLogger singleton, not a new instance");
		((ConsoleLogger)ConsoleLogger.Instance).RuntimeMode.Should().NotBeNull(
			because: "resolving ILogger must back-fill the singleton's run-mode from the injected IRuntimeMode so console suppression is driven by the service, not a static");
		((ConsoleLogger)ConsoleLogger.Instance).RuntimeMode.IsMcpServerMode.Should().BeFalse(
			because: "with no MCP entry point set, the default resolved run-mode is a non-MCP CLI run");
	}
}
