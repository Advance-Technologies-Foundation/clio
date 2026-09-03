using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Detects a page's source type and returns an advisory mobile-conversion GUIDE (ENG-89620).
/// Advisory-only: it reads the source page, classifies its components, and produces a deterministic
/// guide (recommended mobile template + container correspondence, source component structure,
/// per-type component suggestions from the WebToMobilePageConversionRules matrix + registry
/// comparison, and inline mobile component contracts). It builds NO page body and writes NOTHING to
/// Creatio or disk — the caller (LLM) builds the mobile page body itself using create-page +
/// update-page + validate-page.
/// Supported source type today: Freedom UI web (<c>freedom-web</c>). Other source types (e.g. Classic
/// UI) are detected and reported as not yet supported.
/// </summary>
[McpServerToolType]
[FeatureToggle("mobile-page-converter")]
[SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null", Justification = "The best-effort probe helpers return null to signal 'not read' (distinct from 'read, empty'); the caller treats null as skip.")]
[SuppressMessage("Minor Code Smell", "S3267:Loops should be simplified with LINQ", Justification = "Explicit loops that build registry maps with side effects read more clearly than a LINQ rewrite here.")]
public sealed class MobilePageConversionGuideTool {
	private readonly IToolCommandResolver _commandResolver;
	private readonly ILogger _logger;
	private readonly IMobileComponentInfoCatalog _mobileCatalog;
	private readonly IComponentInfoCatalog _webCatalog;
	private readonly IWebToMobilePageConversionRulesCatalog _rulesCatalog;
	private readonly IPlatformVersionResolverFactory _versionResolverFactory;
	private readonly ISettingsRepository _settingsRepository;

	public MobilePageConversionGuideTool(
		IToolCommandResolver commandResolver,
		ILogger logger,
		IMobileComponentInfoCatalog mobileCatalog,
		IComponentInfoCatalog webCatalog,
		IWebToMobilePageConversionRulesCatalog rulesCatalog,
		IPlatformVersionResolverFactory versionResolverFactory,
		ISettingsRepository settingsRepository) {
		_commandResolver = commandResolver;
		_logger = logger;
		_mobileCatalog = mobileCatalog;
		_webCatalog = webCatalog;
		_rulesCatalog = rulesCatalog;
		_versionResolverFactory = versionResolverFactory;
		_settingsRepository = settingsRepository;
	}

	internal const string ToolName = "get-mobile-page-conversion-guide";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Detect a page's source type and return an advisory mobile-conversion GUIDE. Supported source type today: "
		+ "Freedom UI WEB (sourceType \"freedom-web\"); any other source type is detected and reported as not yet "
		+ "supported. ADVISORY-ONLY: this tool builds NO page body and writes NOTHING to Creatio or disk — YOU build "
		+ "the mobile body from the guide, persist it with create-page (mobile template) + update-page, then "
		+ "validate-page. The guide carries FACTS about this page and no prose: the rules are enforced by "
		+ "validate-page / update-page, and the ordered flow plus every standing rule live in the guidance "
		+ "article. It FAILS rather than degrading when the mobile template cannot be read, because without it "
		+ "the guide would insert duplicates of elements that template already provides. "
		+ "MANDATORY before acting on the guide: get-guidance name `freedom-page-web-to-mobile-conversion`.")]
	public async Task<MobilePageConversionGuideResponse> GetMobilePageConversionGuide(
		[Description("Parameters: schema-name (required, the source page); target-schema-name (optional suggested mobile page name); version (optional registry/Creatio version); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] MobilePageConversionGuideArgs args,
		CancellationToken cancellationToken = default) {

		PageGetOptions getOptions = new() {
			SchemaName = args.SchemaName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		PageGetResponse pageResponse;
		try {
			PageGetCommand getCommand = _commandResolver.Resolve<PageGetCommand>(getOptions);
			lock (McpToolExecutionLock.GetLock(McpToolExecutionLock.SharedFallbackKey)) {
				try {
					getCommand.TryGetPage(getOptions, out pageResponse);
				} finally {
					_logger.ClearMessages();
				}
			}
		} catch (Exception ex) {
			return Fail(args, null, $"Failed to read source page '{args.SchemaName}': {ex.Message}");
		}

		if (pageResponse is null || !pageResponse.Success) {
			return Fail(args, null,
				$"Could not read source page '{args.SchemaName}': {pageResponse?.Error ?? "unknown error"}. " +
				"If the page is a Classic UI page, migrate it to a Freedom UI web page first.");
		}

		// Detect the source page type and gate on it. Only Freedom UI web is supported today; a
		// non-Freedom-web source (e.g. Classic UI) or an already-mobile page short-circuits with a
		// failure and never starts conversion (hard acceptance criterion).
		string sourceType = DetectSourceType(pageResponse.Page?.SchemaType);
		MobilePageConversionGuideResponse sourceTypeRejection = RejectUnsupportedSourceType(args, sourceType);
		if (sourceTypeRejection is not null) {
			return sourceTypeRejection;
		}

		// Validate an explicit version BEFORE it reaches CDN URL composition, mirroring get-component-info.
		// The raw value flows into BuildCdnUrl's relative-Uri composition, so an unvalidated value like
		// "//host/x" would be an RFC 3986 network-path reference and redirect the fetch to another host.
		// Accept a 3-part semver or the literal "latest"; reject anything else up front.
		if (!string.IsNullOrWhiteSpace(args.Version)
			&& !string.Equals(args.Version.Trim(), ComponentRegistryClient.LatestVersion, StringComparison.OrdinalIgnoreCase)
			&& !PlatformVersionResolver.TryNormaliseToThreePartSemver(args.Version, out _)) {
			return Fail(args, sourceType,
				$"'version' value '{args.Version}' is not a valid platform version. Use a 3-part semver, for example '8.3.3', or 'latest'.");
		}

		// Resolve the component-registry version against the TARGET environment (mirrors get-component-info):
		// explicit version wins; else probe the environment's platform version; else degrade to "latest" and
		// flag it so the caller confirms with the user (a "latest" superset may list components absent from the
		// target Creatio version). AC: use only mobile components available in the target Creatio version.
		PlatformVersionResolution versionResolution =
			await ResolveVersionAsync(args, cancellationToken).ConfigureAwait(false);
		string version = versionResolution.ResolvedVersion;
		// Load both catalogs via LoadAsync so the version the CDN chain ACTUALLY served
		// (state.ResolvedVersion) is captured — GetAllAsync discards it. Compute resolvedFrom from the served
		// version (mirrors get-component-info): when the environment resolves e.g. 8.2.1 but the chain falls
		// back to "latest"/bundled, this reports environment-superset (+ versionWarning) instead of the
		// false "environment" (exact). When the mobile and web catalogs land on different tiers, report the
		// worse one so a superset/fallback on either side is never hidden.
		ComponentCatalogState mobileState =
			await _mobileCatalog.LoadAsync(version, cancellationToken).ConfigureAwait(false);
		ComponentCatalogState webState =
			await _webCatalog.LoadAsync(version, cancellationToken).ConfigureAwait(false);
		string resolvedFrom = WorseResolvedFrom(
			ComponentInfoResolution.MapResolvedFrom(versionResolution.Source, versionResolution.ResolvedVersion, mobileState.ResolvedVersion),
			ComponentInfoResolution.MapResolvedFrom(versionResolution.Source, versionResolution.ResolvedVersion, webState.ResolvedVersion));
		IReadOnlyList<ComponentRegistryEntry> mobileEntries = mobileState.Entries;
		IReadOnlyList<ComponentRegistryEntry> webEntries = webState.Entries;
		HashSet<string> mobileTypes = new(mobileEntries.Select(e => e.ComponentType), StringComparer.OrdinalIgnoreCase);
		HashSet<string> webTypes = new(webEntries.Select(e => e.ComponentType), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, ComponentRegistryEntry> mobileByType = new(StringComparer.OrdinalIgnoreCase);
		foreach (ComponentRegistryEntry entry in mobileEntries) {
			if (!string.IsNullOrWhiteSpace(entry.ComponentType)) {
				mobileByType[entry.ComponentType] = entry;
			}
		}
		Dictionary<string, ComponentRegistryEntry> webByType = new(StringComparer.OrdinalIgnoreCase);
		foreach (ComponentRegistryEntry entry in webEntries) {
			if (!string.IsNullOrWhiteSpace(entry.ComponentType)) {
				webByType[entry.ComponentType] = entry;
			}
		}

		WebToMobilePageConversionRules rules = await _rulesCatalog.GetRulesAsync(version, cancellationToken).ConfigureAwait(false);
		// Resolve the effective web template, climbing past same-named replacing layers when the page is a
		// replacing schema over a same-named base (parentSchemaName == schemaName). Feeds template-rule
		// resolution, chrome subtraction, and the reported sourceTemplate — all from one value.
		string effectiveTemplate = ResolveEffectiveTemplateName(pageResponse.Page, pageResponse.Bundle, rules);
		// The default is applied HERE and deliberately not inside ResolveTemplateRule: that method is also the
		// predicate ResolveEffectiveTemplateName uses to find the first ancestor that MATCHES a rule, and a
		// never-null result would make every ancestor match and collapse that climb.
		TemplateMappingRule templateRule =
			ResolveTemplateRule(rules, effectiveTemplate) ?? DefaultTemplateRule(rules);
		IReadOnlyDictionary<string, string> containerNameMap = BuildContainerNameMap(templateRule);
		IReadOnlyDictionary<string, ComponentMappingRule> componentNameMap = BuildComponentNameMap(templateRule);
		IReadOnlyList<WebToMobileAnalysisService.PositionalPlacement> positionalPlacements = BuildPositionalPlacements(templateRule);

		// Best-effort read of the mobile template's own bundle. Used for three independent probes: the
		// positional-insert container-parent map (read UNCONDITIONALLY: it also tells the adaptive pass where
		// the mobile template nests a container twin, and only PageWithTabsFreedomTemplate declares positional
		// entries — gating on them left twin placement dead for every other template family),
		// every array anywhere in the template's own merged viewModelConfig (filterAttributes, sortingConfig,
		// or any other array — generic), and the template's own list-collection keys — all fetched
		// unconditionally whenever a mobile template is known, so the page's own arrays can be UNIONED with
		// the template's natives and each template-owned collection can be split into focused targeted merges
		// instead of the mobile diff engine's array-replace root merge silently dropping one side (see
		// WebToMobileAnalysisService.SplitRootMergeIntoTargetedMerges).
		MobileTemplateProbe mobileTemplateProbe = LoadMobileTemplateProbe(templateRule?.Mobile, args);
		MobilePageConversionGuideResponse templateRejection =
			RejectUnobtainableMobileTemplate(args, sourceType, templateRule?.Mobile, mobileTemplateProbe.Unavailable);
		if (templateRejection is not null) {
			return templateRejection;
		}
		IReadOnlyDictionary<string, string> mobileContainerParents = mobileTemplateProbe.ContainerParents;

		// Read the source page's web template (its parent schema) so its inherited chrome can be
		// filtered out of the conversion: the merged page tree carries the template's header/scaffold
		// containers, which the mobile template already provides. A page with NO parent template is fine and
		// yields an empty baseline; a template that WAS named and could not be read is not — see
		// RejectUnobtainableWebTemplate for the two silent defects that state produces.
		WebTemplateBaseline webTemplateBaseline = LoadWebTemplateBaseline(
			effectiveTemplate, pageResponse.Page?.SchemaName, args);
		MobilePageConversionGuideResponse webTemplateRejection =
			RejectUnobtainableWebTemplate(args, sourceType, effectiveTemplate, webTemplateBaseline.Unavailable);
		if (webTemplateRejection is not null) {
			return webTemplateRejection;
		}

		string targetName = string.IsNullOrWhiteSpace(args.TargetSchemaName)
			? DeriveMobileSchemaName(args.SchemaName)
			: args.TargetSchemaName.Trim();

		// Read-only probe: is this page a section, and what would registering it for mobile take?
		// Best-effort — never blocks the guide if the environment can't be queried.
		bool isFormPage = IsFormPage(args.SchemaName, pageResponse.Page?.ParentSchemaName);
		SectionRegistrationInfo sectionRegistration = MobileSectionRegistrationProbe.Probe(
			_commandResolver, args.EnvironmentName, args.Uri, args.Login, args.Password,
			pageResponse.Page?.SchemaUId, isFormPage);

		// Read-only probe: the source page's PAGE-level business rules (stored as add-on metadata,
		// not in the page body). Best-effort — never blocks the guide.
		PageBusinessRuleProbeResult pageBusinessRules = PageBusinessRuleProbe.Probe(
			_commandResolver, args.EnvironmentName, args.Uri, args.Login, args.Password,
			args.SchemaName, pageResponse.Page?.PackageUId);

		MobilePageConversionGuide guide;
		try {
			guide = WebToMobileAnalysisService.Analyze(
				pageResponse.Bundle ?? new PageBundleInfo(),
				mobileTypes, webTypes, webByType, mobileByType, rules, templateRule,
				sourcePage: args.SchemaName,
				sourceTemplate: effectiveTemplate,
				suggestedTarget: targetName,
				containerNameMap: containerNameMap,
				sectionRegistration: sectionRegistration,
				pageBusinessRulesProbe: pageBusinessRules,
				templateComponentNames: webTemplateBaseline.Names,
				componentNameMap: componentNameMap,
				positionalPlacements: positionalPlacements,
				mobileContainerParents: mobileContainerParents,
				mobileTemplateViewModelConfig: mobileTemplateProbe.ViewModelConfig,
				mobileTemplateModelConfig: mobileTemplateProbe.ModelConfig,
				mobileTemplateTypesByName: mobileTemplateProbe.TypesByName,
				mobileTemplateLayoutConfigs: mobileTemplateProbe.LayoutConfigsByName,
				webTemplateBaselineNodes: webTemplateBaseline.Nodes,
				webTemplateResources: webTemplateBaseline.Resources);
		} catch (Exception ex) {
			return Fail(args, sourceType, $"Failed to analyze source page '{args.SchemaName}': {ex.Message}");
		}

		return new MobilePageConversionGuideResponse {
			Success = true,
			SourceSchemaName = args.SchemaName,
			SourceType = sourceType,
			Guide = guide,
			ResolvedTargetVersion = version,
			ResolvedFrom = resolvedFrom,
			VersionWarning = ComponentInfoResolution.GetVersionWarning(resolvedFrom),
			RequiresVersionConfirmation = ComponentInfoResolution.RequiresVersionConfirmation(resolvedFrom),
			ResolvedFromReason = ComponentInfoResolution.GetFallbackReason(resolvedFrom, versionResolution.Reason)
		};
	}

	/// <summary>
	/// Resolves the component-registry version from the per-call arguments, mirroring get-component-info so
	/// the guide is scoped to the target Creatio version: an explicit <c>version</c> is authoritative; an
	/// <c>environment-name</c>/<c>uri</c> is probed for its platform version; neither degrades to <c>latest</c>
	/// on the fallback tier (surfaced as <c>latest-fallback</c> so the caller confirms with the user).
	/// </summary>
	private async Task<PlatformVersionResolution> ResolveVersionAsync(
		MobilePageConversionGuideArgs args, CancellationToken cancellationToken) {
		if (!string.IsNullOrWhiteSpace(args.Version)) {
			return new PlatformVersionResolution(args.Version.Trim(), VersionResolutionSource.Environment);
		}
		if (!string.IsNullOrWhiteSpace(args.EnvironmentName) || !string.IsNullOrWhiteSpace(args.Uri)) {
			EnvironmentSettings settings = _settingsRepository.GetEnvironment(new EnvironmentOptions {
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			});
			using IOwnedPlatformVersionResolver resolver = _versionResolverFactory.CreateOwned(settings);
			return await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
		}
		return ComponentInfoResolution.CreateNoActiveEnvironmentFallback();
	}

	/// <summary>
	/// The web template baseline: a name → node map of every component the source page's web template
	/// provides (inherited chrome), with <see cref="Names"/> derived from its keys for chrome subtraction. The
	/// node map is the DELTA baseline — a same-component twin carries only the properties the page changed from
	/// it, so an untouched inherited property leaves the mobile template's own default in place.
	/// <see cref="Unavailable"/> is true ONLY when a template name was known but its bundle could not be read
	/// (no active environment, read failure) — distinct from "the page has no web template" (both parents
	/// empty); the caller surfaces it so a same-component twin's fallback to carrying the whole node is not
	/// silent.
	/// </summary>
	private sealed record WebTemplateBaseline(
		IReadOnlySet<string> Names,
		IReadOnlyDictionary<string, JObject> Nodes,
		bool Unavailable,
		JObject Resources);

	/// <summary>
	/// Best-effort read of the source page's web template (its parent schema, e.g. PageWithTabsFreedomTemplate)
	/// so its inherited chrome can be filtered out of the conversion and used as the same-component-twin delta
	/// baseline. Loads the template's merged bundle the same way the source page is loaded. Returns an empty
	/// baseline (Unavailable=false) when there is no parent template; Unavailable=true when a template was
	/// known but the read failed. Never throws.
	/// </summary>
	private WebTemplateBaseline LoadWebTemplateBaseline(string parentSchemaName, string ownSchemaName, MobilePageConversionGuideArgs args) {
		var emptyNodes = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
		var absent = new WebTemplateBaseline(
			new HashSet<string>(StringComparer.OrdinalIgnoreCase), emptyNodes, Unavailable: false, Resources: null);
		if (string.IsNullOrWhiteSpace(parentSchemaName)) {
			return absent;
		}
		// Never load the source page as its own template baseline: for a replacing schema layered over a
		// same-named base, the parent name equals the page's own name. Subtracting the page against itself
		// would empty the whole layout. Belt-and-suspenders behind ResolveEffectiveTemplateName.
		if (string.Equals(parentSchemaName, ownSchemaName, StringComparison.OrdinalIgnoreCase)) {
			return absent;
		}
		try {
			PageGetOptions options = new() {
				SchemaName = parentSchemaName,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			PageGetResponse templateResponse;
			PageGetCommand command = _commandResolver.Resolve<PageGetCommand>(options);
			lock (McpToolExecutionLock.GetLock(McpToolExecutionLock.SharedFallbackKey)) {
				try {
					command.TryGetPage(options, out templateResponse);
				} finally {
					_logger.ClearMessages();
				}
			}
			if (templateResponse?.Success == true && templateResponse.Bundle?.ViewConfig is { } viewConfig) {
				// One traversal: derive Names from the node map's keys rather than walking the tree twice.
				IReadOnlyDictionary<string, JObject> nodes =
					WebToMobileAnalysisService.CollectComponentNodesByName(viewConfig);
				// The template's own resource strings — the delta baseline for a twin's caption VALUE (a rename
				// keeps the same token, so the resolved text is what distinguishes it from the inherited label).
				JObject resources = templateResponse.Bundle.Resources?.Strings is { } strings
					? JObject.Parse(strings.ToJsonString())
					: null;
				return new WebTemplateBaseline(
					new HashSet<string>(nodes.Keys, StringComparer.OrdinalIgnoreCase), nodes, Unavailable: false, Resources: resources);
			}
		} catch (Exception) {
			// Best-effort: fall through to Unavailable below.
		}
		// A template name was known but the bundle could not be read — flag it so the caller does not treat the
		// missing baseline as "the page changed everything" without a signal.
		return new WebTemplateBaseline(
			new HashSet<string>(StringComparer.OrdinalIgnoreCase), emptyNodes, Unavailable: true, Resources: null);
	}

	/// <summary>
	/// Maps the platform schema-type of the source page to a conversion source-type label.
	/// Freedom UI web pages report schema-type "web"; mobile pages report "mobile"; anything else
	/// (e.g. a Classic UI page) is surfaced verbatim as a not-yet-supported source type.
	/// </summary>
	internal static string DetectSourceType(string schemaType) {
		if (string.Equals(schemaType, "web", StringComparison.OrdinalIgnoreCase)) {
			return WebToMobileAnalysisService.SourceTypeFreedomWeb;
		}
		if (string.Equals(schemaType, "mobile", StringComparison.OrdinalIgnoreCase)) {
			return "mobile";
		}
		return string.IsNullOrWhiteSpace(schemaType) ? "unknown" : schemaType.Trim().ToLowerInvariant();
	}

	/// <summary>
	/// The fallback rule for a web template no <see cref="WebToMobilePageConversionRules.Templates"/> entry
	/// matches: a generic mobile base from <see cref="WebToMobilePageConversionRules.DefaultMobileTemplate"/>,
	/// with NO container or component correspondence. Null when the rules declare no default.
	/// </summary>
	/// <remarks>
	/// It carries only the mobile schema name on purpose. A recommendation lets the caller create the page and
	/// gives clio a template bundle to diff the data sections against — without one, both diffs degrade to a
	/// root merge (ENG-95827). Name twins, by contrast, cannot be guessed for an unrecognised web template:
	/// asserting them would relocate elements, which is worse than leaving them where the tree walk puts them.
	/// The note says the recommendation is generic so the caller does not read it as a matched pair.
	/// </remarks>
	internal static TemplateMappingRule DefaultTemplateRule(WebToMobilePageConversionRules rules) =>
		string.IsNullOrWhiteSpace(rules?.DefaultMobileTemplate)
			? null
			: new TemplateMappingRule {
				Mobile = rules.DefaultMobileTemplate,
				Note = "No conversion rule matches this page's web template, so this is the generic mobile base "
					+ "rather than a matched counterpart: no container or component name correspondence is known, "
					+ "and every element is placed where the source tree puts it. Review the result in the "
					+ "designer, and consider adding a templates entry for this web template."
			};

	/// <summary>
	/// Returns the template mapping rule for a web page whose parent template is
	/// <paramref name="webParentTemplate"/>. When several rules share the same web template, the
	/// first one wins (the rules file lists the preferred mobile target first). Null when no rule matches.
	/// </summary>
	/// <remarks>
	/// Deliberately returns null rather than falling back to <see cref="DefaultTemplateRule"/>: this method is
	/// also the predicate <c>ResolveEffectiveTemplateName</c> uses to find the first ANCESTOR matching a rule,
	/// and a never-null result would make every ancestor match and collapse that climb. The default is applied
	/// at the call site instead (ENG-95827).
	/// </remarks>
	internal static TemplateMappingRule ResolveTemplateRule(WebToMobilePageConversionRules rules, string webParentTemplate) {
		if (rules?.Templates is null || string.IsNullOrWhiteSpace(webParentTemplate)) {
			return null;
		}
		foreach (TemplateMappingRule rule in rules.Templates) {
			if (string.Equals(rule?.Web, webParentTemplate, StringComparison.OrdinalIgnoreCase)) {
				return rule;
			}
		}
		return null;
	}

	/// <summary>
	/// Resolves the effective web template of the source page — the schema whose chrome must be subtracted
	/// and whose mobile counterpart is recommended. Normally this is the page's direct parent
	/// (<c>ParentSchemaName</c>). For a REPLACING schema layered over a same-named base, the direct parent
	/// equals the page's own name (Creatio keeps the same <c>Name</c> across a replacement stack); trusting it
	/// would load the page as its own template baseline and subtract the whole layout against itself. In that
	/// case (or when the parent is missing) this climbs the inheritance chain (<see cref="PageBundleInfo.Schemas"/>,
	/// ordered HEAD→ROOT), skips every same-named replacing layer, and returns the first ancestor that matches a
	/// known template rule (e.g. <c>PageWithTabsFreedomTemplate</c>) — falling back to the first differently-named
	/// ancestor, then to the raw parent name. Pages whose parent already differs from their own name are returned
	/// verbatim, so non-replacing pages behave exactly as before.
	/// </summary>
	internal static string ResolveEffectiveTemplateName(
		PageMetadataInfo page, PageBundleInfo bundle, WebToMobilePageConversionRules rules) {
		string own = page?.SchemaName;
		string parent = page?.ParentSchemaName;
		// Fast path: a normal (non-replacing) page — the parent is a distinct template/base. Unchanged behavior.
		if (!string.IsNullOrWhiteSpace(parent)
			&& !string.Equals(parent, own, StringComparison.OrdinalIgnoreCase)) {
			return parent;
		}
		// Replacing / self-referential (or missing parent): climb the chain past same-named layers.
		if (bundle?.Schemas is { Count: > 0 }) {
			string firstDistinct = null;
			foreach (PageSchemaChainEntry entry in bundle.Schemas) {
				string name = entry?.SchemaName;
				if (string.IsNullOrWhiteSpace(name)
					|| string.Equals(name, own, StringComparison.OrdinalIgnoreCase)) {
					continue;
				}
				firstDistinct ??= name;
				if (ResolveTemplateRule(rules, name) is not null) {
					return name;
				}
			}
			if (firstDistinct is not null) {
				return firstDistinct;
			}
		}
		return parent;
	}

	/// <summary>
	/// Builds a web→mobile container-name map from the matched template rule's container correspondence.
	/// Returns null when there is no rule or no container entries.
	/// </summary>
	internal static IReadOnlyDictionary<string, string> BuildContainerNameMap(TemplateMappingRule rule) {
		if (rule?.Containers is null || rule.Containers.Count == 0) {
			return null;
		}
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (ContainerMappingRule c in rule.Containers) {
			// Skip positional entries (`<anchor>:top` / `:bottom`) — they are not element-name twins; they
			// are parsed separately by BuildPositionalPlacements.
			if (!string.IsNullOrWhiteSpace(c?.Web) && !string.IsNullOrWhiteSpace(c.Mobile)
				&& !c.Web.Contains(':') && !c.Mobile.Contains(':')) {
				map[c.Web] = c.Mobile;
			}
		}
		return map.Count > 0 ? map : null;
	}

	/// <summary>
	/// Parses the positional container entries of a template rule. A positional entry has the form
	/// <c>{ "web": "&lt;anchor&gt;:top|bottom", "mobile": "&lt;mobileAnchor&gt;:top|bottom" }</c>: content
	/// that is a sibling of the web <c>&lt;anchor&gt;</c> container is placed above/below the mobile
	/// <c>&lt;mobileAnchor&gt;</c> (in that anchor's parent container). Both the <c>:top</c> and <c>:bottom</c>
	/// entries of an anchor resolve to the same placement (the side is inferred from sibling order), so the
	/// result is deduplicated by web anchor. Returns null when the rule declares no positional entries.
	/// </summary>
	internal static IReadOnlyList<WebToMobileAnalysisService.PositionalPlacement> BuildPositionalPlacements(TemplateMappingRule rule) {
		if (rule?.Containers is null || rule.Containers.Count == 0) {
			return null;
		}
		var byAnchor = new Dictionary<string, WebToMobileAnalysisService.PositionalPlacement>(StringComparer.OrdinalIgnoreCase);
		foreach (ContainerMappingRule c in rule.Containers) {
			if (string.IsNullOrWhiteSpace(c?.Web) || string.IsNullOrWhiteSpace(c.Mobile)
				|| !c.Web.Contains(':') || !c.Mobile.Contains(':')) {
				continue;
			}
			string webAnchor = c.Web.Split(':', 2)[0].Trim();
			string mobileAnchor = c.Mobile.Split(':', 2)[0].Trim();
			if (webAnchor.Length == 0 || mobileAnchor.Length == 0 || byAnchor.ContainsKey(webAnchor)) {
				continue;
			}
			byAnchor[webAnchor] = new WebToMobileAnalysisService.PositionalPlacement(webAnchor, mobileAnchor);
		}
		return byAnchor.Count > 0 ? byAnchor.Values.ToList() : null;
	}

	/// <summary>
	/// Result of <see cref="LoadMobileTemplateProbe"/>: the mobile template's container-parent map
	/// (positional inserts) plus the template's OWN merged <c>viewModelConfig</c> and <c>modelConfig</c> base,
	/// which the converted page's configs are diffed against recursively (a shared subtree emits only the real
	/// delta; an array the base already carries is augmented via insert rather than replaced). <c>Unavailable</c>
	/// is true when a template schema name was known but its bundle could not be read (no active
	/// environment, read failure) — the caller surfaces that as an explicit guide constraint instead of
	/// silently falling back to a single root merge that may replace the template's arrays wholesale.
	/// </summary>
	private sealed record MobileTemplateProbe(
		IReadOnlyDictionary<string, string> ContainerParents,
		IReadOnlyDictionary<string, JsonObject> LayoutConfigsByName,
		JsonNode ViewModelConfig,
		JsonNode ModelConfig,
		bool Unavailable,
		IReadOnlyDictionary<string, string> TypesByName);

	/// <summary>
	/// Best-effort read of the mobile template (<paramref name="mobileSchemaName"/>) bundle: maps each mobile
	/// container to its parent — resolving where a positional (<c>:top</c> / <c>:bottom</c>) insert attaches —
	/// and carries the template's OWN merged <c>viewModelConfig</c> and <c>modelConfig</c> so the converted
	/// page's configs can be diffed recursively against that base (a shared subtree emits only the real delta;
	/// an array the base already carries is augmented via insert rather than replaced by the mobile diff
	/// engine's array-replace merge). Mirrors <see cref="LoadWebTemplateBaseline"/>: loads the template's
	/// merged bundle and never throws. Returns a null base (and <c>Unavailable = false</c>) when no template
	/// name is known; <c>Unavailable = true</c> when a name was known but the read failed.
	/// </summary>
	private MobileTemplateProbe LoadMobileTemplateProbe(string mobileSchemaName, MobilePageConversionGuideArgs args) {
		var emptyParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var emptyTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var emptyPlacements = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(mobileSchemaName)) {
			// Unavailable, not "fine": there is no template to read, so there is no base to diff the data
			// sections against and both diffs degrade to a root merge. Reporting this as available made that
			// degradation indistinguishable from the benign case where a template WAS read and simply carries
			// no such config section — the one case that must NOT be reported, because a root merge over a
			// base that owns nothing loses nothing (ENG-95827). Reachable only when the rules declare no
			// defaultMobileTemplate either.
			return new MobileTemplateProbe(emptyParents, emptyPlacements, ViewModelConfig: null, ModelConfig: null,
				Unavailable: true, TypesByName: emptyTypes);
		}
		try {
			PageGetOptions options = new() {
				SchemaName = mobileSchemaName,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			PageGetResponse templateResponse;
			PageGetCommand command = _commandResolver.Resolve<PageGetCommand>(options);
			lock (McpToolExecutionLock.GetLock(McpToolExecutionLock.SharedFallbackKey)) {
				try {
					command.TryGetPage(options, out templateResponse);
				} finally {
					_logger.ClearMessages();
				}
			}
			if (templateResponse?.Success == true && templateResponse.Bundle is { } bundle) {
				IReadOnlyDictionary<string, string> parents = emptyParents;
				IReadOnlyDictionary<string, string> types = emptyTypes;
				IReadOnlyDictionary<string, JsonObject> placements = emptyPlacements;
				if (bundle.ViewConfig is { } viewConfig) {
					parents = WebToMobileAnalysisService.CollectParentByName(viewConfig);
					types = WebToMobileAnalysisService.CollectComponentTypesByName(viewConfig);
					placements = WebToMobileAnalysisService.CollectLayoutConfigByName(viewConfig);
				}
				return new MobileTemplateProbe(parents, placements, bundle.ViewModelConfig, bundle.ModelConfig,
					Unavailable: false, TypesByName: types);
			}
		} catch (Exception) {
			// Best-effort: a failed mobile-template read falls back to defaults; Unavailable flags it below.
		}
		return new MobileTemplateProbe(emptyParents, emptyPlacements, ViewModelConfig: null, ModelConfig: null,
			Unavailable: true, TypesByName: emptyTypes);
	}

	/// <summary>
	/// Builds a web-element-name → mapping-rule dictionary from the matched template rule's component
	/// correspondence (analogous to <see cref="BuildContainerNameMap"/>, but for content components such
	/// as the list template's grid). A mapped element is kept through template-chrome subtraction and
	/// converted by merge-by-name. Returns null when there is no rule or no component entries.
	/// </summary>
	internal static IReadOnlyDictionary<string, ComponentMappingRule> BuildComponentNameMap(TemplateMappingRule rule) {
		if (rule?.Components is null || rule.Components.Count == 0) {
			return null;
		}
		var map = new Dictionary<string, ComponentMappingRule>(StringComparer.OrdinalIgnoreCase);
		foreach (ComponentMappingRule c in rule.Components) {
			if (!string.IsNullOrWhiteSpace(c?.Web) && !string.IsNullOrWhiteSpace(c.Mobile)) {
				map[c.Web] = c;
			}
		}
		return map.Count > 0 ? map : null;
	}

	/// <summary>
	/// Best-effort guess of whether the source page is an edit/form page (vs a list/section page),
	/// from the schema-name suffix or its parent template. Used only to tailor the read-only section
	/// registration advice (the default mobile edit page is a manual step).
	/// </summary>
	internal static bool IsFormPage(string schemaName, string parentTemplate) {
		if (!string.IsNullOrWhiteSpace(schemaName) && schemaName.EndsWith("FormPage", StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		return parentTemplate is "PageWithTabsFreedomTemplate" or "BasePageFreedomTemplate" or "BasePageTemplate";
	}

	internal static string DeriveMobileSchemaName(string webSchemaName) {
		if (string.IsNullOrWhiteSpace(webSchemaName)) {
			return "Mobile_Page";
		}
		if (webSchemaName.EndsWith("_FormPage", StringComparison.Ordinal)) {
			return webSchemaName[..^"_FormPage".Length] + "_MobileFormPage";
		}
		if (webSchemaName.EndsWith("_ListPage", StringComparison.Ordinal)) {
			return webSchemaName[..^"_ListPage".Length] + "_MobileListPage";
		}
		return webSchemaName + "_Mobile";
	}

	/// <summary>
	/// Refuses the conversion when the mobile template could not be obtained. Null in the normal case.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The guide's quality DEPENDS on reading that template, and the degradation is not confined to one
	/// section, which is why this is a top-level failure rather than a diagnostic on an otherwise-normal
	/// guide. With no template bundle, <c>MobileTypesByName</c> is empty, and that breaks two things
	/// silently:
	/// </para>
	/// <list type="bullet">
	/// <item><description>The automatic same-name twin is never detected (its gate is
	/// <c>MobileTypesByName.TryGetValue(name, …)</c>), so an element the mobile template ALREADY provides
	/// under the same name — <c>Feed</c>, <c>Tabs</c> — falls through to the insert path and the page ships a
	/// DUPLICATE of a native element.</description></item>
	/// <item><description><c>RetargetTargetMissing</c> fails open on an empty map, so a retarget into a
	/// container the template does not actually have is no longer caught.</description></item>
	/// </list>
	/// <para>
	/// The data-section diffs also degrade to a root merge that can strip the template's own arrays. That one
	/// used to be reported as a <c>data-section-root-merge-fallback</c> diagnostic and nothing reported the
	/// other two — so the guide shipped with silent correctness defects and a footnote about the least of
	/// them (ENG-95827). A guide that cannot be trusted is worse than no guide: refusing names the cause and
	/// the fix, which a diagnostic buried beside 155 element entries did not.
	/// </para>
	/// </remarks>
	internal static MobilePageConversionGuideResponse RejectUnobtainableMobileTemplate(
		MobilePageConversionGuideArgs args, string sourceType, string mobileTemplateName,
		bool templateUnavailable) {
		if (!templateUnavailable) {
			return null;
		}
		// Two causes, two different fixes. Naming which one applies is the whole value of failing here.
		string error = string.IsNullOrWhiteSpace(mobileTemplateName)
			? "No mobile template could be determined for this page: its web template matches no conversion "
				+ "rule and the conversion rules declare no defaultMobileTemplate. Without a mobile template "
				+ "the guide cannot detect which elements the template already provides, so it would insert "
				+ "duplicates of native elements, and both data-section diffs would degrade to a root merge "
				+ "that can strip the template's own arrays. Add a templates entry for this web template, or a "
				+ "defaultMobileTemplate, to the conversion rules."
			: $"Could not read the mobile template '{mobileTemplateName}'. Without it the guide cannot detect "
				+ "which elements that template already provides, so it would insert DUPLICATES of native "
				+ "elements (e.g. Feed, Tabs) instead of merging onto them, retarget targets would not be "
				+ "validated, and both data-section diffs would degrade to a root merge that can strip the "
				+ "template's own arrays. Verify the mobile package is installed in the target environment and "
				+ "that the schema name is reachable, then re-run.";
		return Fail(args, sourceType, error);
	}

	/// <summary>
	/// Refuses the conversion when the source page's WEB template was known but could not be read. Returns
	/// null when there is nothing to refuse — including the ordinary case of a page with no parent template at
	/// all, which yields an empty baseline legitimately.
	/// </summary>
	/// <remarks>
	/// The mirror of <see cref="RejectUnobtainableMobileTemplate"/>, for the same reason and against the same
	/// failure. An unreadable web template leaves <c>Names</c>/<c>Nodes</c> empty, and two things then go
	/// wrong silently:
	/// <list type="bullet">
	/// <item><description><c>PruneTemplateComponents</c> is skipped entirely (it is gated on a non-empty name
	/// set), so the page's whole INHERITED web chrome — <c>TitleContainer</c>, <c>BackButton</c>,
	/// <c>PageTitle</c>, <c>SaveButton</c>, <c>CancelButton</c>, <c>CloseButton</c> — survives the tree walk
	/// and is converted to <c>insert</c> entries.</description></item>
	/// <item><description>The automatic same-name twin is gated on <c>WebBaselineNodes.ContainsKey</c>, so no
	/// auto twin is detected at all and elements the mobile template already provides fall through to the
	/// insert path.</description></item>
	/// </list>
	/// <para>
	/// Both produce exactly the outcome the mobile-side refusal exists to prevent — a guide that instructs the
	/// caller to insert duplicates of native elements — with <c>success: true</c> and nothing anywhere in the
	/// payload saying so. The state was previously passed to <c>Analyze</c> as a
	/// <c>webTemplateUnavailable</c> flag that NOTHING read, so the doc claiming the caller surfaces it was
	/// false (ENG-95827).
	/// </para>
	/// </remarks>
	internal static MobilePageConversionGuideResponse RejectUnobtainableWebTemplate(
		MobilePageConversionGuideArgs args, string sourceType, string webTemplateName,
		bool templateUnavailable) {
		if (!templateUnavailable) {
			return null;
		}
		string named = string.IsNullOrWhiteSpace(webTemplateName)
			? "this page's web template"
			: $"the web template '{webTemplateName}'";
		return Fail(args, sourceType,
			$"Could not read {named}, which this page inherits from. Without that baseline the guide cannot "
			+ "tell an element the page AUTHORED from one it merely inherits, so it would convert the whole "
			+ "inherited chrome (title container, back/save/cancel/close buttons) into inserts and would detect "
			+ "no same-name twins — the mobile page would ship duplicates of elements its own template already "
			+ "provides. Verify the source package is installed in the target environment and that the template "
			+ "schema is reachable, then re-run.");
	}

	private static MobilePageConversionGuideResponse Fail(MobilePageConversionGuideArgs args, string sourceType, string error) =>
		new() {
			Success = false,
			SourceSchemaName = args?.SchemaName,
			SourceType = sourceType,
			Error = error
		};

	/// <summary>
	/// Picks the less-authoritative of two <see cref="ComponentInfoResolution.MapResolvedFrom"/> tiers
	/// (severity <c>environment</c> &lt; <c>environment-superset</c> &lt; <c>latest-fallback</c>) so that when
	/// the mobile and web catalogs resolve to different tiers, the guide reports the worse one — a superset or
	/// fallback on either catalog is surfaced to the caller rather than masked by the exact tier of the other.
	/// </summary>
	internal static string WorseResolvedFrom(string a, string b) {
		static int Rank(string tier) =>
			string.Equals(tier, ComponentInfoResolution.ResolvedFromLatestFallback, StringComparison.OrdinalIgnoreCase) ? 2
			: string.Equals(tier, ComponentInfoResolution.ResolvedFromEnvironmentSuperset, StringComparison.OrdinalIgnoreCase) ? 1
			: 0;
		return Rank(a) >= Rank(b) ? a : b;
	}

	/// <summary>
	/// Gates a detected source type against what the converter supports today. Returns a failure
	/// response to short-circuit with — an already-mobile page, or a not-yet-supported source such as
	/// Classic UI — or <c>null</c> when the source is a supported Freedom UI web page and conversion may
	/// proceed. Extracted as an internal static gate so the safety-critical "never convert an
	/// unsupported source" rule is unit-testable without a live page read.
	/// </summary>
	internal static MobilePageConversionGuideResponse RejectUnsupportedSourceType(
		MobilePageConversionGuideArgs args, string sourceType) {
		if (string.Equals(sourceType, "mobile", StringComparison.OrdinalIgnoreCase)) {
			return Fail(args, sourceType, $"Source page '{args?.SchemaName}' is already a mobile page. Nothing to convert.");
		}
		if (!string.Equals(sourceType, WebToMobileAnalysisService.SourceTypeFreedomWeb, StringComparison.OrdinalIgnoreCase)) {
			return Fail(args, sourceType,
				$"Source page '{args?.SchemaName}' has source type '{sourceType}', which is not yet supported by get-mobile-page-conversion-guide " +
				$"(supported today: '{WebToMobileAnalysisService.SourceTypeFreedomWeb}'). " +
				"A Classic UI page must first be converted to a Freedom UI web page (use the dedicated classic-web -> freedom-web converter), " +
				"then run get-mobile-page-conversion-guide.");
		}
		return null;
	}
}

/// <summary>
/// Arguments for the <c>get-mobile-page-conversion-guide</c> MCP tool.
/// </summary>
public sealed record MobilePageConversionGuideArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Source page schema name, e.g. 'UsrMyApp_FormPage'. Today only Freedom UI web pages are supported.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("target-schema-name")]
	[property: Description("Optional suggested target mobile page schema name. Defaults to the source name with a mobile suffix (e.g. UsrMyApp_FormPage -> UsrMyApp_MobileFormPage).")]
	string TargetSchemaName,

	[property: JsonPropertyName("version")]
	[property: Description("Optional Creatio/registry version used to resolve the mobile and web component registries. Defaults to the latest published registry.")]
	string Version,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'. Preferred for normal MCP work.")]
	string EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Use only when bootstrap is broken or before the environment can be registered through reg-web-app.")]
	string Uri,

	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	string Login,

	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	string Password
);
