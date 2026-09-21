using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Common;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Read-only environment probe for the reuse-vs-convert check (playbook step 2a): does the entity/page
/// being converted already have an EXISTING mobile page? Sister to
/// <see cref="MobileSectionRegistrationProbe"/>. A section source is settled from the SysModule
/// registration that probe already resolved (<see cref="ProbeSectionMobilePage"/>); a form source is
/// settled from its bound entity's <c>MobileRelatedPage</c> add-on (<see cref="ProbeSourceEntityDefaultMobilePage"/>),
/// the same mechanism <see cref="MobileActionTargetProbe"/> uses to resolve that kind for OTHER objects. Both
/// checks are read-only, best-effort, and never throw — a degraded read simply yields fewer matches.
/// </summary>
public static class ExistingMobilePageProbe {

	/// <summary>
	/// <see cref="ExistingMobilePageInfo.Source"/> value for a match found via the source list/section
	/// page's own <c>SysModule.MobileSectionSchemaUId</c> registration (see
	/// <see cref="ProbeSectionMobilePage"/>) — distinct from
	/// <see cref="MobileActionTargetProbe.KindEntityDefaultMobilePage"/>, which names the same source used
	/// to resolve an ACTION target on a different object.
	/// </summary>
	internal const string KindSection = "section";

	/// <summary>
	/// Orchestrates the whole reuse-vs-convert check for one conversion guide call: a section match from
	/// <paramref name="sectionRegistration"/>'s SysModule registration, plus — for a form source — a match on
	/// each of the page's bound entities' own default mobile edit page. Either match is excluded when it
	/// names <paramref name="targetName"/> (the schema this run is about to create/update): there is nothing
	/// to "reuse vs convert again" for the page the conversion is itself producing. Best-effort; never throws.
	/// </summary>
	/// <param name="sectionRegistration">The source page's SysModule registration, already probed by <see cref="MobileSectionRegistrationProbe"/>.</param>
	/// <param name="isFormPage">Whether the source page is an edit/form page (vs a list/section page).</param>
	/// <param name="modelConfig">The source page's merged <c>modelConfig</c>, used to find its bound entities.</param>
	/// <param name="pagePackageUId">The source page's package UId, used to address the entity add-on read.</param>
	/// <param name="targetName">The schema name this conversion is about to create/update.</param>
	public static List<ExistingMobilePageInfo> Probe(
		IToolCommandResolver commandResolver, string environment, string uri, string login, string password,
		SectionRegistrationInfo sectionRegistration, bool isFormPage, JsonObject modelConfig,
		string pagePackageUId, string targetName) {
		var matches = new List<ExistingMobilePageInfo>();
		if (sectionRegistration is
			{ MobileSectionRegistered: true, MobileSectionSchemaUId: { Length: > 0 } sectionSchemaUId }) {
			ExistingMobilePageInfo sectionMatch = ProbeSectionMobilePage(
				commandResolver, environment, uri, login, password, sectionSchemaUId);
			if (sectionMatch is not null
				&& !string.Equals(sectionMatch.SchemaName, targetName, StringComparison.OrdinalIgnoreCase)) {
				matches.Add(sectionMatch);
			}
		}
		if (isFormPage) {
			foreach (string entityName in MobileActionTargetProbe.CollectSourceEntityNames(modelConfig)) {
				ExistingMobilePageInfo entityMatch = ProbeSourceEntityDefaultMobilePage(
					commandResolver, environment, uri, login, password, entityName, pagePackageUId);
				if (entityMatch is not null
					&& !string.Equals(entityMatch.SchemaName, targetName, StringComparison.OrdinalIgnoreCase)) {
					matches.Add(entityMatch);
				}
			}
		}
		return matches;
	}

