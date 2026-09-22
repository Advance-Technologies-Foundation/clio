using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Clio.Command.AddonSchemaDesigner;
using Clio.Common;
using Newtonsoft.Json.Linq;
// The schema-manager names are shared with the business-rule probe in this directory rather than
// re-declared: they name platform managers, and two copies can drift apart across a platform rename.
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Read-only environment probe that answers, for every action the source page fires, whether the thing it
/// NAVIGATES TO exists on mobile. Whether the request TYPE converts is a separate question,
/// already decided offline by <c>WebToMobileAnalysisService.IsRequestSupported</c>; this probe answers the
/// second one. A <c>crt.OpenPageRequest</c> may convert perfectly and still open a schema that has no
/// mobile page, and a <c>crt.CreateRecordRequest</c> may name an object with no default mobile edit page.
/// Either way the converted page would ship a control that always fails.
/// <para>
/// WHICH requests carry a target is DATA, not code: the versioned rules file's <c>targetParam</c> /
/// <c>targetKind</c> (see <see cref="RequestMappingRule.TargetParam"/>). A request the rules say nothing
/// about is never checked, and an unrecognized <c>targetKind</c> is never checked either — so a newer rules
/// file cannot make an older clio report a target it does not know how to verify.
/// </para>
/// <para>
/// It performs DataService <c>SelectQuery</c> reads plus up to two designer reads per object — the mobile
/// classification, and for a verified-missing object the web candidate — capped per call by
/// <see cref="MaxEntityAddonProbes"/>, and issues no write
/// call. (The add-on <c>GetSchema</c> is a read that the SERVER answers by auto-provisioning an empty
/// descriptor when none exists; that side effect is the platform's, and it is idempotent.) It NEVER throws: any failure degrades to <see cref="MobileActionTargetProbeResult.ProbeOk"/> = false and
/// leaves the affected targets ABSENT from the resolution map, which every consumer reads as
/// <see cref="ActionTargetState.Unknown"/> — the fail-open value. Nothing on the page is ever reported
/// broken on missing information.
/// </para>
/// <para>
/// The two kinds are resolved in SEPARATE tiers, and degrade separately.
/// <see cref="KindWebPage"/> is settled offline — a web page cannot open on mobile by construction — so it
/// survives an unreachable environment; only <see cref="KindEntityDefaultMobilePage"/> needs reads, and it
/// is what <see cref="MobileActionTargetProbeResult.ProbeOk"/> reports on. Collapsing the two into one
/// boolean is how a page carrying BOTH kinds used to lose its web-page verdict whenever the object reads
/// failed, while the same verdict on a page carrying only web-page targets reported fine.
/// </para>
/// </summary>
public static class MobileActionTargetProbe {

	/// <summary>
	/// Target kind: the value names a WEB page schema. Always reported, never looked up — a web page's
	/// <c>schemaName</c> names a web page BY CONSTRUCTION (the converter only accepts a web source, and it
	/// carries the value verbatim: <c>crt.OpenPageRequest</c> declares no <c>paramMap</c> and nothing rewrites
	/// the parameter). The Creatio Mobile app cannot open a web page, so the action is dead on mobile no matter
	/// what the environment holds. Asking it a question whose answer is fixed would only add round trips and a
	/// way to be wrong.
	/// </summary>
	internal const string KindWebPage = "web-page";

	/// <summary>Target kind: the value names an object that must have a default mobile edit page.</summary>
	internal const string KindEntityDefaultMobilePage = "entity-default-mobile-page";

	private const string SysSchemaName = "SysSchema";
	private const string RequestProperty = "request";
	private const string ParamsProperty = "params";
	private const string NameProperty = "name";

	/// <summary>
	/// Depth cap for the page-body walk. The body is authored data, and a <see cref="StackOverflowException"/>
	/// cannot be caught — so the never-throws contract can only be upheld structurally. Set above
	/// <c>JsonDocumentOptions.MaxDepth</c> (64), which the page read already enforces, so this never truncates
	/// a body that got this far; it exists for a tree built by some other route.
	/// </summary>
	private const int MaxWalkDepth = 128;

	/// <summary>
	/// Text <c>dataValueType</c> for an ESQ filter value, matching every by-name lookup in
	/// <see cref="ClassicEntitySchemaQuery"/>.
	/// </summary>
	private const int TextDataValueType = 1;

	/// <summary>
	/// Row headroom per requested name: a schema appears as a base row plus one row per replacing layer, and
	/// there is no bound on how many layers an object carries. The read ORDERS base rows first
	/// (<see cref="ClassicEntitySchemaQuery.ColumnOrderedAsc"/>), so this cap can no longer cost the base row
	/// — it only decides how many extra layers are seen, which is what separates "this object has no rows at
	/// all" (absent) from "rows but no base row" (unknown).
	/// </summary>
	private const int RowsPerNameHeadroom = 4;

	/// <summary>
	/// Per-call ceiling on <see cref="DefaultPageAddonReader.ReadMobileState"/> probes: each one is a sequential
	/// round trip, and read-only MCP tools answer under a wall-clock deadline (<c>McpReadResponseDeadline</c>),
	/// so an unbounded per-object fan-out on a page with many distinct targets can time out the whole call —
	/// and a retry after a timeout repeats the identical unbounded work rather than resuming it. Capping
	/// keeps one guide call's cost predictable regardless of how many targets a page names, at the cost of
	/// reporting the excess as <see cref="ActionTargetState.Unknown"/> (fail open) rather than verifying them.
	/// </summary>
	private const int MaxEntityAddonProbes = 32;

