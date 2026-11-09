namespace Clio.Mcp.E2E.Support.Configuration;

/// <summary>
/// NUnit category names that a filter stored OUTSIDE this repository consumes, kept as shared constants so
/// the string has one compiler-checked definition.
/// </summary>
/// <remarks>
/// <para>
/// A category that is merely filtered by an in-repo lane can safely stay an inline literal, and most of
/// them do (<c>McpE2E.NoEnvironment</c> is written literally in ~98 fixtures - that convention is not
/// worth churning). This holder exists for the opposite case: a category whose filter is stored in a
/// TeamCity job's parameters, where nothing in this repository - not the build, not a test - can notice
/// that a fixture drifted out of the filter.
/// </para>
/// <para>
/// This deliberately holds NO feature-flag or skip logic. Its predecessor
/// (<c>ProcessDesignerE2EGate</c>) also skipped the process-designer fixtures when the
/// <c>process-designer</c> feature was off; that gate was deleted at the ENG-96132 go-live, because
/// after the toggle removal a features-map read reports "disabled" on every default install and would
/// silently skip the fixtures forever. Only the constant survives.
/// </para>
/// </remarks>
internal static class McpE2ECategories {

	/// <summary>
	/// Category carried by every E2E test that needs a live stand serving ProcessDesignService (the
	/// <c>CrtProcessBuilder</c> package): the process-designer fixtures, and the stand tests that build a
	/// process to exercise something else.
	/// </summary>
	/// <remarks>
	/// Both TeamCity lanes filter on it: <c>Team_Atf_ClioMcpE2eTests</c> EXCLUDES it (the default of its
	/// <c>McpE2eTestFilter</c>, mirrored as <c>baseFilter</c> in <c>TestSelection/mcp-e2e-selection.json</c>),
	/// and <c>TestLab_LoadTesting_ClioMcpTest_ClioMcpE2eProcessDesigner</c>, the lane that installs
	/// CrtProcessBuilder, runs ONLY it (<c>processDesignerLane.baseFilter</c>). See
	/// <c>docs/knowledge/infra/mcp-e2e-processdesigner-category-splits-the-suite-between-two-teamcity-lanes.md</c>:
	/// renaming this value needs both TeamCity defaults changed in the same change. To run the category
	/// deliberately, use a stand with CrtProcessBuilder and
	/// <c>dotnet test --filter "TestCategory=McpE2E.ProcessDesigner"</c>.
	/// </remarks>
	public const string ProcessDesigner = "McpE2E.ProcessDesigner";
}