	/// <summary>
	/// Resolves the SOURCE page's own bound entity's existing default MOBILE edit page, if any — the
	/// "does this object already have a mobile page" fact behind the reuse-vs-convert check
	/// (<see cref="ExistingMobilePageInfo"/> / playbook step 2a). Mirrors the missing-target candidate flow
	/// (<see cref="DefaultPageAddonReader.ReadWebDefaultPageUId"/> plus a name lookup) almost exactly, but reads
	/// the MOBILE add-on (<see cref="DefaultPageAddonReader.MobileRelatedPageAddonName"/>) instead of the web
	/// one, and is keyed by an entity NAME resolved via <see cref="MobileActionTargetProbe.ReadEntitySchemaRows"/>
	/// rather than an already-known UId (the missing-target tier already has one; this call-site starts from
	/// just a name — see <see cref="MobileActionTargetProbe.CollectSourceEntityNames"/>). Fails open to
	/// <see langword="null"/> on any degradation (unreachable environment, unresolvable entity name, no default
	/// page, an unparseable add-on body, or a resolved name that fails
	/// <see cref="SchemaNameResolver.Status.NameInvalid"/> validation) — never throws, and never guesses a page
	/// name.
	/// </summary>
	internal static ExistingMobilePageInfo ProbeSourceEntityDefaultMobilePage(
		IToolCommandResolver commandResolver, string environment, string uri, string login, string password,
		string entitySchemaName, string pagePackageUId) {
		if (commandResolver is null || string.IsNullOrWhiteSpace(entitySchemaName)
			|| !Guid.TryParse(pagePackageUId, out Guid packageUId)) {
			return null;
		}
		try {
			MobileActionTargetProbe.ProbeContext context =
				MobileActionTargetProbe.ProbeContext.Create(commandResolver, environment, uri, login, password);

			var uIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			MobileActionTargetProbe.ReadEntitySchemaRows(context, [entitySchemaName], uIdByName, seenNames);
			if (!uIdByName.TryGetValue(entitySchemaName, out string entitySchemaUId)
				|| !Guid.TryParse(entitySchemaUId, out Guid entityUId)) {
				return null;
			}

			AddonSchemaDto schema = context.AddonClient.GetSchema(new AddonGetRequestDto {
				AddonName = DefaultPageAddonReader.MobileRelatedPageAddonName,
				TargetSchemaUId = entityUId,
				TargetParentSchemaUId = Guid.Empty,
				TargetPackageUId = packageUId,
				TargetSchemaManagerName = EntitySchemaManagerName,
				UseFullHierarchy = true
			});
			string pageSchemaUId = MobileActionTargetProbe.ExtractDefaultPageSchemaUId(schema?.MetaData);
			if (string.IsNullOrWhiteSpace(pageSchemaUId) || !Guid.TryParse(pageSchemaUId, out Guid pageUId)) {
				return null;
			}
			SchemaNameResolver.Result result = SchemaNameResolver.ResolveName(context, pageUId);
			return result.Status == SchemaNameResolver.Status.Resolved
				? new ExistingMobilePageInfo {
					SchemaName = result.Name, SchemaUId = pageSchemaUId,
					Source = MobileActionTargetProbe.KindEntityDefaultMobilePage
				}
				: null;
		} catch (Exception) {
			return null;
		}
	}

	/// <summary>
	/// Resolves an already-known mobile page UId (e.g.
	/// <c>SectionRegistrationInfo.MobileSectionSchemaUId</c>) to its schema NAME — the section half of the
	/// reuse-vs-convert check (<see cref="ExistingMobilePageInfo"/> / playbook step 2a). The same
	/// UId→name reverse lookup the missing-target candidate flow batches in
	/// <see cref="SchemaNameResolver.ResolveNames"/>, single-UId here via
	/// <see cref="SchemaNameResolver.ResolveName"/> because this call-site has exactly one UId.
	/// Fails open to <see langword="null"/>, never throws.
	/// </summary>
	internal static ExistingMobilePageInfo ProbeSectionMobilePage(
		IToolCommandResolver commandResolver, string environment, string uri, string login, string password,
		string sectionSchemaUId) {
		if (commandResolver is null || !Guid.TryParse(sectionSchemaUId, out Guid sectionUId)) {
			return null;
		}
		try {
			MobileActionTargetProbe.ProbeContext context =
				MobileActionTargetProbe.ProbeContext.Create(commandResolver, environment, uri, login, password);
			SchemaNameResolver.Result result = SchemaNameResolver.ResolveName(context, sectionUId);
			return result.Status == SchemaNameResolver.Status.Resolved
				? new ExistingMobilePageInfo { SchemaName = result.Name, SchemaUId = sectionSchemaUId, Source = KindSection }
				: null;
		} catch (Exception) {
			return null;
		}
	}
}
