using Clio.Common.DbHub;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Clio.Tests.Common.DbHub;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class DbHubSettingsTests {
	[Test]
	[Description("dbHub settings round-trip through the clio appsettings JSON contract.")]
	public void Settings_ShouldRoundTripDbHubConfiguration() {
		// Arrange
		Settings settings = new() {
			DbHub = new DbHubSettings {
				Enabled = true,
				ConfigPath = @"D:\configs\dbhub.toml",
				Host = "127.0.0.1",
				Port = 18001,
				SyncLocalEnvironments = false
			}
		};

		// Act
		string json = JsonConvert.SerializeObject(settings);
		Settings restored = JsonConvert.DeserializeObject<Settings>(json);

		// Assert
		restored.DbHub.Should().BeEquivalentTo(settings.DbHub,
			because: "every persisted dbHub option must survive clio configuration serialization");
		json.Should().Contain("\"config-path\"", because: "the JSON contract uses documented kebab-case names");
	}
}
