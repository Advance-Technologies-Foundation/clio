namespace Clio.Common;

/// <summary>
/// Where the Dashboards Migrator app comes from. clio does not carry the package; it carries only the
/// coordinates of the feed that does, the way <see cref="Skills.ToolkitDistribution"/> carries the toolkit's
/// source rather than the toolkit. A new app release therefore needs no clio release.
/// </summary>
public static class DashboardsMigratorDistribution {

	#region Constants: Public

	/// <summary>
	/// Package name as published to the feed and as Creatio records it in <c>SysPackage</c>. The outcome
	/// verifier maps this name to the package's own Ping route.
	/// </summary>
	public const string PackageName = "CrtDashboardsMigratorApp";

	/// <summary>
	/// Label this app is reported under by <c>clio info</c> — the name an operator uses for it, not the
	/// package name Creatio stores.
	/// </summary>
	public const string DisplayName = "migrator";

	/// <summary>
	/// Feed used when the caller names none — the same default <c>check-nuget-update</c> uses, so both
	/// commands answer about the same feed unless the caller says otherwise.
	/// </summary>
	public const string DefaultFeedUrl = "https://www.nuget.org/api/v2";

	#endregion

}
