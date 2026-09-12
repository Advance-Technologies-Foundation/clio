using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
	[Description("Documents every knowledge configuration key and each transport-specific requirement in the generated appsettings schema.")]
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
		string[] releaseRequired = RequiredForTransport(source, "github-release");
		string[] nugetRequired = RequiredForTransport(source, "nuget");
		string[] transportTypes = source["properties"]!["type"]!["enum"]!.AsArray()
			.Select(value => value!.GetValue<string>())
			.ToArray();

		// Assert
		knowledgeKeys.Should().BeEquivalentTo(["root-path", "sources", "topic-pins"],
			because: "the editor schema must expose every persisted knowledge section key");
		sourceKeys.Should().BeEquivalentTo([
			"library-id", "type", "location", "trusted-key-id", "trusted-public-key-path", "package-id",
			"repository-owner", "repository-name", "asset-name",
			"branch", "tag", "commit", "enabled", "priority", "participation"
		], because: "the editor schema must expose every trusted-source transport and resolution key");
		undocumentedSourceKeys.Should().BeEmpty(
			because: "hover help must explain every unfamiliar trusted-source setting to an operator");
		transportTypes.Should().BeEquivalentTo(["github-release", "nuget", "git"],
			because: "an editor must offer exactly the transports the validator accepts");
		releaseRequired.Should().BeEquivalentTo(["repository-owner", "repository-name", "asset-name"],
			because: "a GitHub release source is addressed by repository identity rather than an arbitrary URL, "
				+ "and its signing trust is optional because Clio pins the built-in library's key");
		nugetRequired.Should().BeEquivalentTo(["package-id", "trusted-key-id", "trusted-public-key-path"],
			because: "NuGet sources require a package identity and signing trust while Git sources do not");
	}

	[Test]
	[Description("Documents every persisted deploy-creatio default and constrains the automatic site-port range shape.")]
	public void AppSettingsSchema_ShouldDescribeDeployCreatioDefaults() {
		// Arrange
		string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tpl", "jsonschema", "schema.json.tpl");
		JsonObject schema = JsonNode.Parse(File.ReadAllText(templatePath))!.AsObject();
		JsonObject rootProperties = schema["properties"]!.AsObject();
		JsonObject defaults = schema["definitions"]!["deploycreatiodefaults"]!.AsObject();
		JsonObject properties = defaults["properties"]!.AsObject();
		JsonObject range = properties["site-port-range"]!.AsObject();

		// Act
		string[] keys = properties.Select(property => property.Key).ToArray();
		int[] defaultRange = range["default"]!.AsArray().Select(value => value!.GetValue<int>()).ToArray();

		// Assert
		rootProperties.Should().ContainKey("deploy-creatio-defaults",
			because: "the generated settings file points editors at this bundled schema");
		keys.Should().BeEquivalentTo([
			"db-server-name", "redis-server-name", "site-name", "site-port", "site-port-range", "deployment"
		], because: "editor completion must expose every deploy default persisted by SettingsRepository");
		range["minItems"]!.GetValue<int>().Should().Be(2,
			because: "runtime validation requires exactly a start and end port");
		range["maxItems"]!.GetValue<int>().Should().Be(2,
			because: "runtime validation rejects extra range values");
		defaultRange.Should().Equal(new[] { 40100, 40199 },
			because: "schema hover and completion must agree with the materialized built-in value");
	}

	private static string[] RequiredForTransport(JsonObject source, string transportType) => source["allOf"]!
		.AsArray()
		.Single(rule => rule!["if"]!["properties"]!["type"]!["const"]!.GetValue<string>() == transportType)!
		["then"]!["required"]!
		.AsArray()
		.Select(value => value!.GetValue<string>())
		.ToArray();

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
	[Description("A feature key containing the MCP worker payload separators is accepted, persisted, and still survives the freeze the host hands to every worker child.")]
	public void SetFeature_ShouldPersistAndStayWorkerSafe_WhenNameContainsPayloadSeparators() {
		// Arrange — the write surface refuses only null/empty/whitespace, so this key is reachable through
		// `clio experimental --name "a;b=c" --enable`, and a hand-edited appsettings.json can hold it no
		// matter what the write surface allows.
		const string separatorBearingKey = "a;b=c";
		SettingsRepository sut = new(_fileSystem);

		// Act
		sut.SetFeature(separatorBearingKey, true);
		SettingsRepository reloaded = new(_fileSystem);
		IReadOnlyDictionary<string, bool> persisted = reloaded.GetFeatures();
		string workerPayload = McpWorkerEnvironment.Format(persisted);

		// Assert
		persisted.Should().ContainKey(separatorBearingKey,
			because: "the repository persists the key as supplied; nothing between the command and the file "
				+ "narrows the accepted character set");
		McpWorkerEnvironment.Parse(workerPayload).Should().ContainKey(separatorBearingKey,
			because: "the host freezes this exact map into every worker before spawning it, so a key the "
				+ "settings file can hold must never be the reason a worker fails to start");
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

	[Test]
	[Description("Keeps settings editor completion aligned with the per-component automatic-update defaults.")]
	public void AppSettingsSchema_ShouldUseComponentDefaults_WhenCompletingAutoUpdatePolicies() {
		// Arrange
		string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tpl", "jsonschema", "schema.json.tpl");
		JsonObject definitions = JsonNode.Parse(File.ReadAllText(templatePath))!["definitions"]!.AsObject();

		// Act
		JsonNode policies = definitions["autoupdatesettings"]!["properties"]!;
		JsonNode sharedEnabled = definitions["autoupdatepolicy"]!["properties"]!["enabled"]!;

		// Assert
		policies["clio"]!["default"]!["enabled"]!.GetValue<bool>().Should().BeFalse(
			because: "editor completion must not opt users into clio updates");
		policies["knowledge"]!["default"]!["enabled"]!.GetValue<bool>().Should().BeTrue(
			because: "knowledge updates remain enabled by default");
		policies["toolkit"]!["default"]!["enabled"]!.GetValue<bool>().Should().BeFalse(
			because: "editor completion must not opt users into toolkit updates");
		sharedEnabled["default"].Should().BeNull(
			because: "there is no single enabled default shared by all components");
	}

	[TestCase("{}", false, true, false)]
	[TestCase("{\"autoupdate\":null}", false, true, false)]
	[TestCase("{\"autoupdate\":{}}", false, true, false)]
	[TestCase("{\"autoupdate\":{\"clio\":{},\"knowledge\":{},\"toolkit\":{}}}", false, true, false)]
	[TestCase("{\"autoupdate\":{\"clio\":null,\"knowledge\":null,\"toolkit\":null}}", false, true, false)]
	[TestCase("{\"autoupdate\":true}", true, true, false)]
	[TestCase("{\"autoupdate\":false}", false, true, false)]
	[TestCase("{\"autoupdate\":{\"clio\":{\"enabled\":true},\"knowledge\":{\"enabled\":false},\"toolkit\":{\"enabled\":true}}}", true, false, true)]
	[Description("Defaults absent policies correctly and preserves explicit preferences through bootstrap, scheduling, and reload.")]
	public void TryScheduleAutoupdate_ShouldRespectDefaultsAndPreferences_WhenSettingsAreLoaded(
		string json, bool clioEnabled, bool knowledgeEnabled, bool toolkitEnabled) {
		// Arrange
		_fileSystem.File.WriteAllText(SettingsRepository.AppSettingsFile, json);
		SettingsRepository sut = new(_fileSystem);
		DateTimeOffset now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

		// Act
		bool clio = sut.TryScheduleAutoupdate(AutoUpdateTarget.Clio, now);
		bool knowledge = sut.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, now);
		bool toolkit = sut.TryScheduleAutoupdate(AutoUpdateTarget.Toolkit, now);
		Settings persisted = JsonConvert.DeserializeObject<Settings>(
			_fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));

		// Assert
		clio.Should().Be(clioEnabled, because: "clio updates require an opt-in");
		knowledge.Should().Be(knowledgeEnabled, because: "knowledge updates default on but respect an opt-out");
		toolkit.Should().Be(toolkitEnabled, because: "toolkit updates require an opt-in");
		persisted.Autoupdate.Clio.Enabled.Should().Be(clioEnabled, because: "persistence must retain the clio preference");
		persisted.Autoupdate.Knowledge.Enabled.Should().Be(knowledgeEnabled, because: "persistence must retain the knowledge preference");
		persisted.Autoupdate.Toolkit.Enabled.Should().Be(toolkitEnabled, because: "persistence must retain the toolkit preference");
	}

	[Test]
	[Description("Claims each due automatic update once and advances its independent next-run timestamp by the configured frequency.")]
	public void TryScheduleAutoupdate_ShouldAdvanceIndependentTimestamp_WhenPolicyIsDue() {
		// Arrange
		DateTimeOffset now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
		SettingsRepository sut = new(_fileSystem);

		// Act
		bool first = sut.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, now);
		bool repeated = new SettingsRepository(_fileSystem)
			.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, now.AddMinutes(59));
		bool toolkit = new SettingsRepository(_fileSystem)
			.TryScheduleAutoupdate(AutoUpdateTarget.Toolkit, now);
		Settings persisted = JsonConvert.DeserializeObject<Settings>(
			_fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));

		// Assert
		first.Should().BeTrue(because: "a missing next-run timestamp makes the enabled policy due immediately");
		repeated.Should().BeFalse(because: "the same policy must wait for its configured frequency");
		toolkit.Should().BeFalse(because: "toolkit updates are opt-in");
		persisted.Autoupdate.Knowledge.NextRun.Should().Be(now.AddMinutes(60),
			because: "knowledge uses its one-hour default frequency");
		persisted.Autoupdate.Toolkit.NextRun.Should().BeNull(
			because: "a schedule that never advanced stays unscheduled");
		_fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile).Should().NotContain("0001-01-01",
			because: "an unscheduled policy must be OMITTED from the file, not written as a year-0001 timestamp");
	}

	[Test]
	[Description("Leaves a disabled automatic update untouched even when its next-run timestamp is in the past.")]
	public void TryScheduleAutoupdate_ShouldNotAdvanceTimestamp_WhenPolicyIsDisabled() {
		// Arrange
		const string json = """
			{
			  "SettingsVersion": 2,
			  "autoupdate": {
			    "knowledge": {
			      "enabled": false,
			      "next-run": "2026-09-03T10:00:00+00:00"
			    }
			  },
			  "Environments": {}
			}
			""";
		_fileSystem.File.WriteAllText(SettingsRepository.AppSettingsFile, json);
		SettingsRepository sut = new(_fileSystem);
		string beforeSchedule = _fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);
		Settings normalized = JsonConvert.DeserializeObject<Settings>(beforeSchedule);

		// Act
		bool result = sut.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge,
			new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

		// Assert
		result.Should().BeFalse(because: "disabled policies do not run automatically");
		normalized.Autoupdate.Knowledge.FrequencyMinutes.Should().Be(60,
			because: "omitted frequencies use the component default");
		_fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile).Should().Be(beforeSchedule,
			because: "a skipped check must not rewrite appsettings.json");
	}

	[Test]
	[Description("Preserves legacy false when startup schedules knowledge before normal repairs run.")]
	public void TryScheduleAutoupdate_ShouldPreserveHistoricalFalse_WhenBootstrapRepairsAreDeferred() {
		// Arrange
		const string json = """
			{
			  "Autoupdate": false,
			  "Environments": {}
			}
			""";
		_fileSystem.File.WriteAllText(SettingsRepository.AppSettingsFile, json);
		SettingsRepository sut = new(_fileSystem, new SettingsBootstrapService(_fileSystem, applyRepairs: false));

		// Act
		sut.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge,
			new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
		SettingsRepository reloaded = new(_fileSystem);

		// Assert
		reloaded.GetAutoupdate().Should().BeFalse(
			because: "knowledge scheduling must not enable clio updates");
	}

	[Test]
	[Description("Refuses to write settings while a section could not be bound, so the degraded mode cannot round-trip away what a newer clio wrote.")]
	public void UpdateSettings_ShouldRefuseAndLeaveTheFileUntouched_WhenSectionCannotBind() {
		// Arrange
		const string futureShaped = """
			{
			  "ActiveEnvironmentKey": "dev",
			  "SettingsVersion": 3,
			  "autoupdate": {
			    "clio": { "enabled": { "future": true }, "frequency-minutes": 480 }
			  },
			  "Environments": {
			    "dev": { "Uri": "http://localhost", "Login": "Supervisor", "Password": "Supervisor" }
			  }
			}
			""";
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(futureShaped));
		SettingsRepository sut = new(fileSystem);

		// Act
		Action act = () => sut.SetAutoupdate(true);

		// Assert
		act.Should().Throw<SettingsShapeMismatchException>(
				because: "writing the file from a model that dropped an unbindable section would destroy the newer clio's settings")
			.Which.Message.Should().Contain("update-cli",
				because: "the refusal must name the way out, since it also blocks the automatic update that would have fixed the skew");
		fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile).Should().Be(futureShaped,
			because: "a refused update must leave the settings file byte-for-byte as it was");
	}

	[Test]
	[Description("Reports a due schedule without advancing or persisting next-run, so a deferral leaves the update due on the next cold start.")]
	public void IsAutoupdateDue_ShouldReportDue_WithoutAdvancingTheSchedule() {
		// Arrange
		const string json = """
			{
			  "SettingsVersion": 3,
			  "Environments": {},
			  "autoupdate": {
			    "clio": { "enabled": true, "frequency-minutes": 480, "next-run": "2020-01-01T00:00:00+00:00" }
			  }
			}
			""";
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(json));
		SettingsRepository sut = new(fileSystem);

		// Act
		bool due = sut.IsAutoupdateDue(AutoUpdateTarget.Clio, new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
		Settings persisted = JsonConvert.DeserializeObject<Settings>(
			fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));

		// Assert
		due.Should().BeTrue(
			because: "an enabled policy whose next-run is in the past is due");
		persisted.Autoupdate.Clio.NextRun.Should().Be(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
			because: "a read-only due check must never move the schedule the way TryScheduleAutoupdate does");
	}

	[Test]
	[Description("Reports a disabled schedule as not due regardless of its next-run.")]
	public void IsAutoupdateDue_ShouldReportNotDue_WhenPolicyIsDisabled() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);

		// Act
		bool due = sut.IsAutoupdateDue(AutoUpdateTarget.Toolkit, DateTimeOffset.UtcNow);

		// Assert
		due.Should().BeFalse(
			because: "toolkit updates are opt-in and a disabled policy is never due");
	}

	[Test]
	[Description("Preserves members a newer clio wrote that this build does not know, instead of deleting them on the next save.")]
	public void UpdateSettings_ShouldPreserveUnknownMembers_WrittenByANewerClio() {
		// Arrange
		const string original = """
			{
			  "ActiveEnvironmentKey": "dev",
			  "SettingsVersion": 3,
			  "future-section": { "mode": "on", "retries": 3 },
			  "autoupdate": {
			    "clio": { "enabled": false, "frequency-minutes": 480, "jitter-minutes": 7 },
			    "browser": { "enabled": true }
			  },
			  "Environments": {
			    "dev": { "Uri": "http://localhost", "Login": "Supervisor", "Password": "Supervisor",
			      "future-flag": true }
			  }
			}
			""";
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(original));
		SettingsRepository sut = new(fileSystem);

		// Act
		sut.SetAutoupdate(true);
		JObject persisted = JObject.Parse(fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));

		// Assert
		JToken.DeepEquals(persisted["future-section"], JObject.Parse("""{ "mode": "on", "retries": 3 }"""))
			.Should().BeTrue(
				because: "an unknown top-level section must survive a save by an older build, or that build silently deletes the newer one's configuration");
		persisted["Environments"]!["dev"]!["future-flag"]!.Value<bool>().Should().BeTrue(
			because: "an unknown environment member must survive too; losing one changes what commands do against that environment");
		persisted["autoupdate"]!["clio"]!["jitter-minutes"]!.Value<int>().Should().Be(7,
			because: "the autoupdate section binds through a custom converter, and its overflow members must round-trip like every other section's");
		JToken.DeepEquals(persisted["autoupdate"]!["browser"], JObject.Parse("""{ "enabled": true }"""))
			.Should().BeTrue(
				because: "a whole unknown policy added by a newer clio must be carried through unchanged");
		persisted["autoupdate"]!["clio"]!["enabled"]!.Value<bool>().Should().BeTrue(
			because: "carrying unknown members must not stop the write the caller actually asked for");
	}

	[Test]
	[Description("Does not duplicate a read-only member such as $schema, which Json.NET routes into the overflow bag because it cannot be set.")]
	public void UpdateSettings_ShouldNotDuplicateReadOnlyMembers_CarriedInTheOverflowBag() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "$schema": "./schema.json",
			  "ActiveEnvironmentKey": "dev",
			  "SettingsVersion": 3,
			  "Environments": {
			    "dev": { "Uri": "http://localhost", "Login": "Supervisor", "Password": "Supervisor" }
			  }
			}
			"""));
		SettingsRepository sut = new(fileSystem);

		// Act
		sut.SetAutoupdate(true);
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		Regex.Matches(persistedContent, Regex.Escape("\"$schema\"")).Count.Should().Be(1,
			because: "a member the type declares but cannot set must be written once from the property, never a second time from the overflow bag");
	}

	[Test]
	[Description("Keeps a value that merely LOOKS like a timestamp exactly as written, because clio re-serializes what it read and a re-formatted password stops authenticating.")]
	public void UpdateSettings_ShouldPreserveStringsThatLookLikeTimestamps() {
		// Arrange
		// Json.NET's default DateParseHandling converts any ISO-looking string into a DateTime while
		// PARSING - before anything knows which member it belongs to - so the value that comes back is a
		// re-formatted one, and the next save writes that instead of what the user configured.
		const string isoLookingSecret = "2026-09-12T10:00:00+00:00";
		string original = """
			{
			  "ActiveEnvironmentKey": "dev",
			  "SettingsVersion": 3,
			  "future-section": { "token": "__ISO__" },
			  "Environments": {
			    "dev": { "Uri": "http://localhost", "Login": "Supervisor", "Password": "__ISO__" }
			  }
			}
			""".Replace("__ISO__", isoLookingSecret);
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(original));
		SettingsRepository sut = new(fileSystem);

		// Act
		sut.SetAutoupdate(true);
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		Regex.Matches(persistedContent, Regex.Escape(isoLookingSecret)).Count.Should().Be(2,
			because: "both the credential and the unknown member must be written back byte-for-byte; a re-formatted password stops authenticating and clio never interprets an unknown member at all");
		sut.GetEnvironment("dev").Password.Should().Be(isoLookingSecret,
			because: "the value clio hands to an authentication call must be the value the file holds");
	}
}
