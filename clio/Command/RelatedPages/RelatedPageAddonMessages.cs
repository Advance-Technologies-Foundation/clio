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
}
