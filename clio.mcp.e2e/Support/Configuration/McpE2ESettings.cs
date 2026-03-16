namespace Clio.Mcp.E2E.Support.Configuration;

internal sealed class McpE2ESettings {
	public bool AllowDestructiveMcpTests { get; set; }

	public string? ClioProcessPath { get; set; }

	public SandboxSettings Sandbox { get; set; } = new();
}

internal sealed class SandboxSettings {
	public string? EnvironmentName { get; set; }

	public string? ProcessCode { get; set; }

	public string? ApplicationPackagePath { get; set; }

	public string SeedKeyPrefix { get; set; } = "clio-mcp-e2e";
}
