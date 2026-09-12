using System.Collections.Generic;
using Clio.Command;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Integration")]
[Platform(Include = "Win")]
[Property("Module", "Command")]
internal sealed class WindowsFeatureProviderIntegrationTests {
	[Test]
	[Description("Queries the real Windows optional-feature inventory through the operating-system provider.")]
	public void GetActiveWindowsFeatures_ShouldReturnInstalledFeatures_OnWindows() {
		// Arrange
		ServiceCollection services = new();
		services.AddSingleton<IWindowsFeatureProvider, WindowsFeatureProvider>();
		using ServiceProvider serviceProvider = services.BuildServiceProvider();
		IWindowsFeatureProvider sut = serviceProvider.GetRequiredService<IWindowsFeatureProvider>();

		// Act
		IEnumerable<string> features = sut.GetActiveWindowsFeatures();

		// Assert
		features.Should().NotBeEmpty(
			because: "a Windows host exposes installed optional features through Win32_OptionalFeature");
		features.Should().Contain(feature => !string.IsNullOrWhiteSpace(feature),
			because: "the provider contract includes usable feature identifiers even when Windows omits a caption");
	}
}
