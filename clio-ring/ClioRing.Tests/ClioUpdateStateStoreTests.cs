using System;
using System.IO;
using ClioRing.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ClioRing.Tests;

[TestFixture]
[Category("Unit")]
public sealed class ClioUpdateStateStoreTests {
	private string _directory = null!;
	private string _path = null!;

	[SetUp]
	public void SetUp() {
		_directory = Path.Combine(Path.GetTempPath(), $"clio-ring-update-state-{Guid.NewGuid():N}");
		_path = Path.Combine(_directory, "state.json");
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
	}

	[Test]
	[Description("Notification acknowledgement survives a new store instance and is emitted only once per available version.")]
	public void TryMarkNotified_ShouldDeduplicateAcrossStoreInstances() {
		// Arrange
		IClioUpdateStateStore first = ResolveStore();
		first.Write(new ClioUpdateState(DateTimeOffset.UtcNow, "8.1.0.84", "8.1.0.86", null));

		// Act
		bool firstNotification = first.TryMarkNotified("8.1.0.86");
		IClioUpdateStateStore reopened = ResolveStore();
		bool duplicateNotification = reopened.TryMarkNotified("8.1.0.86");

		// Assert
		firstNotification.Should().BeTrue(because: "the version has not been notified before");
		duplicateNotification.Should().BeFalse(because: "notification acknowledgement persists across restarts");
		reopened.Read()!.NotifiedVersion.Should().Be("8.1.0.86",
			because: "only the non-sensitive available version is persisted");
	}

	private IClioUpdateStateStore ResolveStore() {
		var services = new ServiceCollection();
		services.AddSingleton<IClioUpdateStateStore>(new ClioUpdateStateStore(_path));
		return services.BuildServiceProvider().GetRequiredService<IClioUpdateStateStore>();
	}
}
