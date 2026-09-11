namespace Clio.Mcp.E2E;

/// <summary>
/// Decides whether an arrange step that mutates a Creatio stand may run.
/// </summary>
/// <remarks>
/// <c>[Explicit]</c> plus the CI guard keep the destructive fixtures out of every automatic lane,
/// but neither of them stops a developer who selects such a fixture by hand while
/// <c>McpE2E__AllowDestructiveMcpTests=false</c>. The decision is a pure function so the invariant
/// - a stand-touching arrange runs only under the explicit opt-in - is covered off-stand by
/// <c>Clio.Tests.McpFixturePolicyTests</c> rather than only by the fixtures that never run in CI.
/// </remarks>
public static class DestructiveStandAuthorization {

	/// <summary>
	/// The message every fixture uses when it ignores itself for a missing opt-in, so the reason a
	/// run was skipped names the switch that turns it on.
	/// </summary>
	public const string MissingOptInMessage =
		"AllowDestructiveMcpTests is false - skipping an arrange step that pushes a package, "
		+ "publishes configuration and deletes them again on the configured sandbox. Set "
		+ "McpE2E__AllowDestructiveMcpTests=true to run it.";

	/// <summary>
	/// True when the arrange step may proceed: either it never touches a stand, or the destructive
	/// opt-in is on.
	/// </summary>
	/// <param name="touchesStand">Whether the arrange step reaches a Creatio environment at all.</param>
	/// <param name="allowDestructiveMcpTests">The <c>McpE2E:AllowDestructiveMcpTests</c> setting.</param>
	public static bool IsAuthorized(bool touchesStand, bool allowDestructiveMcpTests) =>
		!touchesStand || allowDestructiveMcpTests;
}
