using System;
using System.Reflection;
using ATF.Repository.Providers;
using Clio;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Locks the presence of the <see cref="ClassifyingDataProvider"/> decoration at BOTH provider
/// construction sites in <c>BindingsModule</c>: the active-environment <see cref="IDataProvider"/>
/// registration and the per-environment <c>Func&lt;EnvironmentSettings, ISysSettingsManager&gt;</c>
/// factory.
/// </summary>
/// <remarks>
/// Every other test around this decorator constructs <c>new ClassifyingDataProvider(...)</c> by hand,
/// and <c>CredentialPassthroughClientIdentityTests</c> unwraps decorators generically without asserting
/// any are present. So dropping either wrapping - or adding a third provider construction site that
/// forgets it - would leave the whole suite green while restoring the silent empty-success defect of
/// issue #1222: an ATF response whose <c>Success</c> is <see langword="false"/> reaching the caller as
/// an empty collection with the command reporting success. The container is the only place that
/// wiring is observable, so it has to fail here.
/// </remarks>
[TestFixture]
[Category("Unit")]
[NonParallelizable]
[Property("Module", "Common")]
public sealed class ClassifyingDataProviderDiRegistrationTests {

	private const string EnvironmentUri = "https://tenant-a.creatio.example.com";

	private static EnvironmentSettings BuildSettings() => new() {
		Uri = EnvironmentUri,
		Login = "Supervisor",
		Password = "Supervisor"
	};

	private static IServiceProvider BuildContainer() => new BindingsModule().Register(BuildSettings());

	[Test]
	[Description("The active-environment IDataProvider registration must resolve the classifying decorator itself, not the raw provider it wraps.")]
	public void Container_Should_ResolveClassifyingDataProvider_ForActiveEnvironment() {
		// Arrange
		IServiceProvider container = BuildContainer();

		// Act
		IDataProvider dataProvider = container.GetRequiredService<IDataProvider>();

		// Assert
		dataProvider.Should().BeOfType<ClassifyingDataProvider>(
			because: "the decorator is the only barrier between an unsuccessful ATF response and a command " +
				"reporting success on an empty result (issue #1222); a raw provider here restores that defect silently");
	}

	[Test]
	[Description("The per-environment ISysSettingsManager factory is a second provider construction site and must wrap its provider in the same decorator.")]
	public void EnvironmentScopedSysSettingsManager_Should_BeBuiltOnClassifyingDataProvider() {
		// Arrange
		IServiceProvider container = BuildContainer();
		Func<EnvironmentSettings, ISysSettingsManager> factory =
			container.GetRequiredService<Func<EnvironmentSettings, ISysSettingsManager>>();

		// Act
		ISysSettingsManager manager = factory(BuildSettings());
		FieldInfo providerField = manager.GetType()
			.GetField("_dataProvider", BindingFlags.Instance | BindingFlags.NonPublic);

		// Assert
		providerField.Should().NotBeNull(
			because: "the per-environment manager keeps its provider in '_dataProvider'; if that field is renamed, " +
				"adapt this test rather than drop the wrapping assertion");
		providerField.GetValue(manager).Should().BeOfType<ClassifyingDataProvider>(
			because: "a provider left raw on the per-environment path is unprotected, so every command reached " +
				"through this factory would keep reporting a rejected read as an empty success (issue #1222)");
	}
}
