using System;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Issue #1379: the sys-setting failure classifier is an injected service, so the container - not a
/// static member of <c>SysSettingsCommand</c> - is what every consumer depends on.
/// </summary>
/// <remarks>
/// The five MCP tool classes are instantiated by the MCP SDK through
/// <see cref="ActivatorUtilities"/> against the host container, and <c>SysSettingsCommand</c> is
/// resolved per environment by <c>ToolCommandResolver</c> from a child of the same registrations. A
/// missing or unsatisfiable registration therefore does not fail at compile time: it fails when a tool
/// is first called, on whatever transport happens to reach it. That is the failure this fixture makes
/// loud, and it is why it asserts construction of the consumers rather than only of the classifier.
/// </remarks>
[TestFixture]
[Category("Unit")]
[NonParallelizable]
[Property("Module", "Command")]
public sealed class SysSettingFailureClassifierDiRegistrationTests {

	private static EnvironmentSettings BuildSettings() => new() {
		Uri = "https://tenant-a.creatio.example.com",
		Login = "Supervisor",
		Password = "Supervisor"
	};

	private static IServiceProvider BuildContainer() => new BindingsModule().Register(BuildSettings());

	[Test]
	[Description("The container resolves ISysSettingFailureClassifier to the production implementation, so no consumer has to reach into a command's statics for the classification.")]
	public void Container_Should_Resolve_The_Classifier() {
		// Arrange
		IServiceProvider container = BuildContainer();

		// Act
		ISysSettingFailureClassifier classifier = container.GetRequiredService<ISysSettingFailureClassifier>();

		// Assert
		classifier.Should().BeOfType<SysSettingFailureClassifier>(
			because: "the assembly interface scan is what registers it; a skip-list entry or a renamed interface "
				+ "would leave every sys-setting tool unconstructible at first call rather than at build time");
	}

	[Test]
	[Description("The classifier is registered transient, like the correlation-id provider and the logger-driven siblings the scan produces, so no consumer inherits a shared instance by accident.")]
	public void Classifier_Should_Be_Registered_Transient() {
		// Arrange
		ServiceCollection services = [];

		// Act
		new BindingsModule().RegisterInto(services, BuildSettings());

		// Assert
		services.Should().ContainSingle(descriptor =>
				descriptor.ServiceType == typeof(ISysSettingFailureClassifier),
			because: "two registrations would make which one wins depend on declaration order")
			.Which.Lifetime.Should().Be(ServiceLifetime.Transient,
				because: "the classifier holds no mutable state, and its logger is a process singleton "
					+ "regardless of lifetime");
	}

	[Test]
	[Description("Every consumer of the classifier can be constructed from the container: the four sys-setting MCP tools, the schema-name-prefix tool, and the command itself.")]
	public void Container_Should_Construct_Every_Classifier_Consumer() {
		// Arrange
		IServiceProvider container = BuildContainer();

		// Act
		Action act = () => {
			ActivatorUtilities.CreateInstance<SysSettingGetTool>(container);
			ActivatorUtilities.CreateInstance<SysSettingsListTool>(container);
			ActivatorUtilities.CreateInstance<SysSettingCreateTool>(container);
			ActivatorUtilities.CreateInstance<SysSettingUpdateTool>(container);
			ActivatorUtilities.CreateInstance<SchemaNamePrefixTool>(container);
			container.GetRequiredService<SysSettingsCommand>();
		};

		// Assert
		act.Should().NotThrow(
			because: "the MCP SDK builds a tool class on first call; an unsatisfiable dependency surfaces there, "
				+ "on whatever transport reached it, instead of at build time");
	}
}
