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
	/// Admin-gated <c>ApplicationInfoService</c> operation (requires the <c>CanManageSolution</c>
	/// system operation) that returns sanitized environment info — <c>dbEngineType</c>,
	/// <c>frameworkKind</c> (.NET Framework vs .NET), <c>frameworkDescription</c> and <c>coreVersion</c>
	/// — WITHOUT the cliogate package, on both runtimes (ENG-92465). Lets clio surface the database
	/// engine and executing framework on environments where cliogate is not installed.
	/// </summary>
	public const string GetSystemEnvironmentInfo = "/ServiceModel/ApplicationInfoService.svc/GetSystemEnvironmentInfo";

	/// <summary>
	/// Standard Creatio service that returns the authenticated user's runtime profile details,
	/// including the message-channel session id used by Designer Presence.
	/// </summary>
	public const string GetCurrentUserInfo = "/ServiceModel/UserInfoService.svc/GetCurrentUserInfo";

	/// <summary>
	/// cliogate API gateway endpoint that returns <c>SysInfo</c> (incl. <c>CoreVersion</c>, runtime,
	/// db engine, license). Requires the cliogate package to be installed.
	/// </summary>
	public const string GetSysInfo = "/rest/CreatioApiGateway/GetSysInfo";
}
