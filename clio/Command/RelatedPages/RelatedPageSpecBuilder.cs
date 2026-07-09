using System;
using System.Collections.Generic;

namespace Clio.Command.RelatedPages;

/// <summary>
/// Builds the related-page spec list from the scalar CLI options of <c>create-related-page-addon</c>
/// (default/add page per audience). Pure, side-effect-free domain logic, kept out of the command so the
/// command stays a thin front-end and this mapping is unit-testable on its own.
/// </summary>
public static class RelatedPageSpecBuilder {
	// Audience role names. The Interface Designer scopes the base (general) page set to "All employees" and
	// layers the portal (self-service) set on top as "All external users" (verified live against the designer's
	// own SaveSchema output). IsSspDefault stays false throughout.
	public const string EmployeesRoleName = "All employees";
	public const string PortalRoleName = "All external users";

	/// <summary>
	/// Returns <paramref name="explicitPages"/> verbatim when supplied (the MCP tool passes the full page
	/// set) — including an EMPTY list, which is a deliberate "reset to inline" that clears every binding.
	/// Otherwise builds the page set from the scalar CLI options. An add page given without its matching
	/// default page is rejected: that audience would get a page to ADD a record but none to OPEN one. The
	/// service's base-default check only guarantees ONE audience has an untyped default, so this per-audience
	/// guard is enforced here — otherwise <c>--add-page</c> (or <c>--portal-add-page</c>) alongside the OTHER
	/// audience's default would silently bind an add-only set. The scalar CLI path never yields an empty set
	/// (clearing is an explicit MCP-only operation), so a no-option CLI invocation is rejected rather than
	/// silently wiping the configuration.
	/// </summary>
	public static IReadOnlyList<RelatedPageSpec> Build(
		IReadOnlyList<RelatedPageSpec> explicitPages,
		string defaultPage,
		string addPage,
		string portalDefaultPage,
		string portalAddPage) {
		if (explicitPages is not null) {
			// The MCP tool passes the full set verbatim. An empty list is a deliberate reset-to-inline
			// (clear all bindings) — the effective delete, since the platform has no add-on delete.
			return explicitPages;
		}
		if (!string.IsNullOrWhiteSpace(addPage) && string.IsNullOrWhiteSpace(defaultPage)) {
			throw new ArgumentException(RelatedPageAddonMessages.AddPageRequiresDefaultPage);
		}
		if (!string.IsNullOrWhiteSpace(portalAddPage) && string.IsNullOrWhiteSpace(portalDefaultPage)) {
			throw new ArgumentException(RelatedPageAddonMessages.PortalAddPageRequiresPortalDefaultPage);
		}
		var built = new List<RelatedPageSpec>();
		// The base (general) set is scoped to "All employees", matching the designer; the portal set is layered on
		// top as an "All external users" override. Within each audience the default page also serves record
		// creation, so an add entry is written ONLY when a distinct add page is supplied (see AddAudiencePages).
		AddAudiencePages(built, defaultPage, addPage, EmployeesRoleName);
		AddAudiencePages(built, portalDefaultPage, portalAddPage, PortalRoleName);
		if (built.Count == 0) {
			throw new ArgumentException(RelatedPageAddonMessages.NoPageOptionsSpecified);
		}
		return built;
	}

	/// <summary>
	/// Adds the page entries for one audience: the default page (used for opening AND — implicitly — adding a
	/// record) and, ONLY when a separate add page is supplied, a distinct add entry. Mirrors the designer, which
	/// writes a lone default as a single entry (the platform uses it for adding too) and adds a second entry only
	/// when the add page is set explicitly. Empty pages are skipped.
	/// </summary>
	private static void AddAudiencePages(
		List<RelatedPageSpec> pages, string defaultPage, string addPage, string roleName) {
		if (!string.IsNullOrWhiteSpace(defaultPage)) {
			pages.Add(new RelatedPageSpec(defaultPage.Trim(), IsDefault: true, RoleName: roleName));
		}
		if (!string.IsNullOrWhiteSpace(addPage)) {
			pages.Add(new RelatedPageSpec(addPage.Trim(), IsAdd: true, RoleName: roleName));
		}
	}
}
