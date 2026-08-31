namespace Clio.Mcp.E2E.Support.Configuration;

/// <summary>
/// NUnit category names whose only consumer lives OUTSIDE this repository, kept as shared constants so
/// the string has one compiler-checked definition.
/// </summary>
/// <remarks>
/// <para>
/// A category that is merely filtered by an in-repo lane can safely stay an inline literal, and most of
/// them do (<c>McpE2E.NoEnvironment</c> is written literally in ~98 fixtures - that convention is not
/// worth churning). This holder exists for the opposite case: a category whose exclusion filter is
/// stored in a TeamCity job's arguments, where nothing in this repository - not the build, not a test -
/// can notice that a fixture drifted out of the filter.
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
	/// Category carried by every process-designer E2E fixture that needs a live stand serving
	/// ProcessDesignService (the <c>CrtProcessBuilder</c> package), which the default CI stand does not
	/// provide.
	/// </summary>
	/// <remarks>
	/// Excluded at the runner level by the TeamCity job <c>Team_Atf_ClioMcpE2eTests</c> (step
	/// <c>Run_MCP_e2e_tests</c>) via <c>--filter "TestCategory!=McpE2E.ProcessDesigner"</c>. See
	/// <c>docs/knowledge/infra/mcp-e2e-processdesigner-category-is-consumed-only-by-teamcity-job-args.md</c>:
	/// renaming this value looks safe from inside the repository and silently re-admits ~59 permanently
	/// ignored tests into every plan run. To run the suite deliberately, use a stand with
	/// CrtProcessBuilder and <c>dotnet test --filter "TestCategory=McpE2E.ProcessDesigner"</c>.
	/// </remarks>
	public const string ProcessDesigner = "McpE2E.ProcessDesigner";
}
