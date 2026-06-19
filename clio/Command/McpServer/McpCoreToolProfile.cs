using System;
using System.Collections.Generic;
using Clio.Command.McpServer.Tools;

namespace Clio.Command.McpServer;

/// <summary>
/// The lazy-mode MCP tool profile: the declaring <c>[McpServerToolType]</c> classes kept flat in
/// <c>tools/list</c> when the <c>mcp-lazy-tools</c> feature is enabled.
/// </summary>
/// <remarks>
/// <para>
/// Membership is defined per-TYPE (ADR resolved decision #5, Option A): a whole tool-type class is
/// kept flat, even if it happens to declare a long-tail tool method too — that is negligible against
/// the measured −97% context win and avoids per-method registration that would diverge from the SDK's
/// <c>WithTools(types)</c> scan.
/// </para>
/// <para>
/// <b>Provisional.</b> This is the migration-inventory's proposed ~20-tool core set
/// (<c>spec/mcp-lazy-schema/mcp-lazy-schema-migration-inventory.md</c> §2); Story 7 finalises the
/// exact core membership and index taxonomy. Update this list there, not by scattering constants.
/// </para>
/// <para>
/// The three always-on lazy-mode types (<see cref="Tools.ClioRunTool"/>,
/// <see cref="Tools.ClioRunDestructiveTool"/>, and <see cref="Tools.ToolContractGetTool"/>) are NOT
/// listed here as "core commands"; they are the long-tail entry points and discovery surface that
/// keep the omitted long-tail reachable. They are unioned in at the registration filter.
/// </para>
/// </remarks>
public static class McpCoreToolProfile {

	/// <summary>
	/// The feature key (ADR resolved decision #4) that opts a consumer into lazy MCP mode. OFF by
	/// default ⇒ full flat catalog (unchanged). Compared case-insensitively, like all feature keys.
	/// Toggle with <c>clio experimental --name mcp-lazy-tools --enable</c> / <c>--disable</c>.
	/// </summary>
	public const string FeatureKey = "mcp-lazy-tools";

	/// <summary>
	/// The core flat tool-type classes for lazy mode. Each maps to one of the proposed core tool
	/// names in the migration inventory (several core tools share a declaring class, so this set is
	/// the deduplicated set of their <c>[McpServerToolType]</c> classes).
	/// </summary>
	public static readonly IReadOnlyCollection<Type> CoreToolTypes = new HashSet<Type> {
		// app discovery / inspection
		typeof(ApplicationGetListTool),            // list-apps
		typeof(ApplicationGetInfoTool),            // get-app-info
		typeof(FindAppTool),                       // find-app
		typeof(ApplicationSectionGetListTool),     // list-app-sections

		// page discovery / read / validation
		typeof(PageListTool),                      // list-pages
		typeof(PageGetTool),                       // get-page
		typeof(PageValidateTool),                  // validate-page

		// package / environment discovery
		typeof(GetPkgListTool),                    // list-packages
		typeof(ShowWebAppListTool),                // list-environments

		// Freedom UI component lookup (ENG-89871 hot path)
		typeof(ComponentInfoTool),                 // get-component-info

		// entity-schema lookup / inspection
		typeof(FindEntitySchemaTool),              // find-entity-schema
		typeof(GetEntitySchemaPropertiesTool),     // get-entity-schema-properties
		typeof(GetEntitySchemaColumnPropertiesTool), // get-entity-schema-column-properties

		// Data Forge discovery (dataforge-find-tables / dataforge-find-lookups / dataforge-status)
		typeof(DataForgeTool),

		// guidance index + lazy-schema describe tool
		typeof(GuidanceGetTool),                   // get-guidance
		typeof(ToolContractGetTool),               // get-tool-contract

		// system-setting read / discovery
		typeof(SysSettingGetTool),                 // get-sys-setting
		typeof(SysSettingsListTool),               // list-sys-settings
	};

	/// <summary>
	/// The executor / contract tool-type classes always kept flat in lazy mode so the omitted
	/// long tail stays reachable (<c>clio-run</c> / <c>clio-run-destructive</c>) and discoverable
	/// (<c>get-tool-contract</c>). <see cref="ToolContractGetTool"/> is also a core member; the union
	/// at the filter deduplicates it.
	/// </summary>
	public static readonly IReadOnlyCollection<Type> AlwaysOnLazyToolTypes = new HashSet<Type> {
		typeof(ClioRunTool),
		typeof(ClioRunDestructiveTool),
		typeof(ToolContractGetTool),
	};
}
