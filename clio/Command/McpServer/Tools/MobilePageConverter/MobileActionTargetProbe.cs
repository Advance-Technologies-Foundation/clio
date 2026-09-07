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
/// It performs DataService <c>SelectQuery</c> reads plus two designer reads and never writes. It NEVER
/// throws: any failure degrades to <see cref="MobileActionTargetProbeResult.ProbeOk"/> = false with an
/// EMPTY resolution map, which every consumer reads as <see cref="ActionTargetState.Unknown"/> — the
/// fail-open value. Nothing is ever removed from the page on missing information, matching
/// <c>WebToMobileAnalysisService.RetargetTargetMissing</c>, which likewise refuses to conclude "absent"
/// from an unread probe.
/// </para>
/// </summary>
public static class MobileActionTargetProbe {

	/// <summary>Target kind: the value names a page schema that must itself be a mobile page.</summary>
	internal const string KindMobilePage = "mobile-page";

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
	/// Ceiling on the AUTHORITATIVE per-page escalation (a designer <c>GetParentSchemas</c> round trip each).
	/// Only pages the cheap parent-template classification could not place reach it, so an ordinary page needs
	/// none; the cap exists so a pathological page cannot turn one guide call into dozens of POSTs. A target
	/// past the budget is <see cref="ActionTargetState.Unknown"/>, never <see cref="ActionTargetState.Missing"/>.
	/// </summary>
	private const int MaxAuthoritativeProbes = 5;

	/// <summary>
	/// Ceiling on the per-object add-on reads (one <c>GetSchema</c> round trip each). Same rationale and same
	/// fail-open overflow as <see cref="MaxAuthoritativeProbes"/>.
	/// </summary>
	private const int MaxEntityAddonProbes = 8;

	/// <summary>
	/// Row headroom per requested name: a schema can appear as a base row plus replacing layers, and a
	/// truncated result would be misread as "this layer does not exist".
	/// </summary>
	private const int RowsPerNameHeadroom = 4;

	/// <summary>
	/// Mobile page template roots shipped with clio, used when the environment's template catalog cannot be
	/// read. The authoritative set is the union of this, the versioned rules file's mobile templates, and the
	/// environment's own catalog — no single source is trusted alone: the platform catalog serves a CURATED
	/// subset (see <see cref="SchemaTemplateCatalog"/>), and the rules file only names templates it maps.
	/// Documented under "Template Hierarchy" in <c>spec/mobile-pages/mobile-pages-reference.md</c>.
	/// </summary>
	private static readonly string[] BundledMobileTemplateRoots = [
		"BlankMobilePageTemplate",
		"BaseMobileTemplate",
		"BaseMobilePageTemplate",
		"MobilePageWithTabsFreedomTemplate",
		"BaseMobileListTemplate"
	];

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
	/// <param name="viewConfig">The source page's merged <c>viewConfig</c> — the only place bindings are read from.</param>
	/// <param name="rules">Resolved conversion rules; their <c>requests</c> section declares which targets to check.</param>
	/// <param name="modelConfig">
	/// The source page's merged <c>modelConfig</c>. Only its data-source entity names are read, to exempt the
	/// objects THIS conversion is itself about — see <see cref="CollectSourceEntityNames"/>.
	/// </param>
	/// <param name="pagePackageUId">The source page's package UId, used to address an object's add-on.</param>
	/// <param name="designPackageUId">The source page's design package UId, used for the designer hierarchy read.</param>
	/// <returns>The occurrences found on the page and, when the reads succeeded, one resolution per distinct target.</returns>
	public static MobileActionTargetProbeResult Probe(
		IToolCommandResolver commandResolver,
		string environment, string uri, string login, string password,
		JsonArray viewConfig,
		WebToMobilePageConversionRules rules,
		JsonObject modelConfig,
		string pagePackageUId,
		string designPackageUId) {
		IReadOnlyDictionary<string, RequestMappingRule> targeted = BuildTargetedRequestMap(rules);
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
			occurrences = CollectActionTargets(viewConfig, targeted);
			sourceEntities = CollectSourceEntityNames(modelConfig);
		} catch (Exception ex) {
			return NotProbed([], $"Could not read the page's action bindings ({ex.Message}).");
		}
		if (occurrences.Count == 0) {
			// Nothing on the page navigates anywhere: the check ran and found nothing to verify.
			return new MobileActionTargetProbeResult { ProbeOk = true };
		}
		if (commandResolver is null) {
			return NotProbed(occurrences, "Action targets were not verified (missing environment client).");
		}

