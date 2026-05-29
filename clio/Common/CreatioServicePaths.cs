namespace Clio.Common;

/// <summary>
/// Well-known Creatio service endpoint paths shared across commands and MCP tools.
/// Centralised so a path change lands in exactly one place rather than drifting between
/// the CLI verbs and the MCP resolver that call the same services.
/// </summary>
public static class CreatioServicePaths {
	/// <summary>
	/// Standard Creatio service that returns core/system metadata (incl. <c>sysValues.coreVersion</c>).
	/// Requires only an authenticated session — no cliogate package.
	/// </summary>
	public const string GetApplicationInfo = "/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo";

	/// <summary>
	/// cliogate API gateway endpoint that returns <c>SysInfo</c> (incl. <c>CoreVersion</c>, runtime,
	/// db engine, license). Requires the cliogate package to be installed.
	/// </summary>
	public const string GetSysInfo = "/rest/CreatioApiGateway/GetSysInfo";
}
