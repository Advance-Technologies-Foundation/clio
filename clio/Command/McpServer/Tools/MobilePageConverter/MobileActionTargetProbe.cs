using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Common;
using Newtonsoft.Json.Linq;
// The schema-manager names are shared with the business-rule probe in this directory rather than
// re-declared: they name platform managers, and two copies can drift apart across a platform rename.
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Read-only environment probe that answers, for every action the source page fires, whether the thing it
/// NAVIGATES TO exists on mobile (ENG-94839). Whether the request TYPE converts is a separate question,
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
/// It performs DataService <c>SelectQuery</c> reads plus one designer read per object and issues no write
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
/// failed, while the same verdict on a page carrying only web-page targets reported fine (ENG-94839).
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

	private const string MobileRelatedPageAddonName = "MobileRelatedPage";
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
	/// Ceiling on the per-object add-on reads (one <c>GetSchema</c> round trip each), so a page firing
	/// create/update actions at many objects cannot turn one guide call into an unbounded fan of round trips.
	/// Overflowing it fails OPEN: every object past the ceiling resolves to
	/// <see cref="ActionTargetState.Unknown"/> and <see cref="MobileActionTargetProbeResult.Note"/> says so,
	/// because "not asked" must not read as "asked, and the answer was no".
	/// </summary>
	private const int MaxEntityAddonProbes = 8;

	/// <summary>
	/// Row headroom per requested name: a schema appears as a base row plus one row per replacing layer, and
	/// there is no bound on how many layers an object carries. The read ORDERS base rows first
	/// (<see cref="ClassicEntitySchemaQuery.ColumnOrderedAsc"/>), so this cap can no longer cost the base row
	/// — it only decides how many extra layers are seen, which is what separates "this object has no rows at
	/// all" (absent) from "rows but no base row" (unknown).
	/// </summary>
	private const int RowsPerNameHeadroom = 4;

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
			var options = new EnvironmentOptions {
				Environment = environment, Uri = uri, Login = login, Password = password
			};
			var context = new ProbeContext(
				commandResolver, options,
				commandResolver.Resolve<IApplicationClient>(options),
				commandResolver.Resolve<IServiceUrlBuilder>(options),
				// Resolved ONCE rather than per object: the add-on read runs up to MaxEntityAddonProbes times,
				// and re-resolving inside that loop buys nothing but container work.
				commandResolver.Resolve<IAddonSchemaDesignerClient>(options));

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
		string primaryName = Str(modelConfig, "primaryDataSourceName");
		JsonObject primary =
			(!string.IsNullOrWhiteSpace(primaryName) ? dataSources[primaryName] : null) as JsonObject
			?? dataSources["PDS"] as JsonObject;
		if (primary?["config"] is JsonObject config
			&& Str(config, "entitySchemaName") is { } entity && !string.IsNullOrWhiteSpace(entity)) {
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
	/// Whether a <see cref="ActionTargetState.Missing"/> verdict on this kind is strong enough to REMOVE the
	/// action's binding, as opposed to only reporting it. The single seam that decides it, so the rule lives
	/// in one place instead of being re-derived at each consumer.
	/// <para>
	/// Only <see cref="KindWebPage"/> qualifies, and it qualifies because its verdict is DEFINITIONAL: the
	/// value names a web page by construction, the Creatio Mobile app cannot open one, and no environment
	/// read was involved — so the verdict cannot be wrong for a reason outside this process.
	/// </para>
	/// <para>
	/// <see cref="KindEntityDefaultMobilePage"/> deliberately does NOT qualify. Its verdict comes from reading
	/// the object's <c>MobileRelatedPage</c> add-on, and that read addresses the add-on more cheaply than
	/// <c>RelatedPageAddonService.BuildAddonGetRequest</c> does — no resolved parent schema, and the SOURCE
	/// page's package rather than the object's own. A body carrying no page set classifies as
	/// <see cref="ActionTargetState.Missing"/>, which is ALSO the shape a mis-addressed read plausibly returns
	/// (the server auto-provisions the descriptor), and the two cannot be told apart by inspection. Removing a
	/// working action on that is not a risk worth taking for a diagnosis the report already delivers, so an
	/// object target is reported and left alone (ENG-94839; removal is ENG-96178 / ENG-95084's scope).
	/// </para>
	/// </summary>
	/// <param name="kind">A rules-declared <c>targetKind</c>.</param>
	/// <returns>Whether a verified absence of this kind removes the binding.</returns>
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
		string elementName = Str(obj, NameProperty) ?? enclosingName;
		foreach (KeyValuePair<string, JsonNode> property in obj) {
			if (property.Value is JsonObject candidate && Str(candidate, RequestProperty) is { } request
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
			? Str(parameters, rule.TargetParam)?.Trim()
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
	/// The per-call environment seam, threaded through the resolution passes as one value. Carrying the
	/// resolver alongside the two clients it produced keeps every read on the SAME per-call container: a pass
	/// that re-resolved from somewhere else could silently answer for a different tenant.
	/// </summary>
	/// <summary>
	/// What the object tier did. <c>Answered</c> is what <see cref="MobileActionTargetProbeResult.ProbeOk"/>
	/// reports, so a tier that performed NO read must say false however cleanly it returned — otherwise the
	/// caller is told the object targets were verified when nothing was asked. <c>Note</c> is null only when
	/// the tier ran in full; it is set both when the tier did not run and when it ran incompletely.
	/// </summary>
	private sealed record EntityTierOutcome(bool Answered, string Note);

	private sealed record ProbeContext(
		IToolCommandResolver Resolver, EnvironmentOptions Options,
		IApplicationClient Client, IServiceUrlBuilder UrlBuilder,
		IAddonSchemaDesignerClient AddonClient);

	/// <summary>
	/// Resolves every <see cref="KindEntityDefaultMobilePage"/> target: one batched <c>SysSchema</c> read for
	/// the objects' base-row UIds, then one <c>MobileRelatedPage</c> add-on read per object. That add-on is
	/// what the Creatio Mobile app resolves for a create/update-record action, and it is the same add-on
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

		var uIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool truncated = ReadEntitySchemaRows(context, names, uIdByName, seenNames);

		int budget = MaxEntityAddonProbes;
		bool budgetExhausted = false;
		foreach (string name in names) {
			if (!uIdByName.TryGetValue(name, out string entityUId)) {
				// No rows at all: the object does not exist, so the action is dead — unless the read may have
				// been truncated, when "no row" cannot be told apart from "the row was cut off the result".
				// Rows but no base row: the object cannot be addressed reliably, so refuse to guess (the
				// ResolveEntityUId rule).
				Record(into, KindEntityDefaultMobilePage, name,
					seenNames.Contains(name) || truncated ? ActionTargetState.Unknown : ActionTargetState.Missing);
				continue;
			}
			if (budget-- <= 0) {
				// Fail open AND say so: an unasked target must not look like one the environment answered "no" to.
				budgetExhausted = true;
				Record(into, KindEntityDefaultMobilePage, name, ActionTargetState.Unknown);
				continue;
			}
			Record(into, KindEntityDefaultMobilePage, name,
				ClassifyEntityDefaultMobilePage(context, entityUId, packageUId));
		}
		// Answered either way: the reads that ran did succeed. The note is what says some were never asked.
		return new EntityTierOutcome(true, budgetExhausted
			? $"Only the first {MaxEntityAddonProbes} object targets were checked; the rest are reported as "
				+ "unverified. Check them manually."
			: null);
	}

	/// <summary>
	/// Reads the objects' <c>SysSchema</c> rows and keeps the BASE row UId per name — the stable unit, exactly
	/// as <c>ClassicEntitySchemaQuery.ResolveEntityUId</c> picks it; a replacing layer is not a
	/// different object and must not be addressed instead. <paramref name="seenNames"/> separates "no rows at
	/// all" from "rows but no base row", which resolve differently.
	/// </summary>
	/// <returns>
	/// Whether a chunk came back EXACTLY full, i.e. rows may have been left behind. A <c>SelectQuery</c> is
	/// capped by <c>rowCount</c> and reports no overflow, so a name whose rows were cut off the result is
	/// INDISTINGUISHABLE from a name that does not exist — which is why a full read downgrades every absence
	/// verdict from <see cref="ActionTargetState.Missing"/> to <see cref="ActionTargetState.Unknown"/>.
	/// </returns>
	private static bool ReadEntitySchemaRows(
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
	/// Reads the object's <c>MobileRelatedPage</c> add-on and reports whether it declares a default page.
	/// Degrades per object rather than aborting the probe.
	/// </summary>
	private static ActionTargetState ClassifyEntityDefaultMobilePage(
		ProbeContext context, string entitySchemaUId, Guid packageUId) {
		if (!Guid.TryParse(entitySchemaUId, out Guid entityUId)) {
			return ActionTargetState.Unknown;
		}
		try {
			AddonSchemaDto schema = context.AddonClient.GetSchema(new AddonGetRequestDto {
				AddonName = MobileRelatedPageAddonName,
				TargetSchemaUId = entityUId,
				TargetParentSchemaUId = Guid.Empty,
				TargetPackageUId = packageUId,
				TargetSchemaManagerName = EntitySchemaManagerName,
				UseFullHierarchy = true
			});
			return ClassifyRelatedPageMetadata(schema?.MetaData);
		} catch (Exception) {
			return ActionTargetState.Unknown;
		}
	}

	/// <summary>
	/// Classifies <c>MobileRelatedPage</c> add-on metadata. A parsed body carrying a page set with no untyped
	/// default is <see cref="ActionTargetState.Missing"/> — the mobile app has nothing to open.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A BLANK body is deliberately <see cref="ActionTargetState.Unknown"/>, not <c>Missing</c>. It is the
	/// shape a never-configured object returns AND the shape a request the server did not understand returns
	/// (<c>AddonSchemaDto.MetaData</c> defaults to empty), and the two cannot be told apart by inspection.
	/// Since this probe addresses the add-on more cheaply than <c>RelatedPageAddonService</c> does — no
	/// resolved parent schema, and the SOURCE page's package rather than the object's own — a blank body is a
	/// realistic symptom of that shortcut, and a shortcut must never present as a verified absence.
	/// </para>
	/// <para>
	/// Only an UNTYPED default counts. Record types and roles are web-only concepts on this add-on (see
	/// <c>create-related-page-addon</c>), so a typed entry is not the page a plain create/update action opens.
	/// </para>
	/// </remarks>
	/// <param name="metaData">The add-on's raw <c>metaData</c> JSON string.</param>
	/// <returns>Whether the object has a default mobile edit page.</returns>
	internal static ActionTargetState ClassifyRelatedPageMetadata(string metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return ActionTargetState.Unknown;
		}
		try {
			if (JsonNode.Parse(metaData) is not JsonObject obj) {
				return ActionTargetState.Unknown;
			}
			if (obj["Pages"] is not JsonArray pages) {
				return ActionTargetState.Missing;
			}
			foreach (JsonNode page in pages) {
				if (page is JsonObject entry && Bool(entry, "IsDefault")
					&& string.IsNullOrWhiteSpace(Str(entry, "TypeColumnValue"))) {
					return ActionTargetState.Resolved;
				}
			}
			return ActionTargetState.Missing;
		} catch (Exception) {
			// Every read is inside the try, not just the parse: this is internal and directly unit-tested, so
			// a second caller could reach it without the outer degradation the probe's own path provides.
			return ActionTargetState.Unknown;
		}
	}

	/// <summary>
	/// Splits <paramref name="names"/> into ESQ-safe batches. Every <c>IN</c> value costs a query parameter and
	/// MSSql caps a statement at 2100, so a page-driven value set must be chunked
	/// (<see cref="ClassicEntitySchemaQuery.InFilterChunkSize"/>) or the whole query throws.
	/// </summary>
	private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> names) {
		for (int start = 0; start < names.Count; start += ClassicEntitySchemaQuery.InFilterChunkSize) {
			yield return names
				.Skip(start)
				.Take(ClassicEntitySchemaQuery.InFilterChunkSize)
				.ToArray();
		}
	}

	private static void Record(
		IDictionary<string, ActionTargetResolution> into, string kind, string target, ActionTargetState state) =>
		into[TargetKey(kind, target)] = new ActionTargetResolution { Kind = kind, Target = target, State = state };

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
	/// carrying the web-page target alone reports fine (ENG-94839).
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

	/// <summary>
	/// Reads a scalar property as text. A non-string scalar falls back to its literal form, matching
	/// <c>RelatedPageAddonService</c>'s reader: this probe classifies the SAME add-on metadata, and a numeric
	/// <c>TypeColumnValue</c> read as null there would make a TYPED entry look like the untyped default.
	/// </summary>
	private static string Str(JsonObject obj, string property) {
		if (obj is null || string.IsNullOrEmpty(property)
			|| !obj.TryGetPropertyValue(property, out JsonNode node) || node is not JsonValue value) {
			return null;
		}
		return value.TryGetValue(out string text) ? text : value.ToJsonString().Trim('"');
	}

	/// <summary>
	/// Reads a boolean property, tolerating a bool stored as the STRING <c>"true"</c>. Deliberately as lenient
	/// as <c>RelatedPageAddonService</c>'s reader: the two surfaces classify the same persisted add-on, and a
	/// stricter read here would report "no default mobile page" for a record the other surface reports as the
	/// default — a disagreement that lands on the false-alarm side.
	/// </summary>
	private static bool Bool(JsonObject obj, string property) {
		if (obj is null || !obj.TryGetPropertyValue(property, out JsonNode node) || node is not JsonValue value) {
			return false;
		}
		if (value.TryGetValue(out bool flag)) {
			return flag;
		}
		return value.TryGetValue(out string text) && bool.TryParse(text, out bool parsed) && parsed;
	}
}
