using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio;
using Clio.Command.McpServer.Knowledge;
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
	[Description("GetOrCreateKnowledgeRootPath writes one visible absolute knowledge.root-path into appsettings.json.")]
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
		persisted.Should().Contain("\"knowledge\"",
			because: "multi-source knowledge settings must have one nested configuration object");
		persisted.Should().Contain("\"root-path\"",
			because: "the knowledge location must be explicit and visible in appsettings.json");
		persisted.Should().NotContain("\"knowledge-root-path\"",
			because: "the legacy top-level setting must not remain as a second writable source of truth");
		persisted.Should().Contain(JsonConvert.ToString(expected),
			because: "the visible setting must contain the exact normalized root used by the runtime");
	}

	[Test]
	[Description("Reading a legacy knowledge-root-path migrates it once into the nested knowledge configuration.")]
	public void GetKnowledgeRootPath_ShouldMigrateLegacySetting_WhenNestedSettingIsMissing() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		string expected = TestFileSystem.GetRootedPath("clio", "knowledge");
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(
			JsonConvert.SerializeObject(new { knowledge_root_path = expected }).Replace(
				"knowledge_root_path", "knowledge-root-path", StringComparison.Ordinal)));
		SettingsRepository repository = new(fileSystem);

		// Act
		string result = repository.GetKnowledgeRootPath();
		string persisted = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		result.Should().Be(fileSystem.Path.GetFullPath(expected),
			because: "existing installations must retain their configured knowledge location");
		persisted.Should().Contain("\"root-path\"",
			because: "the legacy value must be migrated into the nested knowledge object");
		persisted.Should().NotContain("\"knowledge-root-path\"",
			because: "migration must leave only one writable root-path setting");
	}

	[Test]
	[Description("UpsertKnowledgeSource persists validated source fields under the nested knowledge configuration.")]
	public void UpsertKnowledgeSource_ShouldPersistNestedSource_WhenConfigurationIsValid() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository repository = new(fileSystem);
		KnowledgeSourceConfiguration source = new() {
			LibraryId = "com.example.partner",
			Type = KnowledgeSourceType.Git,
			Location = "https://example.invalid/knowledge.git",
			TrustedKeyId = "partner-signing-2026",
			TrustedPublicKeyPath = TestFileSystem.GetRootedPath("keys", "partner-public.pem"),
			Branch = "main",
			Enabled = true,
			Priority = 50,
			Participation = KnowledgeSourceParticipation.Supplement
		};

		// Act
		repository.UpsertKnowledgeSource("partner", source);
		KnowledgeConfiguration snapshot = repository.GetKnowledgeConfiguration();
		string persisted = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		snapshot.Sources["PARTNER"].LibraryId.Should().Be("com.example.partner",
			because: "source aliases are operator-facing and must resolve case-insensitively");
		snapshot.Sources["partner"].ArtifactPath.Should().Be("knowledge-bundle.zip",
			because: "Git sources use the safe declared-artifact default when none is supplied");
		persisted.Should().Contain("\"type\": \"git\"",
			because: "transport types must persist as stable lowercase contract values");
		persisted.Should().Contain("\"library-id\": \"com.example.partner\"",
			because: "the stable library identity is the persisted resolution identity");
		persisted.Should().Contain("\"trusted-key-id\": \"partner-signing-2026\"",
			because: "each publisher's declared signing key identity must persist with that source");
		persisted.Should().Contain("\"trusted-public-key-path\"",
			because: "the visible source configuration must identify its local public verification key");
	}

	[Test]
	[Description("SetKnowledgeConfiguration persists topic pins by stable library ID and returns detached snapshots.")]
	public void SetKnowledgeConfiguration_ShouldPersistDetachedTopicPins_WhenPinIsValid() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository repository = new(fileSystem);
		KnowledgeConfiguration configuration = new() {
			Sources = new Dictionary<string, KnowledgeSourceConfiguration>(StringComparer.OrdinalIgnoreCase) {
				["creatio"] = new KnowledgeSourceConfiguration {
					LibraryId = "com.creatio.clio",
					Type = KnowledgeSourceType.NuGet,
					Location = "https://example.invalid/v3/index.json",
					TrustedKeyId = "creatio-signing-2026",
					TrustedPublicKeyPath = TestFileSystem.GetRootedPath("keys", "creatio-public.pem"),
					PackageId = "Clio.Knowledge",
					Participation = KnowledgeSourceParticipation.Authoritative
				}
			},
			TopicPins = new Dictionary<string, string>(StringComparer.Ordinal) {
				["creatio.esq.filters"] = "com.creatio.clio"
			}
		};

		// Act
		repository.SetKnowledgeConfiguration(configuration);
		KnowledgeConfiguration first = repository.GetKnowledgeConfiguration();
		first.TopicPins["creatio.esq.filters"] = "com.example.changed";
		KnowledgeConfiguration second = repository.GetKnowledgeConfiguration();

		// Assert
		second.TopicPins["creatio.esq.filters"].Should().Be("com.creatio.clio",
			because: "callers must not mutate persisted pin state through a returned settings snapshot");
		fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile).Should().Contain("\"topic-pins\"",
			because: "logical-topic preferences must be visible and portable in appsettings.json");
	}

	[Test]
	[Description("UpsertKnowledgeSource rejects a second alias for the same stable library identity.")]
	public void UpsertKnowledgeSource_ShouldRejectDuplicateLibraryId_WhenAliasDiffers() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository repository = new(fileSystem);
		KnowledgeSourceConfiguration first = new() {
			LibraryId = "com.example.partner",
			Type = KnowledgeSourceType.NuGet,
			Location = "https://example.invalid/v3/index.json",
			TrustedKeyId = "partner-signing-2026",
			TrustedPublicKeyPath = TestFileSystem.GetRootedPath("keys", "partner-public.pem"),
			PackageId = "Example.Knowledge"
		};
		repository.UpsertKnowledgeSource("partner", first);
		KnowledgeSourceConfiguration duplicate = new() {
			LibraryId = "com.example.partner",
			Type = KnowledgeSourceType.NuGet,
			Location = "https://example.invalid/v3/index.json",
			TrustedKeyId = "other-signing-2026",
			TrustedPublicKeyPath = TestFileSystem.GetRootedPath("keys", "other-public.pem"),
			PackageId = "Example.OtherKnowledge"
		};

		// Act
		Action action = () => repository.UpsertKnowledgeSource("other", duplicate);

		// Assert
		action.Should().Throw<ArgumentException>(
			because: "stable library IDs must remain unique even when aliases differ");
	}

	[Test]
	[Description("UpsertKnowledgeSource rejects a relative trusted public-key path before persisting the source.")]
	public void UpsertKnowledgeSource_ShouldRejectRelativeTrustedPublicKeyPath() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository repository = new(fileSystem);
		KnowledgeSourceConfiguration source = new() {
			LibraryId = "com.example.partner",
			Type = KnowledgeSourceType.NuGet,
			Location = "https://example.invalid/v3/index.json",
			TrustedKeyId = "partner-signing-2026",
			TrustedPublicKeyPath = "keys/partner-public.pem",
			PackageId = "Example.Knowledge"
		};

		// Act
		Action action = () => repository.UpsertKnowledgeSource("partner", source);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*trusted-public-key-path*absolute*",
				because: "trusted key selection must not depend on the process working directory");
	}

	[Test]
	[Description("Discovered-branch compare-and-swap rejects a stale source snapshot and preserves the concurrent configuration edit.")]
	public void TrySetKnowledgeSourceBranch_ShouldPreserveConcurrentEdit_WhenExpectedSourceIsStale() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository repository = new(fileSystem);
		KnowledgeSourceConfiguration original = GitSource(priority: 10);
		repository.UpsertKnowledgeSource("partner", original);
		KnowledgeSourceConfiguration expected = repository.GetKnowledgeConfiguration().Sources["partner"];
		KnowledgeSourceConfiguration concurrentlyChanged = GitSource(priority: 99);
		repository.UpsertKnowledgeSource("partner", concurrentlyChanged);

		// Act
		bool updated = repository.TrySetKnowledgeSourceBranch("partner", expected, "main");
		KnowledgeSourceConfiguration persisted = repository.GetKnowledgeConfiguration().Sources["partner"];

		// Assert
		updated.Should().BeFalse(
			because: "branch discovery must not overwrite a source changed after retrieval began");
		persisted.Priority.Should().Be(99,
			because: "the concurrent operator change remains authoritative after a lost compare-and-swap");
		persisted.Branch.Should().BeNull(
			because: "a branch resolved from stale source inputs cannot be attached to the new configuration");
	}

	[Test]
	[Description("Source-removal compare-and-swap rejects a stale snapshot and preserves the concurrently edited source.")]
	public void TryRemoveKnowledgeSource_ShouldPreserveConcurrentEdit_WhenExpectedSourceIsStale() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository repository = new(fileSystem);
		KnowledgeSourceConfiguration original = GitSource(priority: 10);
		repository.UpsertKnowledgeSource("partner", original);
		KnowledgeSourceConfiguration expected = repository.GetKnowledgeConfiguration().Sources["partner"];
		KnowledgeSourceConfiguration concurrentlyChanged = GitSource(priority: 99);
		repository.UpsertKnowledgeSource("partner", concurrentlyChanged);

		// Act
		bool removed = repository.TryRemoveKnowledgeSource("partner", expected);
		KnowledgeConfiguration persisted = repository.GetKnowledgeConfiguration();

		// Assert
		removed.Should().BeFalse(
			because: "removal authority applies only to the exact source snapshot the operator reviewed");
		persisted.Sources.Should().ContainKey("partner",
			because: "a concurrent edit must prevent deletion of the changed source");
		persisted.Sources["partner"].Priority.Should().Be(99,
			because: "the concurrent operator change must remain intact after the failed removal");
	}

	private static KnowledgeSourceConfiguration GitSource(int priority) => new() {
		LibraryId = "com.example.partner",
		Type = KnowledgeSourceType.Git,
		Location = "https://example.invalid/knowledge.git",
		TrustedKeyId = "partner-signing-2026",
		TrustedPublicKeyPath = TestFileSystem.GetRootedPath("keys", "partner-public.pem"),
		Enabled = true,
		Priority = priority,
		Participation = KnowledgeSourceParticipation.Supplement
	};
}
