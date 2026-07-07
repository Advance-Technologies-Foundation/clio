using System.Collections.Generic;

namespace Clio.Mcp.E2E.Support.Configuration;

internal sealed class McpE2ESettings {
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

	public string? ProcessCode { get; set; }

	public string? ApplicationPackagePath { get; set; }

	public string? PackageName { get; set; }

	/// <summary>
	/// Optional Freedom UI form page schema name used as the target for the page business-rule
	/// sandbox tests. When set, target resolution pins to this page instead of discovering one
	/// (Contacts_FormPage first, then seeded custom form pages) — needed on stands where every
	/// discoverable page's BusinessRule addon schema lives in a locked OOTB package that
	/// SaveSchema refuses to layer over. Set via <c>McpE2E__Sandbox__PageSchemaName</c>.
	/// When unset, the default discovery behavior is unchanged.
	/// </summary>
	public string? PageSchemaName { get; set; }

	public string SeedKeyPrefix { get; set; } = "clio-mcp-e2e";
}
