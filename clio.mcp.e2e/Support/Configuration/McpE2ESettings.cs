using System.Collections.Generic;

namespace Clio.Mcp.E2E.Support.Configuration;

internal sealed class McpE2ESettings {
	public bool AllowDestructiveMcpTests { get; set; }

	public string? ClioProcessPath { get; set; }

	public Dictionary<string, string?> ProcessEnvironmentVariables { get; set; } = new();

	public SandboxSettings Sandbox { get; set; } = new();
}

internal sealed class SandboxSettings {
	public string? EnvironmentName { get; set; }

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

	public string SeedKeyPrefix { get; set; } = "clio-mcp-e2e";
}
