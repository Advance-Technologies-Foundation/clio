using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Mcp.E2E;

/// <summary>
/// Assembly-wide guard that copies the runner's Clio settings into a suite-owned home and migrates
/// that isolated copy exactly once before any parallel fixture starts.
/// </summary>
/// <remarks>
/// Vetted <see cref="McpContractFixtureBase"/> fixtures that do NOT override
/// <see cref="McpContractFixtureBase.ConfigureMcpServerSettings"/> (PackageHotfix,
/// AddPackageDependency, CompileCreatio, DeployCreatio, RestoreDb, DownloadConfiguration and the
/// <c>*ContractToolE2ETests</c> cohort) let the child <c>clio mcp-server</c> inherit the runner's
/// suite-owned <c>CLIO_HOME</c>. Under <c>NumberOfTestWorkers=2</c> with <c>[Parallelizable]</c>, two such
/// fixtures' <c>[OneTimeSetUp]</c> would otherwise start two servers concurrently, and each resolves
/// settings via <c>SettingsBootstrapService.Load()</c> with repairs enabled — which WRITES
/// <c>appsettings.json</c> when the shared home is missing (first run) or legacy
/// (<c>SettingsVersion &lt; 1</c> migration). That write holds only an in-process lock, so two
/// processes on a fresh/legacy shared home can hit a sharing-violation IOException or a torn read
/// and corrupt the very invalid-environment error text those fixtures assert on.
///
/// Starting one server here first performs that create/migrate once while nothing else runs, so by
/// the time the parallel cohort starts the shared home is present and already at the current
/// settings version — every later load is read-only and the cross-process write race never opens.
/// The child-session harness configures the built-in curated source as disabled in the isolated
/// copy. This prevents an external GitHub clone from becoming a hidden prerequisite while the
/// runner's real settings file is never modified or restored.
/// Fixtures that use an isolated home (OAuth, the canaries) are unaffected: they write their own
/// per-fixture home and never touch the global one.
///
/// This is a <see cref="SetUpFixtureAttribute"/> in the root <c>Clio.Mcp.E2E</c> namespace, so its
/// one-time setup runs before every fixture in the assembly regardless of parallel scope.
/// </remarks>
[SetUpFixture]
public sealed class McpSharedHomeSetUpFixture {
	private string? _sharedClioHome;
	private string? _isolatedSettingsPath;

	[OneTimeSetUp]
	public async Task MigrateSharedClioHomeOnceAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string sourceSettingsPath = TemporaryClioSettingsOverride.GetClioAppSettingsPath(
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		_sharedClioHome = Path.Combine(Path.GetTempPath(), $"clio-mcp-e2e-shared-home-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_sharedClioHome);
		ProtectDirectoryForCurrentUser(_sharedClioHome);
		_isolatedSettingsPath = Path.Combine(_sharedClioHome, "appsettings.json");
		string sourceContent = File.Exists(sourceSettingsPath) ? File.ReadAllText(sourceSettingsPath) : "{}";
		JsonObject root = JsonNode.Parse(sourceContent)?.AsObject() ?? new JsonObject();
		root["knowledge"] = new JsonObject {
			["root-path"] = Path.Combine(_sharedClioHome, "knowledge"),
			["sources"] = new JsonObject()
		};
		EnableSuiteFeatures(root);
		File.WriteAllText(
			_isolatedSettingsPath,
			root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
		ProtectFileForCurrentUser(_isolatedSettingsPath);
		TestConfiguration.UseSharedClioHome(_sharedClioHome);
		settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource startupCts = new(TimeSpan.FromMinutes(5));
		// Completing the MCP initialize handshake proves the child booted, which is when it resolves
		// settings and performs the one-time create/migrate write against the shared home.
		await using McpServerSession session = await McpServerSession.StartAsync(settings, startupCts.Token);
	}

	[OneTimeTearDown]
	public void RestoreSharedClioHome() {
		TestConfiguration.ClearSharedClioHome();
		DeleteSensitiveSettingsFile();
		if (!string.IsNullOrWhiteSpace(_sharedClioHome) && Directory.Exists(_sharedClioHome)) {
			try {
				Directory.Delete(_sharedClioHome, recursive: true);
			} catch (IOException) {
			} catch (UnauthorizedAccessException) {
			}
		}
		_sharedClioHome = null;
		_isolatedSettingsPath = null;
	}

	/// <summary>
	/// Forces the feature flags the suite's own fixtures depend on ON in the suite-owned copy, carrying
	/// every other flag over unchanged.
	/// </summary>
	/// <remarks>
	/// A tool carrying <c>[FeatureToggle(...)]</c> is not registered on the MCP surface while its flag is
	/// off, and flags live in the clio home. Leaving them to the runner's machine made
	/// <c>MobilePageConversionGuideSandboxE2ETests</c> run or skip according to which TeamCity agent picked
	/// up the build — ignored in 43 of 55 runs, executed in 12, with no build parameter separating the two
	/// groups (issue #1382). Deciding it here, in the one home the suite already owns and repairs, is what
	/// makes the outcome a property of the code rather than of the machine. The fixtures that must observe
	/// a flag OFF (see <c>MobilePageConversionGuideToolE2ETests</c>) write their own home and are unaffected.
	/// The keys are removed case-insensitively first because Newtonsoft matches <c>features</c> as a
	/// case-insensitive fallback, so a hand-edited <c>"Features"</c> would otherwise shadow this one.
	/// </remarks>
	private static void EnableSuiteFeatures(JsonObject root) {
		JsonObject features = new();
		foreach (string key in root.Select(property => property.Key)
			.Where(key => string.Equals(key, "features", StringComparison.OrdinalIgnoreCase))
			.ToList()) {
			if (root[key] is JsonObject existing) {
				foreach (KeyValuePair<string, JsonNode?> flag in existing) {
					features[flag.Key] = flag.Value?.DeepClone();
				}
			}
			root.Remove(key);
		}
		features["mobile-page-converter"] = true;
		root["features"] = features;
	}

	private static void ProtectDirectoryForCurrentUser(string path) {
		if (!OperatingSystem.IsWindows()) {
			File.SetUnixFileMode(path,
				UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
	}

	private static void ProtectFileForCurrentUser(string path) {
		if (!OperatingSystem.IsWindows()) {
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}
	}

	private void DeleteSensitiveSettingsFile() {
		if (string.IsNullOrWhiteSpace(_isolatedSettingsPath)) {
			return;
		}
		for (int attempt = 0; attempt < 3 && File.Exists(_isolatedSettingsPath); attempt++) {
			try {
				File.Delete(_isolatedSettingsPath);
			} catch (IOException) when (attempt < 2) {
				Thread.Sleep(50);
			} catch (UnauthorizedAccessException) when (attempt < 2) {
				Thread.Sleep(50);
			}
		}
		if (File.Exists(_isolatedSettingsPath)) {
			throw new IOException($"Unable to remove the E2E settings copy at '{_isolatedSettingsPath}'.");
		}
	}
}
