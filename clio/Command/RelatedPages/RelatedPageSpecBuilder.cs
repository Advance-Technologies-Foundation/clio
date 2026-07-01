using System;
using System.Collections.Generic;

namespace Clio.Command.RelatedPages;

/// <summary>
/// Builds the related-page spec list from the scalar CLI options of <c>create-related-page-addon</c>
/// (default/add page per audience). Pure, side-effect-free domain logic, kept out of the command so the
/// command stays a thin front-end and this mapping is unit-testable on its own.
/// </summary>
public static class RelatedPageSpecBuilder {
	// The portal (self-service) audience role name. A portal page set is layered on top of the role-less base
	// as an "All external users" override (verified against Cases; IsSspDefault stays false).
	public const string PortalRoleName = "All external users";

	/// <summary>
	/// Returns <paramref name="explicitPages"/> verbatim when supplied (the MCP tool passes the full page
	/// set); otherwise builds the page set from the scalar CLI options. An add page given without its matching
	/// default page is rejected: that audience would get a page to ADD a record but none to OPEN one. The
	/// service's base-default check only guarantees ONE audience has an untyped default, so this per-audience
	/// guard is enforced here — otherwise <c>--add-page</c> (or <c>--portal-add-page</c>) alongside the OTHER
	/// audience's default would silently bind an add-only set.
	/// </summary>
	public static IReadOnlyList<RelatedPageSpec> Build(
		IReadOnlyList<RelatedPageSpec> explicitPages,
		string defaultPage,
		string addPage,
		string portalDefaultPage,
		string portalAddPage) {
		if (explicitPages is { Count: > 0 }) {
			return explicitPages;
		}
		if (!string.IsNullOrWhiteSpace(addPage) && string.IsNullOrWhiteSpace(defaultPage)) {
			throw new ArgumentException(
				"--add-page requires --default-page: the internal audience needs a default page to open a record, "
				+ "not just an add page.");
		}
		if (!string.IsNullOrWhiteSpace(portalAddPage) && string.IsNullOrWhiteSpace(portalDefaultPage)) {
			throw new ArgumentException(
				"--portal-add-page requires --portal-default-page: the portal audience needs a default page to open a "
				+ "record, not just an add page.");
		}
		var built = new List<RelatedPageSpec>();
		// The base default is always role-less so it applies to EVERY user — a role-less base default is mandatory
		// (see RelatedPageAddonService.ValidateRequest). A portal page is layered on top as an "All external users"
		// override: portal users match that more specific set, everyone else falls back to the role-less base.
		AddAudiencePages(built, defaultPage, addPage, roleName: null);
		AddAudiencePages(built, portalDefaultPage, portalAddPage, PortalRoleName);
		return built;
	}

	/// <summary>
	/// Adds the default and add page entries for one audience: an empty default/add page is skipped and
	/// the add page falls back to the default page when omitted (so a single page can serve both).
	/// </summary>
	private static void AddAudiencePages(
		List<RelatedPageSpec> pages, string defaultPage, string addPage, string roleName) {
		if (!string.IsNullOrWhiteSpace(defaultPage)) {
			pages.Add(new RelatedPageSpec(defaultPage.Trim(), IsDefault: true, RoleName: roleName));
		}
		string effectiveAddPage = string.IsNullOrWhiteSpace(addPage) ? defaultPage : addPage;
		if (!string.IsNullOrWhiteSpace(effectiveAddPage)) {
			pages.Add(new RelatedPageSpec(effectiveAddPage.Trim(), IsAdd: true, RoleName: roleName));
		}
	}
}
