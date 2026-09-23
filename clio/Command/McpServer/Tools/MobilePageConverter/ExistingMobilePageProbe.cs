using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
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
	/// <paramref name="request"/>'s SysModule registration, plus — for a form source — a match on each of the
	/// page's bound entities' own default mobile edit page. Either match is excluded when it names
	/// <see cref="ExistingMobilePageProbeRequest.TargetName"/> (the schema this run is about to create/update):
	/// there is nothing to "reuse vs convert again" for the page the conversion is itself producing.
	/// Best-effort; never throws — a cancelled <paramref name="cancellationToken"/> degrades the same way
	/// every other failure here does: the affected match is simply skipped, never an exception.
	/// </summary>
	/// <param name="request">
	/// Guarded as a whole, not member-by-member — mirrors <see cref="MobileActionTargetProbe.Probe"/>: a
	/// null-conditional on the first access and a plain dereference on the next reads as safe and is not, and
	/// this method's own contract is that it never throws. A <see langword="null"/> request yields no matches,
	/// exactly like every other degradation this probe already fails open to.
	/// </param>
	public static List<ExistingMobilePageInfo> Probe(
		IToolCommandResolver commandResolver, string environment, string uri, string login, string password,
		ExistingMobilePageProbeRequest request, CancellationToken cancellationToken = default) {
		var matches = new List<ExistingMobilePageInfo>();
		if (request is null) {
			return matches;
		}
		// Resolved AT MOST ONCE per call and shared by both sub-probes below, rather than each one calling
		// MobileActionTargetProbe.ProbeContext.Create independently — a registered-section form page (the
		// common case: both checks run) used to pay the environment-client resolution cost twice for the
		// SAME environment/uri/login/password. Lazy (not built up front) so a page that needs neither check
		// (no registered section, not a form page) still resolves nothing.
		MobileActionTargetProbe.ProbeContext sharedContext = null;
		bool sharedContextAttempted = false;
		MobileActionTargetProbe.ProbeContext ResolveContext() {
			if (!sharedContextAttempted) {
				sharedContextAttempted = true;
				sharedContext = MobileActionTargetProbe.ProbeContext.Create(commandResolver, environment, uri, login, password);
			}
			return sharedContext;
		}

		if (request.SectionRegistration is
			{ MobileSectionRegistered: true, MobileSectionSchemaUId: { Length: > 0 } sectionSchemaUId }) {
			ExistingMobilePageInfo sectionMatch =
				ProbeSectionMobilePage(commandResolver, ResolveContext, sectionSchemaUId, cancellationToken);
			if (sectionMatch is not null
				&& !string.Equals(sectionMatch.SchemaName, request.TargetName, StringComparison.OrdinalIgnoreCase)) {
				matches.Add(sectionMatch);
			}
		}
		if (request.IsFormPage) {
			foreach (string entityName in MobileActionTargetProbe.CollectSourceEntityNames(request.ModelConfig)) {
				ExistingMobilePageInfo entityMatch = ProbeSourceEntityDefaultMobilePage(
					commandResolver, ResolveContext, entityName, request.PagePackageUId, cancellationToken);
				if (entityMatch is not null
					&& !string.Equals(entityMatch.SchemaName, request.TargetName, StringComparison.OrdinalIgnoreCase)) {
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
		IToolCommandResolver commandResolver, Func<MobileActionTargetProbe.ProbeContext> resolveContext,
		string entitySchemaName, string pagePackageUId, CancellationToken cancellationToken = default) {
		if (commandResolver is null || string.IsNullOrWhiteSpace(entitySchemaName)
			|| !Guid.TryParse(pagePackageUId, out Guid fallbackPackageUId)) {
			return null;
		}
		try {
			// Checked inside the try, not before it: a cancellation here must fail open to null exactly like
			// every other degradation this method already swallows, never escape as an exception.
			cancellationToken.ThrowIfCancellationRequested();
			MobileActionTargetProbe.ProbeContext context = resolveContext();

			var uIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var packageUIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			MobileActionTargetProbe.ReadEntitySchemaRows(
				context, [entitySchemaName], uIdByName, packageUIdByName, seenNames);
			if (!uIdByName.TryGetValue(entitySchemaName, out string entitySchemaUId)
				|| !Guid.TryParse(entitySchemaUId, out Guid entityUId)) {
				return null;
			}
			// The entity's OWN package — never the source page's — so an auto-provisioned add-on descriptor
			// lands where the entity itself lives (see DefaultPageAddonReader). Falls back only when the
			// row's package could not be resolved.
			Guid packageUId = packageUIdByName.TryGetValue(entitySchemaName, out string rawPackageUId)
				&& Guid.TryParse(rawPackageUId, out Guid parsedPackageUId)
					? parsedPackageUId
					: fallbackPackageUId;

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
		IToolCommandResolver commandResolver, Func<MobileActionTargetProbe.ProbeContext> resolveContext,
		string sectionSchemaUId, CancellationToken cancellationToken = default) {
		if (commandResolver is null || !Guid.TryParse(sectionSchemaUId, out Guid sectionUId)) {
			return null;
		}
		try {
			cancellationToken.ThrowIfCancellationRequested();
			MobileActionTargetProbe.ProbeContext context = resolveContext();
			SchemaNameResolver.Result result = SchemaNameResolver.ResolveName(context, sectionUId);
			return result.Status == SchemaNameResolver.Status.Resolved
				? new ExistingMobilePageInfo { SchemaName = result.Name, SchemaUId = sectionSchemaUId, Source = KindSection }
				: null;
		} catch (Exception) {
			return null;
		}
	}
}