	/// <summary>
	/// Upper bound on how many of the per-object reads inside the <see cref="MaxEntityAddonProbes"/> budget
	/// run AT THE SAME TIME, rather than one after another. The only existing precedent for concurrent reads
	/// on the shared <see cref="IApplicationClient"/> (<c>EntitySchemaDependencyResolver.Resolve</c>) runs two
	/// requests at once and documents why that is safe (<c>CreatioClientAdapter</c>'s
	/// <c>Lazy&lt;CreatioClient&gt;</c> guarded by <c>ExecutionAndPublication</c>, <c>ReauthExecutor</c>
	/// collapsing a parallel failure burst into one <c>Login</c>, <c>LoginDiagnostics</c> counting
	/// <c>RequestsInFlight</c> with <c>Interlocked</c>) — proven at concurrency 2, not yet proven at the width
	/// this constant introduces. Kept well under <see cref="MaxEntityAddonProbes"/> as a deliberate caution
	/// pending a live-stand measurement, rather than running the whole budget wide open.
	/// </summary>
	private const int MaxEntityProbeParallelism = 8;

	/// <summary>
	/// The key one distinct action target is resolved under. Single-sourced so the probe that WRITES a
	/// resolution and the report that READS it can never disagree on casing or trimming — the discipline
	/// <c>WebToMobileAnalysisService.IsRequestSupported</c> applies to request support, applied to targets.
	/// </summary>
	internal static string TargetKey(string kind, string target) =>
		$"{kind?.Trim().ToLowerInvariant()}:{target?.Trim()}";

	/// <summary>
	/// Probes the environment for the existence of every navigation target the page's action bindings name.
	/// </summary>
	/// <param name="commandResolver">Per-call environment resolver; <see langword="null"/> means "not probed".</param>
	/// <param name="environment">Registered environment name, or null when uri/login/password are supplied.</param>
	/// <param name="uri">Explicit environment URI.</param>
	/// <param name="login">Explicit login.</param>
	/// <param name="password">Explicit password.</param>
	/// <param name="request">The page-side inputs: body, resolved rules and the package identity to read with.</param>
	/// <returns>The occurrences found on the page and, when the reads succeeded, one resolution per distinct target.</returns>
	public static MobileActionTargetProbeResult Probe(
		IToolCommandResolver commandResolver,
		string environment, string uri, string login, string password,
		MobileActionTargetProbeRequest request) {
		// Guard the REQUEST, not each member: a null-conditional on the first access and a plain dereference on
		// the next reads as safe and is not, and this method's contract is that it never throws.
		if (request is null) {
			return new MobileActionTargetProbeResult {
				ProbeOk = false, Note = "Action targets were not verified (no page inputs were supplied)."
			};
		}
		IReadOnlyDictionary<string, RequestMappingRule> targeted = BuildTargetedRequestMap(request.Rules);
		if (targeted.Count == 0) {
			// Not an environment failure: the rules simply declare no navigation targets (an older published
			// rules file). Report "not probed" so an empty finding list is never read as "all clear".
			return new MobileActionTargetProbeResult {
				ProbeOk = false,
				Note = "Action targets were not verified (the conversion rules declare no request navigation targets)."
			};
		}

		IReadOnlyList<ActionTargetOccurrence> occurrences;
		IReadOnlySet<string> sourceEntities;
		try {
			// Inside the try on purpose: the walk reads an authored page body, and the never-throws contract
			// must be structural rather than a property of today's parser.
			sourceEntities = CollectSourceEntityNames(request.ModelConfig);
			occurrences = ExceptSourceEntityTargets(CollectActionTargets(request.ViewConfig, targeted), sourceEntities);
		} catch (Exception ex) {
			return NotProbed([], Describe("Could not read the page's action bindings", ex));
		}
		if (occurrences.Count == 0) {
			// Nothing on the page navigates anywhere: the check ran and found nothing to verify.
			return new MobileActionTargetProbeResult { ProbeOk = true };
		}

		// Settled without asking anyone: a web-page reference is dead on mobile by construction. Doing this
		// BEFORE the environment is reached is what lets an offline run still report it.
		var resolutions = new Dictionary<string, ActionTargetResolution>(StringComparer.OrdinalIgnoreCase);
		foreach (string webPage in DistinctTargetsOfKind(occurrences, KindWebPage)) {
			Record(resolutions, KindWebPage, webPage, ActionTargetState.Missing);
		}

		IReadOnlyList<string> entityTargets = DistinctTargetsOfKind(occurrences, KindEntityDefaultMobilePage);
		if (entityTargets.Count == 0) {
			return new MobileActionTargetProbeResult {
				ProbeOk = true, Occurrences = occurrences, TargetsByKey = resolutions
			};
		}
		if (commandResolver is null) {
			return NotProbed(occurrences, "Object action targets were not verified (missing environment client).",
				resolutions);
		}

		try {
			// Resolved ONCE rather than per object: the add-on read runs once per distinct object, and
			// re-resolving inside that loop buys nothing but container work.
			ProbeContext context = ProbeContext.Create(commandResolver, environment, uri, login, password);

			EntityTierOutcome outcome =
				ResolveEntityTargets(context, entityTargets, request.PagePackageUId, resolutions);

			return new MobileActionTargetProbeResult {
				ProbeOk = outcome.Answered, Occurrences = occurrences, TargetsByKey = resolutions,
				Note = outcome.Note
			};
		} catch (Exception ex) {
			// Covers the DataService failure envelope and a non-JSON body alike: IApplicationClient returns a
			// proxy/auth error PAGE as an ordinary string rather than throwing, so it is the SelectQuery helper
			// this file calls that raises it. Reading such a body as "no rows" would report every target as
			// absent and send the user off to fix controls that already work.
			return NotProbed(occurrences,
				Describe("Could not verify object action targets", ex)
				+ " Check each action's target object manually.",
				resolutions);
		}
	}

	/// <summary>
	/// The requests whose target this build knows how to verify: a rule declaring BOTH a <c>targetParam</c>
	/// and a RECOGNIZED <c>targetKind</c>. Everything else is deliberately absent, so it is never checked.
	/// </summary>
	private static IReadOnlyDictionary<string, RequestMappingRule> BuildTargetedRequestMap(
		WebToMobilePageConversionRules rules) {
		var map = new Dictionary<string, RequestMappingRule>(StringComparer.OrdinalIgnoreCase);
		IEnumerable<RequestMappingRule> declared = (rules?.Requests ?? []).Where(rule =>
			!string.IsNullOrWhiteSpace(rule?.Web)
			&& !string.IsNullOrWhiteSpace(rule.TargetParam)
			&& IsRecognizedKind(rule.TargetKind));
		foreach (RequestMappingRule rule in declared) {
			map[rule.Web] = rule;
		}
		return map;
	}

