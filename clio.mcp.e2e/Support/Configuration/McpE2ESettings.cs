using System.Collections.Generic;

namespace Clio.Mcp.E2E.Support.Configuration;

internal sealed class McpE2ESettings {
	public bool SuppressCuratedKnowledgeBootstrap { get; set; } = true;

	public bool AllowDestructiveMcpTests { get; set; }

	public string? ClioProcessPath { get; set; }

	public Dictionary<string, string?> ProcessEnvironmentVariables { get; set; } = new();

	public SandboxSettings Sandbox { get; set; } = new();

	public DataForgeSettings DataForge { get; set; } = new();
}

internal sealed class DataForgeSettings {
	/// <summary>
	/// When true, the DataForge similarity-search E2E fixtures (find-tables, find-lookups,
	/// get-relations) run a one-time arrange step that invokes the destructive
	/// <c>dataforge-initialize</c> tool and polls <c>dataforge-status</c> until the similarity
	/// index is built before asserting. Off by default so non-DataForge runs and stands whose
	/// index is already warm are unaffected, and so the destructive initialize stays opt-in
	/// (mirrors <see cref="McpE2ESettings.AllowDestructiveMcpTests"/>). Set in CI via
	/// <c>McpE2E__DataForge__InitializeAndWait=true</c>.
	/// </summary>
	public bool InitializeAndWait { get; set; }
}

internal sealed class SandboxSettings {
	public string? EnvironmentName { get; set; }

	/// <summary>
	/// Explicit IIS application-pool name for the disposable uninstall sandbox. TeamCity resolves this
	/// from its <c>ApplicationPoolName</c> build parameter because the externally routed environment URL
	/// does not necessarily match the agent's local IIS bindings or application path.
	/// </summary>
	public string? ApplicationPoolName { get; set; }

	/// <summary>Archive used by the opt-in destructive deploy/uninstall lifecycle proof.</summary>
	public string? DeploymentArchivePath { get; set; }

	/// <summary>Explicit disposable IIS port used by the lifecycle proof.</summary>
	public int DeploymentSitePort { get; set; }

	/// <summary>Configured local database server used by the lifecycle proof.</summary>
	public string? DeploymentDbServerName { get; set; }

	/// <summary>Configured local Redis server used by the lifecycle proof.</summary>
	public string? DeploymentRedisServerName { get; set; }

	/// <summary>Requires deploy and uninstall to prove the real offline-dbHub warning contract.</summary>
	public bool RequireDbHubWarning { get; set; }

	/// <summary>Optional secret value that must not appear in MCP results or progress.</summary>
	public string? SecretSentinel { get; set; }

	/// <summary>
	/// Requires the destructive uninstall sandbox to assert the conditional dbHub source-removal stage.
	/// Enable only with an isolated CLIO_HOME whose dbHub integration is configured for the disposable environment.
	/// </summary>
	public bool RequireDbHubLifecycle { get; set; }

	/// <summary>
	/// When set, the harness re-registers the sandbox env at this URL via reg-web-app before tests,
	/// so it targets the freshly-deployed stand instead of a stale registration.
	/// In CI set via <c>McpE2E__Sandbox__EnvironmentUrl=%DeployedUrl%</c>.
	/// </summary>
	public string? EnvironmentUrl { get; set; }

	/// <summary>
	/// Absolute path to the Creatio installation root for the sandbox environment.
	/// Required by ClearRedis and other tests that read ConnectionStrings.config.
	/// Set via McpE2E__Sandbox__EnvironmentPath environment variable in CI,
	/// or ensure the clio environment is registered with --environment-path.
	/// </summary>
	public string? EnvironmentPath { get; set; }

	/// <summary>
	/// Database provider used by provider-specific sandbox assertions. Set to <c>postgresql</c>
	/// through <c>McpE2E__Sandbox__DatabaseProvider</c> to enable PostgreSQL catalog checks.
	/// </summary>
	public string? DatabaseProvider { get; set; }

	public string? ProcessCode { get; set; }

	public string? ApplicationPackagePath { get; set; }

	public string? PackageName { get; set; }

	public string SeedKeyPrefix { get; set; } = "clio-mcp-e2e";
}
