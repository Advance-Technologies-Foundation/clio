using System.Collections.Generic;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Configuration;

internal sealed class McpE2ESettings {
	public bool SuppressCuratedKnowledgeBootstrap { get; set; } = true;

	public bool AllowDestructiveMcpTests { get; set; }

	public string? ClioProcessPath { get; set; }

	public Dictionary<string, string?> ProcessEnvironmentVariables { get; set; } = new();

	/// <summary>
	/// Overrides the <c>clientInfo</c> (Name/Version) that <see cref="Mcp.McpServerSession.StartAsync(McpE2ESettings,System.Threading.CancellationToken)"/>
	/// sends during the MCP "initialize" handshake. <c>null</c> (the default) keeps the harness's
	/// standard <c>clio.mcp.e2e</c>/<c>1.0.0</c> identity so every existing fixture is unaffected; set
	/// this (for example from <see cref="Mcp.McpContractFixtureBase.ConfigureMcpServerSettings"/>) to
	/// impersonate a specific real-world MCP client for tests that assert client-identity-dependent
	/// server behavior (ENG-93885).
	/// </summary>
	public Implementation? ClientInfo { get; set; }

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

	/// <summary>
	/// Classic section schema on the sandbox whose live body DOES declare static list columns, used by
	/// get-classic-list-columns to prove the <c>schema-default</c> branch against a real Classic body.
	/// OPT-IN (the test self-ignores when unset): which sections declare static columns is a fact about the
	/// SEEDING, not about the product, and no base Studio section is known to declare them — the CI stand
	/// resolves <c>ContactSectionV2</c> to <c>entity-default</c>, so defaulting to it asserted a false premise
	/// and turned the build red rather than skipping. Point
	/// <c>McpE2E__Sandbox__ClassicSchemaDefaultSectionSchema</c> at a section the stand really seeds with a
	/// <c>getGridDataColumns</c> / <c>initColumnsConfig</c> override.
	/// </summary>
	public string? ClassicSchemaDefaultSectionSchema { get; set; }

	/// <summary>
	/// Classic section schema on the sandbox that was never configured with static list columns, used by
	/// get-classic-list-columns to prove the <c>entity-default</c> branch discriminates from
	/// <c>schema-default</c>. Defaults to <c>ContactSectionV2</c>: the base Studio Contact section declares no
	/// static list columns, which the sandbox run confirms by resolving it to <c>entity-default</c> with the
	/// resolver's own "does not define static list columns" note and no skipped layers. Override through
	/// <c>McpE2E__Sandbox__ClassicEntityDefaultSectionSchema</c> on a stand where Contact IS configured.
	/// </summary>
	public string? ClassicEntityDefaultSectionSchema { get; set; } = "ContactSectionV2";

	public string SeedKeyPrefix { get; set; } = "clio-mcp-e2e";
}
