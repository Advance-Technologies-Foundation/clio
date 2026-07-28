namespace Clio.Command.RelatedPages;

/// <summary>
/// User-facing validation messages shared by the related-page MCP tools and the service, so the
/// required-field wording stays identical across the CLI and MCP surfaces. Mirrors the
/// <see cref="PageSchemaMetadataHelper.SchemaNameFormatError"/> pattern (no duplicated user-facing
/// string literals across call sites).
/// </summary>
internal static class RelatedPageAddonMessages {
	internal const string EntitySchemaNameRequired = "entity-schema-name is required.";
	internal const string PackageNameRequired = "package-name is required.";

	// The pages list itself. Shared by the MCP tool's argument guard and the service's ValidateRequest, which
	// previously carried near-identical inline literals (one with a trailing period, one without) — centralized
	// here so the CLI and MCP wording cannot drift.
	internal const string PagesRequired = "pages is required (send an empty list to clear all bindings / reset to inline)";
	internal const string PagesEntryRequired = "each entry in pages is required (a null pages entry was provided)";

	// CLI scalar-option pairing (RelatedPageSpecBuilder): an add page needs its matching default page, and a
	// no-option invocation is rejected rather than silently wiping the configuration.
	internal const string AddPageRequiresDefaultPage =
		"--add-page requires --default-page: the internal audience needs a default page to open a record, not just an add page.";
	internal const string PortalAddPageRequiresPortalDefaultPage =
		"--portal-add-page requires --portal-default-page: the portal audience needs a default page to open a record, not just an add page.";
	internal const string NoPageOptionsSpecified =
		"No pages specified. Provide --default-page (optionally with --add-page, --portal-default-page, or --portal-add-page). To clear all bindings (reset to inline), call the MCP tool with an empty pages list.";
}