		try {
			var options = new EnvironmentOptions {
				Environment = environment, Uri = uri, Login = login, Password = password
			};
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			var resolutions = new Dictionary<string, ActionTargetResolution>(StringComparer.OrdinalIgnoreCase);
			ResolvePageTargets(commandResolver, options, client, urlBuilder, rules,
				DistinctTargetsOfKind(occurrences, KindMobilePage), designPackageUId, resolutions);
			ResolveEntityTargets(commandResolver, options, client, urlBuilder,
				DistinctTargetsOfKind(occurrences, KindEntityDefaultMobilePage), pagePackageUId,
				sourceEntities, resolutions);

			return new MobileActionTargetProbeResult {
				ProbeOk = true, Occurrences = occurrences, TargetsByKey = resolutions
			};
		} catch (Exception ex) {
			// Covers the DataService failure envelope and a non-JSON body alike: IApplicationClient returns a
			// proxy/auth error PAGE as an ordinary string rather than throwing, so it is the SelectQuery helper
			// this file calls that raises it. Reading such a body as "no rows" would report every target as
			// absent and tell the user to delete working controls.
			//
			// The message is REDACTED because this note is surfaced to the MCP caller on the guide
			// (requestConversions.targetsNote): a Creatio read failure routinely names the tenant host, the
			// integration login (a SecurityException quotes it), or a local settings path.
			return NotProbed(occurrences,
				$"Could not verify action targets ({SensitiveErrorTextRedactor.Redact(ex.Message)}). "
				+ "Check each action's target page manually.");
		}
	}

	/// <summary>
	/// The requests whose target this build knows how to verify: a rule declaring BOTH a <c>targetParam</c>
	/// and a RECOGNIZED <c>targetKind</c>. Everything else is deliberately absent, so it is never checked.
	/// </summary>
	private static IReadOnlyDictionary<string, RequestMappingRule> BuildTargetedRequestMap(
		WebToMobilePageConversionRules rules) {
		var map = new Dictionary<string, RequestMappingRule>(StringComparer.OrdinalIgnoreCase);
		foreach (RequestMappingRule rule in rules?.Requests ?? []) {
			if (!string.IsNullOrWhiteSpace(rule?.Web)
				&& !string.IsNullOrWhiteSpace(rule.TargetParam)
				&& IsRecognizedKind(rule.TargetKind)) {
				map[rule.Web] = rule;
			}
		}
		return map;
	}

	/// <summary>
	/// The objects the source page's own data sources are bound to, read from
	/// <c>modelConfig.dataSources.*.config.entitySchemaName</c>.
	/// <para>
	/// These are EXEMPT from the default-mobile-page check, and that exemption is load-bearing. A record page
	/// for <c>Lead</c> routinely carries a "create Lead" action; before the conversion runs there is no
	/// <c>MobileRelatedPage</c> add-on for <c>Lead</c> — creating it IS the conversion's own closing step, the
	/// one <see cref="MobileSectionRegistrationProbe"/> spells out as "register it as the object's default
	/// MOBILE edit page". Reporting it absent would make one response tell the caller to register that page
	/// and to delete the control that opens it.
	/// </para>
	/// </summary>
	internal static IReadOnlySet<string> CollectSourceEntityNames(JsonObject modelConfig) {
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (modelConfig?["dataSources"] is not JsonObject dataSources) {
			return names;
		}
		foreach (KeyValuePair<string, JsonNode> dataSource in dataSources) {
			if (dataSource.Value is JsonObject source && source["config"] is JsonObject config
				&& Str(config, "entitySchemaName") is { } entity && !string.IsNullOrWhiteSpace(entity)) {
				names.Add(entity.Trim());
			}
		}
		return names;
	}

	/// <summary>Whether this build knows how to verify a target of <paramref name="kind"/>.</summary>
	private static bool IsRecognizedKind(string kind) =>
		string.Equals(kind, KindMobilePage, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(kind, KindEntityDefaultMobilePage, StringComparison.OrdinalIgnoreCase);

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
		var ordered = new List<string>();
		foreach (ActionTargetOccurrence occurrence in occurrences) {
			if (string.Equals(occurrence.Kind, kind, StringComparison.OrdinalIgnoreCase)
				&& seen.Add(occurrence.Target)) {
				ordered.Add(occurrence.Target);
			}
		}
		return ordered;
	}

	/// <summary>
	/// Resolves every <see cref="KindMobilePage"/> target: ONE batched <c>SysSchema</c> read classifies each
	/// name by its parent template, and only the residue no template set places escalates to the authoritative
	/// designer read. The numeric client-unit schema type (9 web / 10 mobile) is NOT a queryable
	/// <c>SysSchema</c> column — it lives in the schema metadata and is only exposed by
	/// <see cref="IPageDesignerHierarchyClient.GetParentSchemas"/> — which is why this is two-tier rather than
	/// one authoritative query.
	/// </summary>
	private static void ResolvePageTargets(
		IToolCommandResolver commandResolver, EnvironmentOptions options,
		IApplicationClient client, IServiceUrlBuilder urlBuilder,
		WebToMobilePageConversionRules rules,
		IReadOnlyList<string> names, string designPackageUId,
		IDictionary<string, ActionTargetResolution> into) {
		if (names.Count == 0) {
			return;
		}
		(HashSet<string> mobileRoots, HashSet<string> webRoots) = LoadTemplateRoots(commandResolver, options, rules);
		SchemaLookup<PageSchemaRow> lookup = ReadPageSchemaRows(client, urlBuilder, names);

		var residue = new List<(string Name, string UId)>();
		foreach (string name in names) {
			if (!lookup.RowsByName.TryGetValue(name, out List<PageSchemaRow> rows) || rows.Count == 0) {
				// No client-unit schema of that name — unambiguous, and independent of any template allowlist:
				// the action opens something that does not exist. UNLESS the read may have been truncated, in
				// which case "no row" cannot be told apart from "the row was cut off the result".
				Record(into, KindMobilePage, name,
					lookup.PossiblyTruncated ? ActionTargetState.Unknown : ActionTargetState.Missing);
				continue;
			}
			if (rows.Any(row => mobileRoots.Contains(row.ParentName ?? string.Empty))) {
				Record(into, KindMobilePage, name, ActionTargetState.Resolved);
				continue;
			}
			if (!lookup.PossiblyTruncated
				&& rows.All(row => !string.IsNullOrWhiteSpace(row.ParentName) && webRoots.Contains(row.ParentName))) {
				// It exists, but every layer descends from a WEB template — a page that was never converted.
				// Skipped on a truncated read for the same reason: a mobile-rooted layer may simply be missing
				// from the rows, and concluding "web-only" from a partial set would be a guess.
				Record(into, KindMobilePage, name, ActionTargetState.Missing);
				continue;
			}
			residue.Add((name, rows[0].UId));
		}

		int budget = MaxAuthoritativeProbes;
		foreach ((string Name, string UId) target in residue) {
			ActionTargetState state = budget-- > 0
				? ClassifyPageByHierarchy(commandResolver, options, target.UId, designPackageUId)
				: ActionTargetState.Unknown;
			Record(into, KindMobilePage, target.Name, state);
		}
	}

	/// <summary>One <c>SysSchema</c> row of a client-unit schema, as the batched classification reads it.</summary>
	private sealed record PageSchemaRow(string UId, string ParentName);

	/// <summary>
	/// The result of a batched <c>SysSchema</c> read.
	/// <para>
	/// <paramref name="PossiblyTruncated"/> is the load-bearing half. A <c>SelectQuery</c> is capped by
	/// <c>rowCount</c> and reports no overflow, so a chunk that came back exactly full may have left rows
	/// behind — and a name whose rows were left behind is INDISTINGUISHABLE from a name that does not exist.
	/// Concluding <see cref="ActionTargetState.Missing"/> from that would tell the user to delete a control
	/// whose target is actually there, so every absence-flavoured verdict is downgraded to
	/// <see cref="ActionTargetState.Unknown"/> when this is set.
	/// </para>
	/// </summary>
	private sealed record SchemaLookup<TRow>(
		IReadOnlyDictionary<string, List<TRow>> RowsByName, bool PossiblyTruncated);

	/// <summary>
	/// Reads the <c>SysSchema</c> rows for every named page in as few round trips as the ESQ parameter cap
	/// allows. A page can have several rows (a base schema plus replacing layers), so rows are grouped by name
	/// and the classification folds over all of them.
	/// </summary>
	private static SchemaLookup<PageSchemaRow> ReadPageSchemaRows(
		IApplicationClient client, IServiceUrlBuilder urlBuilder, IReadOnlyList<string> names) {
		var byName = new Dictionary<string, List<PageSchemaRow>>(StringComparer.OrdinalIgnoreCase);
		bool truncated = false;
		foreach (IReadOnlyList<string> chunk in Chunk(names)) {
			int requestedRows = chunk.Count * RowsPerNameHeadroom;
			JObject query = ClassicEntitySchemaQuery.Query(
				SysSchemaName,
				new JObject {
					["Name"] = ClassicEntitySchemaQuery.Column("Name"),
					["UId"] = ClassicEntitySchemaQuery.Column("UId"),
					["ParentName"] = ClassicEntitySchemaQuery.Column("[SysSchema:Id:Parent].Name")
				},
				ClassicEntitySchemaQuery.Group(
					("byName", ClassicEntitySchemaQuery.InFilter("Name", chunk, TextDataValueType)),
					("byManager", ClassicEntitySchemaQuery.Eq("ManagerName", ClientUnitSchemaManagerName, TextDataValueType))),
				requestedRows);
			JArray rows = ClassicEntitySchemaQuery.Select(client, urlBuilder, query);
			truncated |= rows.Count >= requestedRows;
			foreach (JToken row in rows) {
				string name = row["Name"]?.ToString();
				if (string.IsNullOrWhiteSpace(name)) {
					continue;
				}
				if (!byName.TryGetValue(name, out List<PageSchemaRow> pageRows)) {
					pageRows = [];
					byName[name] = pageRows;
				}
				pageRows.Add(new PageSchemaRow(row["UId"]?.ToString(), row["ParentName"]?.ToString()));
			}
		}
		return new SchemaLookup<PageSchemaRow>(byName, truncated);
	}

	/// <summary>
	/// The authoritative per-page answer: the designer hierarchy carries the numeric schema type, so index 0
	/// (the schema itself) settles web vs mobile. Fails to <see cref="ActionTargetState.Unknown"/> on its own
	/// rather than aborting the probe, so one unreadable target never suppresses the other targets' verdicts.
	/// </summary>
	private static ActionTargetState ClassifyPageByHierarchy(
		IToolCommandResolver commandResolver, EnvironmentOptions options,
		string pageSchemaUId, string designPackageUId) {
		if (!Guid.TryParse(pageSchemaUId, out _)) {
			return ActionTargetState.Unknown;
		}
		try {
			IPageDesignerHierarchyClient hierarchyClient =
				commandResolver.Resolve<IPageDesignerHierarchyClient>(options);
			// The design package is schema-specific ("where a new replacing schema would be created"), and the
			// schema being read here is NOT the source page — so resolve it for the target, exactly as
			// PageBusinessRuleSchemaProvider and ClassicListColumnResolver do. The source page's package is the
			// fallback only: a wrong package context can change WHICH layer answers, and answering from the
			// wrong layer is how a mobile page gets classified off someone else's hierarchy.
			string targetDesignPackageUId = ResolveDesignPackageOrFallback(
				hierarchyClient, pageSchemaUId, designPackageUId);
			IReadOnlyList<PageDesignerHierarchySchema> hierarchy =
				hierarchyClient.GetParentSchemas(pageSchemaUId, targetDesignPackageUId);
			if (hierarchy is null || hierarchy.Count == 0) {
				return ActionTargetState.Unknown;
			}
			return PageSchemaTypeExtensions.FromNumericValue(hierarchy[0].SchemaType) switch {
				PageSchemaType.Mobile => ActionTargetState.Resolved,
				PageSchemaType.Web => ActionTargetState.Missing,
				_ => ActionTargetState.Unknown
			};
		} catch (Exception) {
			return ActionTargetState.Unknown;
		}
	}

	/// <summary>
	/// The design package for <paramref name="pageSchemaUId"/>, falling back to <paramref name="fallback"/>
	/// (the source page's) when the platform cannot answer. Best-effort by design: a wrong package still
	/// yields either a correct answer or a failure that degrades to
	/// <see cref="ActionTargetState.Unknown"/>, so this must never abort the classification.
	/// </summary>
	private static string ResolveDesignPackageOrFallback(
		IPageDesignerHierarchyClient hierarchyClient, string pageSchemaUId, string fallback) {
		try {
			string resolved = hierarchyClient.GetDesignPackageUId(pageSchemaUId);
			return string.IsNullOrWhiteSpace(resolved) ? fallback : resolved;
		} catch (Exception) {
			return fallback;
		}
	}

	/// <summary>
	/// Resolves every <see cref="KindEntityDefaultMobilePage"/> target: one batched <c>SysSchema</c> read for
	/// the objects' base-row UIds, then one <c>MobileRelatedPage</c> add-on read per object. That add-on is
	/// what the Creatio Mobile app resolves for a create/update-record action, and it is the same add-on
	/// <c>create-related-page-addon --schema-type mobile</c> writes at the end of a conversion.
	/// </summary>
	private static void ResolveEntityTargets(
		IToolCommandResolver commandResolver, EnvironmentOptions options,
		IApplicationClient client, IServiceUrlBuilder urlBuilder,
		IReadOnlyList<string> names, string pagePackageUId,
		IReadOnlySet<string> sourceEntities,
		IDictionary<string, ActionTargetResolution> into) {
		// The page's own objects are what this conversion is FOR; their mobile page does not exist yet by
		// definition. Answer Unknown without a round trip — see CollectSourceEntityNames.
		names = [.. names.Where(name => !sourceEntities.Contains(name))];
		foreach (string sourceEntity in sourceEntities) {
			Record(into, KindEntityDefaultMobilePage, sourceEntity, ActionTargetState.Unknown);
		}
		if (names.Count == 0) {
			return;
		}
		if (!Guid.TryParse(pagePackageUId, out Guid packageUId)) {
			// The add-on read is addressed by package; without one nothing can be verified. Unknown, not
			// Missing — the absence is in clio's inputs, not in the environment.
			foreach (string name in names) {
				Record(into, KindEntityDefaultMobilePage, name, ActionTargetState.Unknown);
			}
			return;
		}

		var uIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool truncated = ReadEntitySchemaRows(client, urlBuilder, names, uIdByName, seenNames);

		int budget = MaxEntityAddonProbes;
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
			ActionTargetState state = budget-- > 0
				? ClassifyEntityDefaultMobilePage(commandResolver, options, entityUId, packageUId)
				: ActionTargetState.Unknown;
			Record(into, KindEntityDefaultMobilePage, name, state);
		}
	}

	/// <summary>
	/// Reads the objects' <c>SysSchema</c> rows and keeps the BASE row UId per name — the stable unit, exactly
	/// as <see cref="ClassicEntitySchemaQuery.ResolveEntityUId"/> picks it; a replacing layer is not a
	/// different object and must not be addressed instead. <paramref name="seenNames"/> separates "no rows at
	/// all" from "rows but no base row", which resolve differently.
	/// </summary>
	/// <returns>
	/// Whether a chunk came back exactly full, i.e. rows may have been left behind — see
	/// <see cref="SchemaLookup{TRow}"/> for why that must downgrade every absence verdict.
	/// </returns>
	private static bool ReadEntitySchemaRows(
		IApplicationClient client, IServiceUrlBuilder urlBuilder, IReadOnlyList<string> names,
		IDictionary<string, string> uIdByName, ISet<string> seenNames) {
		bool truncated = false;
		foreach (IReadOnlyList<string> chunk in Chunk(names)) {
			int requestedRows = chunk.Count * RowsPerNameHeadroom;
			JObject query = ClassicEntitySchemaQuery.Query(
				SysSchemaName,
				new JObject {
					["Name"] = ClassicEntitySchemaQuery.Column("Name"),
					["UId"] = ClassicEntitySchemaQuery.Column("UId"),
					["ExtendParent"] = ClassicEntitySchemaQuery.Column("ExtendParent")
				},
				ClassicEntitySchemaQuery.Group(
					("byName", ClassicEntitySchemaQuery.InFilter("Name", chunk, TextDataValueType)),
					("byManager", ClassicEntitySchemaQuery.Eq("ManagerName", EntitySchemaManagerName, TextDataValueType))),
				requestedRows);
			JArray rows = ClassicEntitySchemaQuery.Select(client, urlBuilder, query);
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
		IToolCommandResolver commandResolver, EnvironmentOptions options,
		string entitySchemaUId, Guid packageUId) {
		if (!Guid.TryParse(entitySchemaUId, out Guid entityUId)) {
			return ActionTargetState.Unknown;
		}
		try {
			IAddonSchemaDesignerClient addonClient = commandResolver.Resolve<IAddonSchemaDesignerClient>(options);
			AddonSchemaDto schema = addonClient.GetSchema(new AddonGetRequestDto {
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
	/// The mobile and web page-template root names used to classify a page by its parent. Best-effort on the
	/// environment tier: when the catalog cannot be read the bundled and rules-declared roots still place every
	/// OOTB shape, and anything they cannot place escalates to the authoritative read instead.
	/// </summary>
	private static (HashSet<string> Mobile, HashSet<string> Web) LoadTemplateRoots(
		IToolCommandResolver commandResolver, EnvironmentOptions options,
		WebToMobilePageConversionRules rules) {
		var mobile = new HashSet<string>(BundledMobileTemplateRoots, StringComparer.OrdinalIgnoreCase);
		var web = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (TemplateMappingRule rule in rules?.Templates ?? []) {
			if (!string.IsNullOrWhiteSpace(rule?.Mobile)) {
				mobile.Add(rule.Mobile);
			}
			if (!string.IsNullOrWhiteSpace(rule?.Web)) {
				web.Add(rule.Web);
			}
		}
		try {
			ISchemaTemplateCatalog catalog = commandResolver.Resolve<ISchemaTemplateCatalog>(options);
			AddTemplateNames(catalog.GetTemplates(PageSchemaType.Mobile), mobile);
			AddTemplateNames(catalog.GetTemplates(PageSchemaType.Web), web);
		} catch (Exception) {
			// Best-effort: classification continues on the bundled + rules-declared roots.
		}
		// A name known as a MOBILE root must never also disqualify a page as web-only.
		web.ExceptWith(mobile);
		return (mobile, web);
	}

	private static void AddTemplateNames(IReadOnlyList<PageTemplateInfo> templates, HashSet<string> into) {
		foreach (PageTemplateInfo template in templates ?? []) {
			if (!string.IsNullOrWhiteSpace(template?.Name)) {
				into.Add(template.Name);
			}
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

	/// <summary>A degraded result: the occurrences are known, nothing about their targets is.</summary>
	private static MobileActionTargetProbeResult NotProbed(
		IReadOnlyList<ActionTargetOccurrence> occurrences, string note) =>
		new() { ProbeOk = false, Note = note, Occurrences = occurrences };

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
	/// default — a disagreement that lands on the delete-the-control side.
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