	/// <summary>
	/// The object the source page is FOR: the entity of its PRIMARY data source, read from
	/// <c>modelConfig.primaryDataSourceName</c> -&gt; <c>dataSources[name].config.entitySchemaName</c>. Empty when
	/// the page declares no primary (a list page, or a body that predates the marker).
	/// <para>
	/// Targets naming this object are DROPPED FROM THE REPORT, and that exemption is load-bearing. A record
	/// page for <c>Lead</c> routinely carries a "create Lead" action; before the conversion runs there is no
	/// <c>MobileRelatedPage</c> add-on for <c>Lead</c> — creating it IS the conversion's own closing step, the
	/// one <see cref="MobileSectionRegistrationProbe"/> spells out as "register it as the object's default
	/// MOBILE edit page". Reporting it would have one response flag an action as broken and, in its
	/// <c>sectionRegistration</c> section, instruct the caller to create the very page it is missing.
	/// </para>
	/// <para>
	/// ONLY the primary. Every OTHER data source on the page — a details list, a timeline tile, an attachment
	/// list — names an object this conversion does nothing about, so its default mobile page is a fair
	/// question. The real <c>Leads_FormPage</c> declares twelve data sources across nine objects
	/// (<c>LeadProduct</c>, <c>Opportunity</c>, <c>Activity</c>, …); exempting all of them silenced the
	/// feature on the very page it was built for.
	/// </para>
	/// </summary>
	internal static IReadOnlySet<string> CollectSourceEntityNames(JsonObject modelConfig) {
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (modelConfig?["dataSources"] is not JsonObject dataSources) {
			return names;
		}
		// The marker is authoritative; the conventional name is the fallback for a body that omits it. When
		// neither resolves, nothing is exempt — an unnecessary question costs the user a glance, while a wrong
		// exemption costs them the finding entirely.
		string primaryName = RelatedPageAddonMetadata.Str(modelConfig, "primaryDataSourceName");
		JsonObject primary =
			(!string.IsNullOrWhiteSpace(primaryName) ? dataSources[primaryName] : null) as JsonObject
			?? dataSources["PDS"] as JsonObject;
		if (primary?["config"] is JsonObject config
			&& RelatedPageAddonMetadata.Str(config, "entitySchemaName") is { } entity && !string.IsNullOrWhiteSpace(entity)) {
			names.Add(entity.Trim());
		}
		return names;
	}

	/// <summary>
	/// Drops the occurrences whose object is one the source page is itself bound to. See
	/// <see cref="CollectSourceEntityNames"/> for why those are not reportable: the conversion creates their
	/// mobile page, so neither "missing" nor "please verify" is a question worth putting to the user.
	/// </summary>
	private static IReadOnlyList<ActionTargetOccurrence> ExceptSourceEntityTargets(
		IReadOnlyList<ActionTargetOccurrence> occurrences, IReadOnlySet<string> sourceEntities) =>
		sourceEntities.Count == 0
			? occurrences
			: [.. occurrences.Where(o =>
				!string.Equals(o.Kind, KindEntityDefaultMobilePage, StringComparison.OrdinalIgnoreCase)
				|| !sourceEntities.Contains(o.Target))];

