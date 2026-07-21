using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// The MCP tool profile: the declaring <c>[McpServerToolType]</c> classes kept flat in
/// <c>tools/list</c>. This is the only tool surface clio's MCP server exposes — the long-tail
/// schemas are reached via <c>clio-run</c> / <c>clio-run-destructive</c> and discovered via
/// <c>get-tool-contract</c>.
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
	/// The core flat tool-type classes. Each maps to one of the proposed core tool
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

		// Freedom UI request lookup (ENG-93187)
		typeof(RequestInfoTool),                   // get-request-info

		// entity-schema lookup / inspection
		typeof(FindEntitySchemaTool),              // find-entity-schema
		typeof(GetEntitySchemaPropertiesTool),     // get-entity-schema-properties
		typeof(GetEntitySchemaColumnPropertiesTool), // get-entity-schema-column-properties

		// NOTE: DataForgeTool was moved OUT of the resident profile (ENG-92761). Data Forge is a
		// niche path — the whole 8-method class cost ~19% of the tools/list payload (~1.5k tokens)
		// on every session, resident-per-TYPE, even sessions that never touch the database. Its
		// read-only discovery tools stay reachable+discoverable via the get-tool-contract curated
		// index (CanonicalToolNames) and clio-run; on the stdio host a direct call also still works
		// through the durable unmatched-name handler (ENG-93370). No canonical name changed, so no
		// McpToolCompatibilityCatalog entry is needed.

		// guidance index + lazy-schema describe tool
		typeof(GuidanceGetTool),                   // get-guidance
		typeof(ToolContractGetTool),               // get-tool-contract

		// NOTE: SysSettingGetTool / SysSettingsListTool were moved OUT of the resident profile. Each is
		// a single-method class (no destructive ride-along, unlike the DataForgeTool precedent), and
		// system-setting read/discovery is a niche path relative to app/page/entity discovery. Both
		// tool names are already in the get-tool-contract curated index (CanonicalToolNames,
		// independent of residency), so read discovery is unaffected; reachable via clio-run. No
		// canonical name changed, so no McpToolCompatibilityCatalog entry is needed.
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

	/// <summary>
	/// Every MCP tool NAME declared on a resident type (<see cref="CoreToolTypes"/> ∪
	/// <see cref="AlwaysOnLazyToolTypes"/>), derived by reflecting each type's <c>[McpServerTool]</c>
	/// methods for their declared <c>Name</c>. Backs <see cref="IsResident"/>, which the compact index
	/// (<c>get-tool-contract</c>) uses to populate the <c>resident</c> flag: <c>true</c> for a tool
	/// called natively because it is present in <c>tools/list</c>, <c>false</c> for a long-tail tool
	/// reachable only through <c>clio-run</c> / <c>clio-run-destructive</c>.
	/// </summary>
	/// <remarks>
	/// The <see cref="BindingFlags"/> mirror <c>McpToolInvokerRegistry.EnumerateToolMethods</c> exactly
	/// (public + non-public, instance + static, no <c>DeclaredOnly</c>) so residency never diverges from
	/// what the SDK's own <c>WithTools(types)</c> scan would register for these types.
	/// </remarks>
	public static readonly IReadOnlyCollection<string> ResidentToolNames = BuildResidentToolNames();

	/// <summary>
	/// True when <paramref name="toolName"/> is declared on a resident tool-type (see
	/// <see cref="ResidentToolNames"/>); false for a long-tail tool reachable only via <c>clio-run</c> /
	/// <c>clio-run-destructive</c>.
	/// </summary>
	/// <param name="toolName">The MCP tool name to classify.</param>
	public static bool IsResident(string toolName) {
		return !string.IsNullOrWhiteSpace(toolName) && ResidentToolNames.Contains(toolName);
	}

	private static IReadOnlyCollection<string> BuildResidentToolNames() {
		// Sonar S3011: BindingFlags.NonPublic is a deliberate, required accessibility bypass — NOT a leak.
		// This mirrors McpToolInvokerRegistry.EnumerateToolMethods exactly (no DeclaredOnly) so residency
		// never diverges from what the SDK's own WithTools(types) scan would register for these types; the
		// reflected members are only filtered for [McpServerTool].Name and no private state is read or mutated.
#pragma warning disable S3011
		const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
			BindingFlags.Instance | BindingFlags.Static;
#pragma warning restore S3011
		HashSet<Type> residentTypes = new(CoreToolTypes);
		residentTypes.UnionWith(AlwaysOnLazyToolTypes);
		return residentTypes
			.SelectMany(type => type.GetMethods(flags))
			.Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
	}
}
