namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// Page bodies that are syntactically valid, carry every schema marker, and pass the regex
/// validators - only the AST lint pass rejects them. Shared by the write-path suites so the
/// update-page and sync-pages gate scenarios exercise byte-identical input.
/// </summary>
internal static class PageLintProbeBodies {

	/// <summary>
	/// A body whose returned handler calls a helper the factory declares only inside a branch that
	/// never runs. The name hoists, so the binding exists; nothing ever stores the function in it, so
	/// the handler throws a TypeError on the deployed page. Rejected with `undefined-section-call`.
	/// </summary>
	public static string ConditionallyDeclaredHelper(string schemaName) =>
		$"define(\"{schemaName}\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { " +
		"if (false) { function lintProbeHelper() { return 1; } } " +
		"return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelInitRequest\", " +
		"handler: async (request, next) => { lintProbeHelper(); return next?.handle(request); } }]" +
		"/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

}