	/// <summary>Whether this build knows how to verify a target of <paramref name="kind"/>.</summary>
	private static bool IsRecognizedKind(string kind) =>
		string.Equals(kind, KindWebPage, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(kind, KindEntityDefaultMobilePage, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether a <see cref="ActionTargetState.Missing"/> verdict on this kind is strong enough to BLANK the
	/// action's target param, as opposed to only reporting it. The single seam that decides it, so the rule
	/// lives in one place instead of being re-derived at each consumer.
	/// <para>
	/// True only for <see cref="KindWebPage"/> — a DEFINITIONAL absence that needs no environment read and
	/// cannot be wrong for a reason outside this process: a web page cannot open on the Creatio Mobile app,
	/// full stop. The request still converts and the binding stays on the element (mobile request name,
	/// <c>paramMap</c> applied, every other param intact) — only the target param (<c>schemaName</c> on
	/// <c>crt.OpenPageRequest</c>) is blanked to <c>""</c>, because leaving it pointed at a non-existent page
	/// shows the user a settings-error dialog on every tap. Blanking it instead leaves the control inert (no
	/// dialog, no error) while keeping the action visible and reconfigurable in Mobile Designer, rather than
	/// losing it silently. Because the binding is never removed, there is nothing to reconstruct later: once
	/// the missing target page converts (or an existing mobile equivalent is found under a different name), a
	/// caller patches the already-present <c>values[binding].params[targetParam]</c> in place.
	/// </para>
	/// <para>
	/// <see cref="KindEntityDefaultMobilePage"/> stays exempt for a SEPARATE reason: the <c>MobileRelatedPage</c>
	/// add-on declaring no default page is a fact about the add-on, not proof the action is dead — a legacy
	/// default page can exist without ever being registered there (see
	/// <see cref="DefaultPageAddonReader.ReadMobileState"/>). Blanking on that uncertain a signal risks disabling a
	/// working action, which is a trade this tool does not make.
	/// </para>
	/// </summary>
	/// <param name="kind">A rules-declared <c>targetKind</c>.</param>
	/// <returns>Whether a verified absence of this kind blanks the target param.</returns>
	internal static bool StripsBindingOnMissing(string kind) =>
		// Trimmed to match TargetKey, which trims the kind when it builds the key a resolution is stored
		// under: an untrimmed Kind would otherwise be FOUND by the lookup and then silently not stripped.
		string.Equals(kind?.Trim(), KindWebPage, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Every place a target-carrying request appears in the page body. PURE — no environment, so the whole
	/// collection rule is unit-testable offline. A binding is recognized STRUCTURALLY (an object property
	/// whose value is an object carrying a non-empty string <c>request</c>), the same test
	/// <c>WebToMobileAnalysisService.IsEventBinding</c> applies, so it covers <c>clicked</c>,
	/// <c>valueChange</c>, <c>updated</c> and any future event property without a registry of outputs.
	/// </summary>
	/// <param name="viewConfig">The merged page view configuration.</param>
	/// <param name="targeted">Web request name to the rule declaring its target, from <c>BuildTargetedRequestMap</c>.</param>
	/// <returns>One entry per binding that names a literal target, in document order.</returns>
	internal static IReadOnlyList<ActionTargetOccurrence> CollectActionTargets(
		JsonArray viewConfig, IReadOnlyDictionary<string, RequestMappingRule> targeted) {
		var found = new List<ActionTargetOccurrence>();
		if (viewConfig is null || targeted is null || targeted.Count == 0) {
			return found;
		}
		foreach (JsonNode node in viewConfig) {
			WalkForTargets(node, null, targeted, found, depth: 0);
		}
		return found;
	}

	/// <summary>
	/// Recursive half of <see cref="CollectActionTargets"/>. <paramref name="enclosingName"/> carries the
	/// nearest named ancestor so a binding on an unnamed wrapper is still attributed to a component the caller
	/// can find on the page.
	/// </summary>
	private static void WalkForTargets(
		JsonNode node, string enclosingName,
		IReadOnlyDictionary<string, RequestMappingRule> targeted, List<ActionTargetOccurrence> found, int depth) {
		if (depth > MaxWalkDepth) {
			// Under-report rather than risk an uncatchable stack overflow: a target we never saw is simply
			// never warned about, which is the safe direction.
			return;
		}
		if (node is JsonArray array) {
			foreach (JsonNode item in array) {
				WalkForTargets(item, enclosingName, targeted, found, depth + 1);
			}
			return;
		}
		if (node is not JsonObject obj) {
			return;
		}
		string elementName = RelatedPageAddonMetadata.Str(obj, NameProperty) ?? enclosingName;
		foreach (KeyValuePair<string, JsonNode> property in obj) {
			if (property.Value is JsonObject candidate && RelatedPageAddonMetadata.Str(candidate, RequestProperty) is { } request
				&& !string.IsNullOrWhiteSpace(request)) {
				// An event binding. Record it when the rules declare a target for it, and never descend into
				// it: its params are data, not components.
				RecordOccurrence(elementName, property.Key, request, candidate, targeted, found);
				continue;
			}
			WalkForTargets(property.Value, elementName, targeted, found, depth + 1);
		}
	}

	/// <summary>
	/// Records one occurrence when the rules declare a target for <paramref name="request"/> AND the binding
	/// carries a LITERAL target value. A value bound to a page attribute (for example <c>$SomeId</c>) is
	/// deliberately skipped: its target is only known at runtime, so it can never be verified here and must
	/// never be reported as absent.
	/// </summary>
	private static void RecordOccurrence(
		string elementName, string binding, string request, JsonObject bindingObject,
		IReadOnlyDictionary<string, RequestMappingRule> targeted, List<ActionTargetOccurrence> found) {
		if (!targeted.TryGetValue(request, out RequestMappingRule rule)) {
			return;
		}
		string target = bindingObject[ParamsProperty] is JsonObject parameters
			? RelatedPageAddonMetadata.Str(parameters, rule.TargetParam)?.Trim()
			: null;
		// Trim BEFORE the attribute-binding test: " $PageToOpen" is just as runtime-resolved as "$PageToOpen",
		// and letting it through would look up a schema named "$PageToOpen", find none, and report the control
		// as navigating nowhere.
		if (string.IsNullOrEmpty(target) || target.StartsWith('$')) {
			return;
		}
		found.Add(new ActionTargetOccurrence {
			ElementName = elementName, Binding = binding, WebRequest = request,
			Kind = rule.TargetKind, Target = target
		});
	}

	/// <summary>The distinct target values of one kind, in first-seen order (case-insensitive dedupe).</summary>
	private static IReadOnlyList<string> DistinctTargetsOfKind(
		IReadOnlyList<ActionTargetOccurrence> occurrences, string kind) {
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		return [.. occurrences
			.Where(o => string.Equals(o.Kind, kind, StringComparison.OrdinalIgnoreCase) && seen.Add(o.Target))
			.Select(o => o.Target)];
	}

	/// <summary>
	/// What the object tier did. <c>Answered</c> is what <see cref="MobileActionTargetProbeResult.ProbeOk"/>
	/// reports, so a tier that performed NO read must say false however cleanly it returned — otherwise the
	/// caller is told the object targets were verified when nothing was asked. <c>Note</c> is null only when
	/// the tier ran in full; it is set both when the tier did not run and when it ran incompletely — including
	/// when every <c>Missing</c> verdict was itself settled but a candidate lookup
	/// (<see cref="DefaultPageAddonReader.ReadWebDefaultPageUId"/> / <see cref="ResolveCandidateNames"/>) failed for one or more of
	/// them, which still leaves <c>Answered</c> <see langword="true"/>: the
	/// verdicts stand, only the bonus candidate name is what a caller cannot trust as "confirmed absent".
	/// </summary>
	private sealed record EntityTierOutcome(bool Answered, string Note);

	/// <summary>
	/// The per-call environment seam, threaded through the resolution passes as one value. Carrying the
	/// resolver alongside the two clients it produced keeps every read on the SAME per-call container: a pass
	/// that re-resolved from somewhere else could silently answer for a different tenant. Internal, not
	/// private: <see cref="DefaultPageAddonReader"/> reads the same per-call container.
	/// </summary>
	internal sealed record ProbeContext(
		IToolCommandResolver Resolver, EnvironmentOptions Options,
		IApplicationClient Client, IServiceUrlBuilder UrlBuilder,
		IAddonSchemaDesignerClient AddonClient) {

		/// <summary>
		/// Builds a <see cref="ProbeContext"/> for one call, resolving the environment client trio from
		/// <paramref name="resolver"/> exactly once. The single seam every probe entry point in this file —
		/// and <see cref="ExistingMobilePageProbe"/>, which reads the same per-call container — constructs its
		/// context through, so a pass threading the result never re-resolves from somewhere else.
		/// </summary>
		internal static ProbeContext Create(
			IToolCommandResolver resolver, string environment, string uri, string login, string password) {
			var options = new EnvironmentOptions {
				Environment = environment, Uri = uri, Login = login, Password = password
			};
			return new ProbeContext(
				resolver, options,
				resolver.Resolve<IApplicationClient>(options),
				resolver.Resolve<IServiceUrlBuilder>(options),
				resolver.Resolve<IAddonSchemaDesignerClient>(options));
		}
	}

	/// <summary>
	/// The per-object outcome of the CONCURRENT phase of <see cref="ResolveEntityTargets"/>: the mobile
	/// classification plus, for a verified-missing object, the candidate web page UId (or the read failure that
	/// stopped one being found). Written once per array slot from inside the parallel body and read back
	/// sequentially afterwards — nothing else touches it, so no further synchronization is needed.
	/// </summary>
	private sealed record EntityProbeOutcome(
		string Name, ActionTargetState State, Guid? CandidatePageUId, Exception CandidateFailure);

	/// <summary>
	/// Resolves every <see cref="KindEntityDefaultMobilePage"/> target in three phases —
	/// <see cref="PartitionByEntityRow"/> (the one batched <c>SysSchema</c> read plus the budget clip),
	/// <see cref="ProbeBudgeted"/> (the concurrent per-object add-on reads), and
	/// <see cref="CollectOutcomes"/> (recording verdicts and batching the candidate name lookup). That add-on
	/// is what the Creatio Mobile app resolves for a create/update-record action, and it is the same add-on
	/// <c>create-related-page-addon --schema-type mobile</c> writes at the end of a conversion.
	/// </summary>
	/// <returns>Whether the tier answered, and what limited it — see <see cref="EntityTierOutcome"/>.</returns>
	private static EntityTierOutcome ResolveEntityTargets(
		ProbeContext context, IReadOnlyList<string> names, string pagePackageUId,
		IDictionary<string, ActionTargetResolution> into) {
		if (names.Count == 0) {
			return new EntityTierOutcome(true, null);
		}
		if (!Guid.TryParse(pagePackageUId, out Guid packageUId)) {
			// The add-on read is addressed by package; without one nothing can be verified. Unknown, not
			// Missing — the absence is in clio's inputs, not in the environment. And NOT "answered": no read
			// happened, so reporting targetsProbed:true here would claim a verification that never ran, the
			// same degradation PageBusinessRuleProbe reports for the identical input.
			foreach (string name in names) {
				Record(into, KindEntityDefaultMobilePage, name, ActionTargetState.Unknown);
			}
			return new EntityTierOutcome(false,
				"Object action targets were not verified (the source page package could not be resolved).");
		}

		(List<(string Name, string EntityUId)> budgeted, bool budgetExhausted) =
			PartitionByEntityRow(context, names, into);
		EntityProbeOutcome[] outcomes = ProbeBudgeted(context, budgeted, packageUId);
		return CollectOutcomes(context, outcomes, budgetExhausted, into);
	}

	/// <summary>
	/// Phase 1 of <see cref="ResolveEntityTargets"/> — the only phase that reads anything: one batched
	/// <c>SysSchema</c> read for <paramref name="names"/>' base-row UIds, then a purely sequential split by
	/// whether the object even HAS a row to probe. A name with no row is recorded directly (see the inline
	/// reasoning below) and never competes for the budget; what remains is clipped to
	/// <see cref="MaxEntityAddonProbes"/>, in the same first-seen order <paramref name="names"/> walks in, so
	/// the budget consumes candidates in a deterministic, input-order sequence before any concurrency enters
	/// the picture in <see cref="ProbeBudgeted"/>.
	/// </summary>
	/// <returns>The budgeted (name, entityUId) pairs to probe, and whether the budget clipped anything.</returns>
	private static (List<(string Name, string EntityUId)> Budgeted, bool BudgetExhausted) PartitionByEntityRow(
		ProbeContext context, IReadOnlyList<string> names, IDictionary<string, ActionTargetResolution> into) {
		var uIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool truncated = ReadEntitySchemaRows(context, names, uIdByName, seenNames);

		var toProbe = new List<(string Name, string EntityUId)>();
		foreach (string name in names) {
			if (!uIdByName.TryGetValue(name, out string entityUId)) {
				// No rows at all: the object does not exist, so the action is dead — unless the read may have
				// been truncated, when "no row" cannot be told apart from "the row was cut off the result".
				// Rows but no base row: the object cannot be addressed reliably, so refuse to guess (the
				// ResolveEntityUId rule). Resolving the name cost no probe, so this branch never touches budget.
				Record(into, KindEntityDefaultMobilePage, name,
					seenNames.Contains(name) || truncated ? ActionTargetState.Unknown : ActionTargetState.Missing);
				continue;
			}
			toProbe.Add((name, entityUId));
		}

		bool budgetExhausted = toProbe.Count > MaxEntityAddonProbes;
		List<(string Name, string EntityUId)> budgeted =
			budgetExhausted ? toProbe.Take(MaxEntityAddonProbes).ToList() : toProbe;
		if (budgetExhausted) {
			// Fail open AND say so: an unasked target must not look like one the environment answered "no" to.
			// Never entering the probe phase for these also skips their candidate read — a target never probed
			// is never Missing, so it never enters candidate resolution.
			foreach ((string name, _) in toProbe.Skip(MaxEntityAddonProbes)) {
				Record(into, KindEntityDefaultMobilePage, name, ActionTargetState.Unknown);
			}
		}
		return (budgeted, budgetExhausted);
	}

	/// <summary>
	/// Phase 2 of <see cref="ResolveEntityTargets"/>: each budgeted entry's classify-then-candidate sequence is
	/// independent of every other entry's, so it runs on its own task, bounded by
	/// <see cref="MaxEntityProbeParallelism"/>. Both <see cref="DefaultPageAddonReader.ReadMobileState"/> and
	/// <see cref="DefaultPageAddonReader.ReadWebDefaultPageUId"/> already fail open inside their own
	/// try/catch; the outer try/catch here is a second, structural guarantee that no single object's failure
	/// can escape the parallel body and take any other object's result down with it. Writing into a
	/// PRE-SIZED, per-index array — never a shared mutable collection — keeps the result order deterministic
	/// (byte-for-byte identical to a sequential version) no matter which read finishes first or slowest.
	/// </summary>
	private static EntityProbeOutcome[] ProbeBudgeted(
		ProbeContext context, IReadOnlyList<(string Name, string EntityUId)> budgeted, Guid packageUId) {
		var outcomes = new EntityProbeOutcome[budgeted.Count];
		Parallel.For(0, budgeted.Count,
			new ParallelOptions { MaxDegreeOfParallelism = MaxEntityProbeParallelism },
			i => {
				(string name, string entityUId) = budgeted[i];
				try {
					ActionTargetState state = DefaultPageAddonReader.ReadMobileState(context, entityUId, packageUId);
					Guid? candidatePageUId = null;
					Exception candidateFailure = null;
					// Candidate resolution runs ONLY for a verified-missing verdict: Unknown/Resolved need no
					// candidate.
					if (state == ActionTargetState.Missing) {
						candidatePageUId = DefaultPageAddonReader.ReadWebDefaultPageUId(
							context, entityUId, packageUId, out candidateFailure);
					}
					outcomes[i] = new EntityProbeOutcome(name, state, candidatePageUId, candidateFailure);
				} catch (Exception) {
					outcomes[i] = new EntityProbeOutcome(name, ActionTargetState.Unknown, null, null);
				}
			});
		return outcomes;
	}

	/// <summary>
	/// Phase 3 of <see cref="ResolveEntityTargets"/>: records every outcome's verdict, batches the
	/// verified-missing objects' candidate UId→NAME resolution in one call
	/// (<see cref="ResolveCandidateNames"/>) instead of one by-UId round trip per object, and composes the
	/// tier's final note from <paramref name="budgetExhausted"/> plus whatever candidate failures were
	/// accumulated along the way.
	/// </summary>
	private static EntityTierOutcome CollectOutcomes(
		ProbeContext context, IReadOnlyList<EntityProbeOutcome> outcomes, bool budgetExhausted,
		IDictionary<string, ActionTargetResolution> into) {
		var candidateFailures = new CandidateFailureAccumulator();
		var pendingCandidates = new List<(string Name, Guid PageUId)>();
		foreach (EntityProbeOutcome outcome in outcomes) {
			if (outcome.CandidateFailure is not null) {
				candidateFailures.RecordFailure(outcome.CandidateFailure);
			}
			if (outcome.CandidatePageUId is not null) {
				pendingCandidates.Add((outcome.Name, outcome.CandidatePageUId.Value));
			}
			// Candidate stays null here; ResolveCandidateNames re-records the entries that resolve a name.
			RecordEntityResolution(into, outcome.Name, outcome.State, null);
		}
		ResolveCandidateNames(context, pendingCandidates, into, candidateFailures);
		// Answered either way: the reads that ran did succeed. The note is what says some were never asked
		// and/or that a candidate lookup failed.
		return new EntityTierOutcome(true, candidateFailures.ComposeNote(budgetExhausted));
	}

	/// <summary>
	/// Accumulates candidate-web-page-lookup failures across <see cref="ProbeBudgeted"/>'s per-object reads
	/// and <see cref="ResolveCandidateNames"/>'s batched name lookup, replacing the <c>ref int, ref
	/// Exception</c> pair the two used to thread through by hand. Only the COUNT and the FIRST failure are
	/// kept — the composed note names how many objects failed and quotes one representative reason, never
	/// all of them. A candidate-resolution failure never demotes a STATE already settled in
	/// <see cref="CollectOutcomes"/>, and never aborts the batch — the other objects' candidates stand; it
	/// only costs the note, so a caller can tell "the read failed" apart from "the object genuinely has no
	/// default web page" instead of both silently reaching the wire as the same null.
	/// </summary>
	private sealed class CandidateFailureAccumulator {
		private int _count;
		private Exception _first;

		/// <summary>Records one candidate-lookup failure.</summary>
		internal void RecordFailure(Exception failure) {
			_count++;
			_first ??= failure;
		}

		/// <summary>
		/// Records <paramref name="count"/> failures that all share one <paramref name="failure"/> — the
		/// batched UId→name resolver throwing and failing every candidate pending in that call at once.
		/// </summary>
		internal void RecordBatch(int count, Exception failure) {
			_count += count;
			_first ??= failure;
		}

		/// <summary>
		/// Composes the tier's degradation note from <paramref name="budgetExhausted"/> plus whatever
		/// candidate failures were accumulated. Both can fire in the same call (some objects never probed,
		/// others probed but failed to resolve a candidate) — neither may silently replace the other.
		/// </summary>
		internal string ComposeNote(bool budgetExhausted) {
			string candidateNote = _count > 0
				? Describe($"Could not resolve a candidate web page for {_count} object(s)", _first)
				: null;
			string budgetNote = budgetExhausted
				? $"Only the first {MaxEntityAddonProbes} object targets were checked; the rest are reported as "
					+ "unverified. Check them manually."
				: null;
			if (budgetNote is null) {
				return candidateNote;
			}
			string candidateSuffix = candidateNote is null ? "" : " " + candidateNote;
			return budgetNote + candidateSuffix;
		}
	}

	/// <summary>
	/// Reads the objects' <c>SysSchema</c> rows and keeps the BASE row UId per name — the stable unit, exactly
	/// as <c>ClassicEntitySchemaQuery.ResolveEntityUId</c> picks it; a replacing layer is not a
	/// different object and must not be addressed instead. <paramref name="seenNames"/> separates "no rows at
	/// all" from "rows but no base row", which resolve differently. Internal, not private:
	/// <see cref="ExistingMobilePageProbe"/> resolves an entity NAME to a UId the same way.
	/// </summary>
	/// <returns>
	/// Whether a chunk came back EXACTLY full, i.e. rows may have been left behind. A <c>SelectQuery</c> is
	/// capped by <c>rowCount</c> and reports no overflow, so a name whose rows were cut off the result is
	/// INDISTINGUISHABLE from a name that does not exist — which is why a full read downgrades every absence
	/// verdict from <see cref="ActionTargetState.Missing"/> to <see cref="ActionTargetState.Unknown"/>.
	/// </returns>
	internal static bool ReadEntitySchemaRows(
		ProbeContext context, IReadOnlyList<string> names,
		IDictionary<string, string> uIdByName, ISet<string> seenNames) {
		bool truncated = false;
		foreach (IReadOnlyList<string> chunk in Chunk(names)) {
			int requestedRows = chunk.Count * RowsPerNameHeadroom;
			JObject query = ClassicEntitySchemaQuery.Query(
				SysSchemaName,
				new JObject {
					["Name"] = ClassicEntitySchemaQuery.Column("Name"),
					["UId"] = ClassicEntitySchemaQuery.Column("UId"),
					// ORDERED ascending, so the base row (ExtendParent = false) is always inside the row window.
					// A SelectQuery caps an UNORDERED result, and an OOTB object routinely carries more schema
					// layers than the per-name headroom: on a real stand Account, Lead, Activity and Case all
					// lost their base row to this window and degraded to Unknown.
					["ExtendParent"] = ClassicEntitySchemaQuery.ColumnOrderedAsc("ExtendParent")
				},
				ClassicEntitySchemaQuery.Group(
					("byName", ClassicEntitySchemaQuery.InFilter("Name", chunk, TextDataValueType)),
					("byManager", ClassicEntitySchemaQuery.Eq("ManagerName", EntitySchemaManagerName, TextDataValueType))),
				requestedRows);
			JArray rows = ClassicEntitySchemaQuery.Select(context.Client, context.UrlBuilder, query);
			truncated |= rows.Count >= requestedRows;
			foreach (JToken row in rows) {
				string name = row["Name"]?.ToString();
				if (string.IsNullOrWhiteSpace(name)) {
					continue;
				}
				seenNames.Add(name);
				string uId = row["UId"]?.ToString();
				if (row["ExtendParent"]?.Value<bool?>() == false && !string.IsNullOrWhiteSpace(uId)) {
					uIdByName[name] = uId;
				}
			}
		}
		return truncated;
	}

	/// <summary>
	/// The BATCHED half: resolves every pending candidate page UId to its schema NAME through
	/// <see cref="SchemaNameResolver.ResolveNames"/>, then re-records each object's resolution with the name
	/// that came back. Distinctions the old per-object lookup drew are preserved via
	/// <see cref="SchemaNameResolver.Status"/>:
	/// <list type="bullet">
	/// <item><description><see cref="SchemaNameResolver.Status.RowMissing"/> is a FAILURE (a dangling
	/// reference the add-on still names), exactly like the single-row lookup's "no row"
	/// branch;</description></item>
	/// <item><description><see cref="SchemaNameResolver.Status.NameInvalid"/> is a SILENT null candidate — the
	/// row exists, the object just has nothing offerable — so it is neither a failure nor
	/// recorded;</description></item>
	/// <item><description>a THROW from the resolver (transport, failure envelope —
	/// <c>DataServiceSelectResponse.ReadRows</c> throws on one) fails every candidate pending in this call,
	/// but never the verdicts, which were settled before this ran.</description></item>
	/// </list>
	/// </summary>
	private static void ResolveCandidateNames(
		ProbeContext context, IReadOnlyList<(string Name, Guid PageUId)> pending,
		IDictionary<string, ActionTargetResolution> into, CandidateFailureAccumulator candidateFailures) {
		if (pending.Count == 0) {
			return;
		}
		Guid[] pageUIds = [.. pending.Select(p => p.PageUId).Distinct()];
		IReadOnlyDictionary<Guid, SchemaNameResolver.Result> resolved;
		try {
			resolved = SchemaNameResolver.ResolveNames(context, pageUIds);
		} catch (Exception ex) {
			candidateFailures.RecordBatch(pending.Count, ex);
			return;
		}
		foreach ((string name, Guid pageUId) in pending) {
			SchemaNameResolver.Result result = resolved[pageUId];
			if (result.Status == SchemaNameResolver.Status.RowMissing) {
				candidateFailures.RecordFailure(new InvalidOperationException(
					$"Page schema '{pageUId}' could not be resolved to a name."));
				continue;
			}
			if (result.Status == SchemaNameResolver.Status.Resolved) {
				RecordEntityResolution(into, name, ActionTargetState.Missing, result.Name);
			}
		}
	}

	/// <summary>
	/// The untyped default page's <c>PageSchemaUId</c> out of a <c>RelatedPage</c>/<c>MobileRelatedPage</c>
	/// add-on's <c>metaData</c> — the same "at least one default page, any package" reading
	/// <see cref="ClassifyRelatedPageMetadata"/> applies to decide PRESENCE, but returning the UId to resolve
	/// instead of a state. Null for every shape that classifies as anything other than a real untyped default:
	/// blank/unparseable body, no <c>Pages</c> array, or a page set with no untyped <c>IsDefault</c> entry.
	/// </summary>
	internal static string ExtractDefaultPageSchemaUId(string metaData) {
		RelatedPageAddonMetadata.Result result = RelatedPageAddonMetadata.TryFindUntypedDefault(metaData);
		return result.Status == RelatedPageAddonMetadata.Status.DefaultFound ? result.PageSchemaUId : null;
	}

	/// <summary>
	/// Classifies <c>MobileRelatedPage</c> add-on metadata. A parsed body carrying a page set with no untyped
	/// default is <see cref="ActionTargetState.Missing"/> — the mobile app has nothing to open.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A BLANK body is <see cref="ActionTargetState.Unknown"/>, not <c>Missing</c>: that is the shape
	/// <c>AddonSchemaDto.MetaData</c> defaults to when the server did not answer with a payload at all, which
	/// says nothing about the object. An unconfigured object is NOT this shape — a stand returns a
	/// well-formed <c>{"Pages":[],"TypeColumnUId":null}</c> for one, which is a real answer and classifies as
	/// <c>Missing</c>.
	/// </para>
	/// <para>
	/// The mobile UI offers no record-type and no audience choice: an object has at most ONE mobile page. Every
	/// object observed on a stand matches that — <c>Contact</c>, <c>Account</c> and <c>Lead</c> each carry a
	/// single entry with <c>TypeColumnValue: null</c> under a null top-level <c>TypeColumnUId</c>, and the rest
	/// carry none. So <c>Role</c> is IGNORED and only an UNTYPED entry counts.
	/// </para>
	/// <para>
	/// Both reads are kept as GUARDS rather than as claims about the mobile UI. The serialized shape is shared
	/// with the web <c>RelatedPage</c> add-on, where record types and audiences are real, and clio's own writer
	/// applies one spec builder to both add-ons with no mobile-specific restriction — so a typed or
	/// audience-scoped entry can be WRITTEN into this add-on even though the mobile UI would not produce one.
	/// A <c>Role</c> seen on a mobile entry is not evidence to the contrary: the two observed are the GENERAL
	/// audience ("All employees", i.e. everyone), which is what a stamped default looks like, and <c>Lead</c>
	/// carries no role at all.
	/// </para>
	/// </remarks>
	/// <param name="metaData">The add-on's raw <c>metaData</c> JSON string.</param>
	/// <returns>Whether the object has a default mobile edit page.</returns>
	internal static ActionTargetState ClassifyRelatedPageMetadata(string metaData) =>
		RelatedPageAddonMetadata.TryFindUntypedDefault(metaData).Status switch {
			RelatedPageAddonMetadata.Status.DefaultFound => ActionTargetState.Resolved,
			RelatedPageAddonMetadata.Status.NoDefault => ActionTargetState.Missing,
			_ => ActionTargetState.Unknown
		};

	/// <summary>
	/// Splits <paramref name="values"/> (entity names, page UIds) into ESQ-safe batches. Every <c>IN</c>
	/// value costs a query parameter and MSSql caps a statement at 2100, so a page-driven value set must be
	/// chunked (<see cref="ClassicEntitySchemaQuery.InFilterChunkSize"/>) or the whole query throws.
	/// </summary>
	private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> values) {
		for (int start = 0; start < values.Count; start += ClassicEntitySchemaQuery.InFilterChunkSize) {
			yield return values
				.Skip(start)
				.Take(ClassicEntitySchemaQuery.InFilterChunkSize)
				.ToArray();
		}
	}

	private static void Record(
		IDictionary<string, ActionTargetResolution> into, string kind, string target, ActionTargetState state) =>
		into[TargetKey(kind, target)] = new ActionTargetResolution { Kind = kind, Target = target, State = state };

	/// <summary>
	/// <see cref="Record"/> for an entity target, additionally carrying the candidate web page
	/// <see cref="ResolveCandidateNames"/> resolved (null unless <paramref name="state"/> is
	/// <see cref="ActionTargetState.Missing"/> and a candidate was actually found).
	/// </summary>
	private static void RecordEntityResolution(
		IDictionary<string, ActionTargetResolution> into, string target, ActionTargetState state,
		string resolvedCandidateSchemaName) =>
		into[TargetKey(KindEntityDefaultMobilePage, target)] = new ActionTargetResolution {
			Kind = KindEntityDefaultMobilePage, Target = target, State = state,
			ResolvedCandidateSchemaName = resolvedCandidateSchemaName
		};

	/// <summary>
	/// Composes a degradation note from a LOCALLY authored sentence plus, when there is one, the failure
	/// detail neutralized as data.
	/// </summary>
	/// <remarks>
	/// <see cref="SensitiveErrorTextRedactor.RedactUntrustedOrNull"/>, never plain <c>Redact</c>: this note is
	/// surfaced to the MCP caller on the guide (<c>requestConversions.targetsNote</c>) in a server whose tool
	/// surface includes destructive tools, and the failure detail is prose a third party CHOOSES rather than
	/// values inside prose clio wrote. A DataService failure envelope carries the server's own
	/// <c>errorInfo.message</c> verbatim (<c>DataServiceSelectResponse</c> interpolates it), and the page-walk
	/// path can quote a page-authored key. <c>Redact</c> scrubs URIs, paths and credential shapes and has no
	/// opinion on prose, line breaks or length — so on its own it neither bounds an unbounded body nor stops a
	/// forged instruction block. The untrusted variant additionally flattens, clamps and fences the text, and
	/// returns null when there is nothing to say so the sentence stands alone.
	/// </remarks>
	private static string Describe(string what, Exception ex) {
		string detail = SensitiveErrorTextRedactor.RedactUntrustedOrNull(ex?.Message);
		return string.IsNullOrEmpty(detail) ? $"{what}." : $"{what}: {detail}.";
	}

	/// <summary>
	/// A degraded result: the OBJECT tier could not answer. Any resolution already settled WITHOUT the
	/// environment — a web-page target is dead by construction — is carried through, because a tier that
	/// never needed the environment is not made doubtful by the environment failing. Dropping them was how a
	/// page carrying one web-page target AND one object target silently lost the web-page warning that a page
	/// carrying the web-page target alone reports fine.
	/// </summary>
	/// <param name="occurrences">Where target-carrying requests appear on the page; collected offline.</param>
	/// <param name="note">Why the object tier did not answer, for the caller's <c>targetsNote</c>.</param>
	/// <param name="settled">Resolutions that needed no environment read; empty when the walk itself failed.</param>
	private static MobileActionTargetProbeResult NotProbed(
		IReadOnlyList<ActionTargetOccurrence> occurrences, string note,
		IReadOnlyDictionary<string, ActionTargetResolution> settled = null) =>
		new() {
			ProbeOk = false, Note = note, Occurrences = occurrences,
			TargetsByKey = settled ?? new Dictionary<string, ActionTargetResolution>(StringComparer.OrdinalIgnoreCase)
		};
}
