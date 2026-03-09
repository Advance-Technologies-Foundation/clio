namespace Clio.Mcp.E2E.Support.Configuration;

internal sealed record SandboxEnvironmentContext(
	string EnvironmentName,
	string Uri,
	string Login,
	string Password,
	bool IsNetCore,
	string EnvironmentPath,
	string ConnectionStringsPath,
	string RedisConnectionString,
	string DatabaseConnectionString);
