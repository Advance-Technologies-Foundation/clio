using System;
using System.IO;
using System.Threading;
using ClioRing.Services;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>Regression coverage for clio environment settings invalidation.</summary>
[TestFixture]
[Category("Unit")]
public sealed class EnvironmentSettingsWatcherTests {
	[Test]
	[Description("A settings file rewrite raises one debounced environment change notification.")]
	public void Changed_ShouldBeRaised_WhenSettingsFileIsRewritten() {
		// Arrange
		string directory = Path.Combine(Path.GetTempPath(), $"clio-ring-watch-test-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, "appsettings.json");
		File.WriteAllText(path, "{}");
		using var signal = new ManualResetEventSlim();
		using var watcher = new EnvironmentSettingsWatcher();
		watcher.Changed += signal.Set;
		watcher.Start(path);

		try {
			// Act
			File.WriteAllText(path, "{\"Environments\":{}}");
			bool raised = signal.Wait(TimeSpan.FromSeconds(5));

			// Assert
			raised.Should().BeTrue(because: "editing clio appsettings.json must invalidate the open Ring environment catalog");
		}
		finally {
			watcher.Stop();
			Directory.Delete(directory, recursive: true);
		}
	}
}
