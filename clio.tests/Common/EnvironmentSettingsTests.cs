using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using YamlDotNet.Serialization;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class EnvironmentSettingsTests {

	#region Methods: Private

	private static EnvironmentSettings CreateSecretBearingSettings() => new() {
		Uri = "https://passthrough.creatio.com",
		Login = "Supervisor",
		AccessToken = "super-secret-token-value",
		AccessTokenType = "Bearer",
		Cookie = "BPMLOADER=secret-cookie; .ASPXAUTH=secret-auth"
	};

	#endregion

	[Test]
	[Description("AccessTokenType defaults to Bearer so the bearer branch is the implicit passthrough contract")]
	public void AccessTokenType_ShouldDefaultToBearer_WhenNotSet() {
		// Arrange & Act
		EnvironmentSettings settings = new();

		// Assert
		settings.AccessTokenType.Should().Be("Bearer",
			because: "the passthrough contract defaults the access-token type to Bearer");
	}

	[Test]
	[Description("Newtonsoft JSON serialization omits the AccessToken, AccessTokenType and Cookie secret fields")]
	public void JsonSerialize_ShouldOmitSecretFields_WhenSerialized() {
		// Arrange
		EnvironmentSettings settings = CreateSecretBearingSettings();

		// Act
		string json = JsonConvert.SerializeObject(settings);

		// Assert
		json.Should().NotContain("AccessToken",
			because: "[Newtonsoft.Json.JsonIgnore] must keep AccessToken/AccessTokenType out of appsettings.json");
		json.Should().NotContain("Cookie",
			because: "[Newtonsoft.Json.JsonIgnore] must keep Cookie out of appsettings.json");
		json.Should().NotContain("super-secret-token-value",
			because: "the secret token value must never be persisted");
		json.Should().NotContain("secret-cookie",
			because: "the secret cookie value must never be persisted");
	}

	[Test]
	[Description("YamlDotNet serialization omits the AccessToken, AccessTokenType and Cookie secret fields")]
	public void YamlSerialize_ShouldOmitSecretFields_WhenSerialized() {
		// Arrange
		EnvironmentSettings settings = CreateSecretBearingSettings();
		ISerializer serializer = new SerializerBuilder().Build();

		// Act
		string yaml = serializer.Serialize(settings);

		// Assert
		yaml.Should().NotContain("AccessToken",
			because: "[YamlIgnore] must keep AccessToken/AccessTokenType out of any YAML export");
		yaml.Should().NotContain("Cookie",
			because: "[YamlIgnore] must keep Cookie out of any YAML export");
		yaml.Should().NotContain("super-secret-token-value",
			because: "the secret token value must never be serialized to YAML");
		yaml.Should().NotContain("secret-cookie",
			because: "the secret cookie value must never be serialized to YAML");
	}

	[Test]
	[Description("ShowSettingsTo does not emit the AccessToken, AccessTokenType or Cookie secret fields")]
	public void ShowSettingsTo_ShouldOmitSecretFields_WhenEnvironmentHasThem() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository repository = new(fileSystem);
		repository.ConfigureEnvironment("passthrough", CreateSecretBearingSettings());
		StringWriter writer = new();

		// Act
		repository.ShowSettingsTo(writer, "passthrough");
		string output = writer.ToString();
		// ConfigureEnvironment already invoked the real SaveSettings/appsettings.json write path.
		string persisted = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		output.Should().NotContain("AccessToken",
			because: "ShowSettingsTo serializes via Newtonsoft and must honor [Newtonsoft.Json.JsonIgnore] on the token fields");
		output.Should().NotContain("Cookie",
			because: "ShowSettingsTo must not expose the Cookie field");
		output.Should().NotContain("super-secret-token-value",
			because: "the secret token value must never be printed by ShowSettingsTo");
		output.Should().NotContain("secret-cookie",
			because: "the secret cookie value must never be printed by ShowSettingsTo");
		persisted.Should().NotContain("super-secret-token-value",
			because: "the real SaveSettings/appsettings.json write path must never persist the token value");
		persisted.Should().NotContain("secret-cookie",
			because: "the real SaveSettings/appsettings.json write path must never persist the cookie value");
	}

	[Test]
	[Description("GetOrCreateKnowledgeRootPath writes one visible absolute knowledge-root-path into appsettings.json.")]
	public void GetOrCreateKnowledgeRootPath_ShouldPersistVisibleAbsoluteSetting_WhenMissing() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository repository = new(fileSystem);
		string expected = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(
			fileSystem.Path.GetDirectoryName(SettingsRepository.AppSettingsFile)!, "knowledge"));

		// Act
		string resolved = repository.GetOrCreateKnowledgeRootPath(expected);
		string persisted = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		resolved.Should().Be(expected,
			because: "the first knowledge operation must use the persisted absolute default path");
		persisted.Should().Contain("\"knowledge-root-path\"",
			because: "the knowledge location must be explicit and visible in appsettings.json");
		persisted.Should().Contain(JsonConvert.ToString(expected),
			because: "the visible setting must contain the exact normalized root used by the runtime");
	}
}
