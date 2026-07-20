using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SettingsRepositoryFeatureTests {

	private MockFileSystem _fileSystem;

	[SetUp]
	public void SetUp() {
		_fileSystem = TestFileSystem.MockFileSystem();
		_fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(
			File.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-netcore-active-env.json"))));
	}

	[Test]
	[Description("Persists a normalized IIS certificate thumbprint at the appsettings root and clears it without affecting environments.")]
	public void PinnedIisCertificateThumbprint_ShouldRoundTrip_AndClear() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);

		// Act
		sut.SetPinnedIisCertificateThumbprint("aa bb cc dd ee ff 00 11 22 33 44 55 66 77 88 99 aa bb cc dd");
		SettingsRepository pinned = new(_fileSystem);
		string persistedThumbprint = pinned.GetPinnedIisCertificateThumbprint();
		pinned.SetPinnedIisCertificateThumbprint(null);
		SettingsRepository cleared = new(_fileSystem);

		// Assert
		persistedThumbprint.Should().Be("AABBCCDDEEFF00112233445566778899AABBCCDD",
			because: "thumbprints should be stored in one canonical uppercase hex representation");
		cleared.GetPinnedIisCertificateThumbprint().Should().BeNull(
			because: "clearing the preference should remove it from subsequent repository loads");
		cleared.GetAllEnvironments().Should().NotBeEmpty(
			because: "updating the root certificate preference must preserve registered environments");
	}

	[Test]
	[Description("Refreshes an existing stale generated schema from the bundled template and leaves no temporary artifacts.")]
	public void Constructor_ShouldRefreshStaleSchema_WithoutLeavingTemporaryFiles() {
		// Arrange
		const string currentTemplate = "{\"schema-version\":2}";
		string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tpl", "jsonschema", "schema.json.tpl");
		_fileSystem.AddFile(templatePath, new MockFileData(currentTemplate));
		_fileSystem.AddFile(SettingsRepository.SchemaFilePath, new MockFileData("{\"schema-version\":1}"));

		// Act
		_ = new SettingsRepository(_fileSystem);
		_ = new SettingsRepository(_fileSystem);

		// Assert
		_fileSystem.File.ReadAllText(SettingsRepository.SchemaFilePath).Should().Be(currentTemplate,
			because: "existing generated schemas must receive new appsettings fields from the bundled template");
		_fileSystem.AllFiles.Should().NotContain(path => path.Contains("schema.json.", StringComparison.Ordinal)
			&& path.EndsWith(".tmp", StringComparison.Ordinal),
			because: "atomic refresh and an idempotent second load must clean every temporary schema artifact");
	}

	[Test]
	[Description("Documents every knowledge configuration key and the transport-specific NuGet requirement in the generated appsettings schema.")]
	public void AppSettingsSchema_ShouldDescribeKnowledgeKeys_WhenKnowledgeConfigurationIsAvailable() {
		// Arrange
		string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tpl", "jsonschema", "schema.json.tpl");
		JsonObject schema = JsonNode.Parse(File.ReadAllText(templatePath))!.AsObject();
		JsonObject definitions = schema["definitions"]!.AsObject();
		JsonObject knowledge = definitions["knowledgeconfiguration"]!.AsObject();
		JsonObject knowledgeProperties = knowledge["properties"]!.AsObject();
		JsonObject source = definitions["knowledgesource"]!.AsObject();
		JsonObject sourceProperties = source["properties"]!.AsObject();

		// Act
		string[] knowledgeKeys = knowledgeProperties.Select(property => property.Key).ToArray();
		string[] sourceKeys = sourceProperties.Select(property => property.Key).ToArray();
		string[] undocumentedSourceKeys = sourceProperties
			.Where(property => string.IsNullOrWhiteSpace(property.Value?["description"]?.GetValue<string>()))
			.Select(property => property.Key)
			.ToArray();
		string[] nugetRequired = source["allOf"]![0]!["then"]!["required"]!.AsArray()
			.Select(value => value!.GetValue<string>())
			.ToArray();

		// Assert
		knowledgeKeys.Should().BeEquivalentTo(["root-path", "sources", "topic-pins"],
			because: "the editor schema must expose every persisted knowledge section key");
		sourceKeys.Should().BeEquivalentTo([
			"library-id", "type", "location", "trusted-key-id", "trusted-public-key-path", "package-id",
			"branch", "tag", "commit", "enabled", "priority", "participation"
		], because: "the editor schema must expose every trusted-source transport and resolution key");
		undocumentedSourceKeys.Should().BeEmpty(
			because: "hover help must explain every unfamiliar trusted-source setting to an operator");
		nugetRequired.Should().BeEquivalentTo(["package-id", "trusted-key-id", "trusted-public-key-path"],
			because: "NuGet sources require a package identity and signing trust while Git sources do not");
	}

	[Test]
	[Description("IsFeatureEnabled returns false when the feature flag is absent from settings.")]
	public void IsFeatureEnabled_ShouldReturnFalse_WhenFeatureAbsent() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);

		// Act
		bool result = sut.IsFeatureEnabled("absent-feature");

		// Assert
		result.Should().BeFalse(because: "a feature with no stored flag defaults to disabled");
	}

	[Test]
	[Description("IsFeatureEnabled returns false for a null or whitespace feature name without throwing.")]
	public void IsFeatureEnabled_ShouldReturnFalse_WhenNameIsNullOrWhitespace() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);

		// Act
		bool nullResult = sut.IsFeatureEnabled(null);
		bool whitespaceResult = sut.IsFeatureEnabled("   ");

		// Assert
		nullResult.Should().BeFalse(because: "a null feature name is treated as disabled rather than throwing");
		whitespaceResult.Should().BeFalse(because: "a whitespace feature name is treated as disabled rather than throwing");
	}

	[Test]
	[Description("SetFeature persists an enabled flag that round-trips through a freshly loaded repository.")]
	public void SetFeature_ShouldPersistEnabledFlag_WhenSetToTrue() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);

		// Act
		sut.SetFeature("round-trip-feature", true);
		SettingsRepository reloaded = new(_fileSystem);
		bool result = reloaded.IsFeatureEnabled("round-trip-feature");

		// Assert
		result.Should().BeTrue(because: "a feature set to true must persist and round-trip across repository instances");
	}

	[Test]
	[Description("SetFeature upserts an existing flag value and persists the change.")]
	public void SetFeature_ShouldUpsertExistingFlag_WhenCalledTwice() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		sut.SetFeature("toggle-feature", true);

		// Act
		sut.SetFeature("toggle-feature", false);
		SettingsRepository reloaded = new(_fileSystem);
		bool result = reloaded.IsFeatureEnabled("toggle-feature");

		// Assert
		result.Should().BeFalse(because: "re-setting an existing feature overwrites the prior value and persists it");
	}

	[Test]
	[Description("SetFeature throws ArgumentException when the feature name is null or whitespace.")]
	public void SetFeature_ShouldThrowArgumentException_WhenNameIsNullOrWhitespace() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);

		// Act
		Action nullAct = () => sut.SetFeature(null, true);
		Action whitespaceAct = () => sut.SetFeature("  ", true);

		// Assert
		nullAct.Should().Throw<ArgumentException>(because: "a null feature name cannot be persisted");
		whitespaceAct.Should().Throw<ArgumentException>(because: "a whitespace feature name cannot be persisted");
	}

	[Test]
	[Description("IsFeatureEnabled matches a feature key case-insensitively regardless of stored casing.")]
	public void IsFeatureEnabled_ShouldMatchCaseInsensitively_WhenCasingDiffers() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		sut.SetFeature("AiAssist", true);

		// Act
		bool lowerResult = sut.IsFeatureEnabled("aiassist");
		bool upperResult = sut.IsFeatureEnabled("AIASSIST");

		// Assert
		lowerResult.Should().BeTrue(because: "feature keys are compared case-insensitively, so a lowercase lookup must hit the stored flag");
		upperResult.Should().BeTrue(because: "feature keys are compared case-insensitively, so an uppercase lookup must hit the stored flag");
	}

	[Test]
	[Description("SetFeature updates the same flag entry when called with different casing rather than creating a duplicate.")]
	public void SetFeature_ShouldUpdateSameEntry_WhenCasingDiffers() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		sut.SetFeature("AiAssist", true);

		// Act
		sut.SetFeature("aiassist", false);
		SettingsRepository reloaded = new(_fileSystem);
		bool result = reloaded.IsFeatureEnabled("AIASSIST");
		int aiAssistEntryCount = reloaded.GetFeatures().Keys
			.Count(key => string.Equals(key, "aiassist", StringComparison.OrdinalIgnoreCase));

		// Assert
		result.Should().BeFalse(because: "re-setting the same key with different casing overwrites the single stored entry");
		aiAssistEntryCount.Should().Be(1, because: "case-insensitive keys must not produce duplicate entries for the same logical feature");
	}

	[Test]
	[Description("GetFeatures snapshot supports case-insensitive lookups so orphan-detection callers are casing-agnostic.")]
	public void GetFeatures_ShouldSupportCaseInsensitiveLookup_WhenCasingDiffers() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		sut.SetFeature("AiAssist", true);

		// Act
		IReadOnlyDictionary<string, bool> snapshot = sut.GetFeatures();
		bool found = snapshot.ContainsKey("aiassist");

		// Assert
		found.Should().BeTrue(because: "the snapshot is built with a case-insensitive comparer so callers can match keys regardless of casing");
	}

	[Test]
	[Description("Constructing the repository does not throw and applies last-wins when appsettings.json holds case-variant duplicate feature keys.")]
	public void Constructor_ShouldNotThrowAndApplyLastWins_WhenFeatureKeysDifferOnlyByCase() {
		// Arrange
		const string json = @"{
  ""ActiveEnvironmentKey"": ""netcore-env"",
  ""Environments"": {
    ""netcore-env"": { ""Uri"": ""http://localhost:5001"", ""Login"": ""Supervisor"", ""Password"": ""Supervisor"", ""IsNetCore"": true }
  },
  ""Features"": { ""AiAssist"": true, ""aiassist"": false }
}";
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(json));

		// Act
		Action act = () => _ = new SettingsRepository(fileSystem);
		SettingsRepository sut = new(fileSystem);
		bool enabled = sut.IsFeatureEnabled("AiAssist");
		int aiAssistEntryCount = sut.GetFeatures().Keys
			.Count(key => string.Equals(key, "aiassist", StringComparison.OrdinalIgnoreCase));

		// Assert
		act.Should().NotThrow(
			because: "case-variant duplicate keys must be rebuilt last-wins instead of throwing ArgumentException");
		enabled.Should().BeFalse(
			because: "the last case-variant entry in file order (aiassist=false) must win the case-insensitive rebuild");
		aiAssistEntryCount.Should().Be(1,
			because: "case-variant duplicate keys collapse into a single case-insensitive entry");
	}

	[Test]
	[Description("GetFeatures returns a snapshot of stored flags that does not affect persisted settings when mutated.")]
	public void GetFeatures_ShouldReturnSnapshot_WhenFeaturesExist() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		sut.SetFeature("snapshot-feature", true);

		// Act
		IReadOnlyDictionary<string, bool> snapshot = sut.GetFeatures();
		((Dictionary<string, bool>)snapshot)["snapshot-feature"] = false;
		bool stillEnabled = sut.IsFeatureEnabled("snapshot-feature");

		// Assert
		snapshot.Should().ContainKey("snapshot-feature", because: "the snapshot reflects the stored feature flags");
		stillEnabled.Should().BeTrue(because: "mutating the returned snapshot must not change the repository's stored state");
	}
}
