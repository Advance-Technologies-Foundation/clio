using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;

namespace Clio.Mcp.E2E;

/// <summary>
/// Assembly-wide one-time guard that migrates the shared clio home exactly once, serially,
/// before any parallel fixture starts.
/// </summary>
/// <remarks>
/// Vetted <see cref="McpContractFixtureBase"/> fixtures that do NOT override
/// <see cref="McpContractFixtureBase.ConfigureMcpServerSettings"/> (PackageHotfix,
/// AddPackageDependency, CompileCreatio, DeployCreatio, RestoreDb, DownloadConfiguration and the
/// <c>*ContractToolE2ETests</c> cohort) let the child <c>clio mcp-server</c> inherit the runner's
/// global <c>CLIO_HOME</c>. Under <c>NumberOfTestWorkers=2</c> with <c>[Parallelizable]</c>, two such
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
/// The fixture also temporarily configures the built-in curated source as disabled and restores the
/// exact original file at assembly teardown. This prevents an external GitHub clone from becoming a
/// hidden prerequisite of unrelated MCP mechanics tests while preserving the runner's environments.
/// Fixtures that use an isolated home (OAuth, the canaries) are unaffected: they write their own
/// per-fixture home and never touch the global one.
///
/// This is a <see cref="SetUpFixtureAttribute"/> in the root <c>Clio.Mcp.E2E</c> namespace, so its
/// one-time setup runs before every fixture in the assembly regardless of parallel scope.
/// </remarks>
[SetUpFixture]
public sealed class McpSharedHomeSetUpFixture {
	private TemporaryClioSettingsOverride? _curatedSourceOverride;

	[OneTimeSetUp]
	public async Task MigrateSharedClioHomeOnceAsync() {
		// Mirror McpContractFixtureBase.StartSharedMcpServerAsync with the default (non-overridden)
		// settings so this server resolves the SAME global home the non-isolated fixtures use.
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		_curatedSourceOverride = TemporaryClioSettingsOverride.DisableCuratedKnowledgeBootstrap(
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		using CancellationTokenSource startupCts = new(TimeSpan.FromMinutes(5));
		// Completing the MCP initialize handshake proves the child booted, which is when it resolves
		// settings and performs the one-time create/migrate write against the shared home.
		await using McpServerSession session = await McpServerSession.StartAsync(settings, startupCts.Token);
	}

	[OneTimeTearDown]
	public void RestoreSharedClioHome() {
		_curatedSourceOverride?.Dispose();
		_curatedSourceOverride = null;
	}
}
