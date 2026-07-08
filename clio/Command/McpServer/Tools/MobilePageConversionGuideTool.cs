using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

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
public sealed class MobilePageConversionGuideTool {
	private readonly IToolCommandResolver _commandResolver;
	private readonly ILogger _logger;
	private readonly IMobileComponentInfoCatalog _mobileCatalog;
	private readonly IComponentInfoCatalog _webCatalog;
	private readonly IWebToMobilePageConversionRulesCatalog _rulesCatalog;

	public MobilePageConversionGuideTool(
		IToolCommandResolver commandResolver,
		ILogger logger,
		IMobileComponentInfoCatalog mobileCatalog,
		IComponentInfoCatalog webCatalog,
		IWebToMobilePageConversionRulesCatalog rulesCatalog) {
		_commandResolver = commandResolver;
		_logger = logger;
		_mobileCatalog = mobileCatalog;
		_webCatalog = webCatalog;
		_rulesCatalog = rulesCatalog;
	}

	internal const string ToolName = "get-mobile-page-conversion-guide";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Detect a page's source type and return an advisory mobile-conversion GUIDE. " +
		"Supported source type today: Freedom UI WEB (sourceType \"freedom-web\"). Other source types (e.g. Classic UI) " +
		"are detected and reported as not yet supported (Classic UI must first be converted to a Freedom UI web page " +
		"with a separate classic-web -> freedom-web converter). " +
		"ADVISORY-ONLY: this tool builds NO page body and writes NOTHING to Creatio or disk. It returns a guide with: " +
		"sourceType (detected), recommendedMobileTemplate + containerMap (web->mobile container names), sourceStructure " +
		"(the full resolved component tree incl. inherited template components), componentSuggestions (per source component " +
		"type: category directMapping/withAdaptation/alternativeAvailable/unsupported/requiresManualDecision + suggested " +
		"mobile type(s) from the WebToMobilePageConversionRules matrix and registry comparison; a structural mapping like " +
		"grid -> [crt.List, crt.ListItem] carries a note describing how to convert, e.g. the crt.ListItem goes into the " +
		"crt.List itemLayout as the row), mobileContracts (inline " +
		"allowed properties + example + designer defaults for each suggested mobile type), modelConfigDiff / " +
		"viewModelConfigDiff (READY-TO-PASTE diffs — paste them verbatim as the page's diffs; never source the " +
		"data-source section from a pre-existing body or attribute types like ForwardReference get dropped), " +
		"modelConfig / viewModelConfig (same data in full-object form, for reference; viewModelConfig already " +
		"filtered to drop attributes of unsupported components), plus per-element elementMap (each insert carries " +
		"prebuilt mobileValues — paste them verbatim to keep every mobile-supported property, then add only the value binding; " +
		"on a tabbed record page every web tab inserts as its own new mobile tab (elementMap parentName Tabs), and a positional insert carries an `index` to sit above/below the mobile Tabs; " +
		"localized strings (captions AND nested ones like config.title/text.template) are carried verbatim in mobileValues as #ResourceString tokens and collected+resolved into guide.resourceStrings ({key: en-US text}) — register that whole map via update-page resources so every token renders), " +
		"plus pageBusinessRules (the source page's PAGE-level business rules converted for mobile — condition kept, only the " +
		"hide/show/make-* actions whose elements survive; recreate each convertedRules[].rule with create-page-business-rule), " +
		"plus requestConversions (component event-binding requests/actions for mobile — supported requests are kept/remapped inside " +
		"elementMap[].mobileValues; a component whose request the mobile app does not support is DROPPED entirely, appearing as an elementMap `drop`; advisory summary only), " +
		"plus adaptiveLayout (the responsive layout for each MULTI-column grid container - phone collapses to 1 column and stacks, " +
			"tablet/desktop keep the web columns; both the container columns and each child's layoutConfig.adaptive are already baked " +
			"into mobileValues, nothing separate to apply; a single-column grid gets no adaptive; present it to the user to adjust or decline), " +
			"plus constraints and ordered nextSteps. " +
		"YOU (the caller) build the mobile page body from the guide and persist it with create-page (mobile template) + update-page, then validate-page. " +
		"Call get-guidance with name `freedom-page-web-to-mobile-conversion` before acting on the guide.")]
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
			lock (McpToolExecutionLock.SyncRoot) {
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

		string version = string.IsNullOrWhiteSpace(args.Version)
			? ComponentRegistryClient.LatestVersion
			: args.Version.Trim();
		IReadOnlyList<ComponentRegistryEntry> mobileEntries =
			await _mobileCatalog.GetAllAsync(version, cancellationToken).ConfigureAwait(false) ?? [];
		IReadOnlyList<ComponentRegistryEntry> webEntries =
			await _webCatalog.GetAllAsync(version, cancellationToken).ConfigureAwait(false) ?? [];
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
		TemplateMappingRule templateRule = ResolveTemplateRule(rules, pageResponse.Page?.ParentSchemaName);
		IReadOnlyDictionary<string, string> containerNameMap = BuildContainerNameMap(templateRule);
		IReadOnlyDictionary<string, ComponentMappingRule> componentNameMap = BuildComponentNameMap(templateRule);
		IReadOnlyList<WebToMobileAnalysisService.PositionalPlacement> positionalPlacements = BuildPositionalPlacements(templateRule);

		// Positional (:top/:bottom) inserts attach to the mobile anchor's parent container. Read the mobile
		// template to resolve that parent; only needed when the rule declares positional entries.
		IReadOnlyDictionary<string, string> mobileContainerParents = positionalPlacements is { Count: > 0 }
			? LoadMobileContainerParents(templateRule?.Mobile, args)
			: null;

		// Read the source page's web template (its parent schema) so its inherited chrome can be
		// filtered out of the conversion: the merged page tree carries the template's header/scaffold
		// containers, which the mobile template already provides. Best-effort — never blocks the guide.
		IReadOnlySet<string> templateComponentNames = LoadTemplateComponentNames(
			pageResponse.Page?.ParentSchemaName, args);

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
				sourceTemplate: pageResponse.Page?.ParentSchemaName,
				suggestedTarget: targetName,
				containerNameMap: containerNameMap,
				sectionRegistration: sectionRegistration,
				pageBusinessRulesProbe: pageBusinessRules,
				templateComponentNames: templateComponentNames,
				componentNameMap: componentNameMap,
				positionalPlacements: positionalPlacements,
				mobileContainerParents: mobileContainerParents);
		} catch (Exception ex) {
			return Fail(args, sourceType, $"Failed to analyze source page '{args.SchemaName}': {ex.Message}");
		}

		return new MobilePageConversionGuideResponse {
			Success = true,
			SourceSchemaName = args.SchemaName,
			SourceType = sourceType,
			Guide = guide
		};
	}

	/// <summary>
	/// Best-effort read of the source page's web template (its parent schema, e.g.
	/// PageWithTabsFreedomTemplate) so its inherited chrome can be filtered out of the conversion.
	/// Loads the template's merged bundle the same way the source page is loaded and collects every
	/// component name in it (the template + its own base templates). Returns an empty set when the
	/// parent name is missing or the read fails — the guide is then produced without template
	/// subtraction (current behavior). Never throws.
	/// </summary>
	private IReadOnlySet<string> LoadTemplateComponentNames(string parentSchemaName, MobilePageConversionGuideArgs args) {
		var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(parentSchemaName)) {
			return empty;
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
			lock (McpToolExecutionLock.SyncRoot) {
				try {
					command.TryGetPage(options, out templateResponse);
				} finally {
					_logger.ClearMessages();
				}
			}
			if (templateResponse?.Success == true && templateResponse.Bundle?.ViewConfig is { } viewConfig) {
				return WebToMobileAnalysisService.CollectComponentNames(viewConfig);
			}
		} catch (Exception) {
			// Best-effort: a failed template read falls back to no subtraction.
		}
		return empty;
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
	/// Returns the template mapping rule for a web page whose parent template is
	/// <paramref name="webParentTemplate"/>. When several rules share the same web template, the
	/// first one wins (the rules file lists the preferred mobile target first). Null when no rule matches.
	/// </summary>
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
	/// Best-effort read of the mobile template (<paramref name="mobileSchemaName"/>) to map each mobile
	/// container to its parent — used to resolve where a positional (<c>:top</c> / <c>:bottom</c>) insert
	/// attaches (the mobile anchor's parent). Mirrors <see cref="LoadTemplateComponentNames"/>: loads the
	/// template's merged bundle and never throws. Returns an empty map when the name is missing or the read
	/// fails (positional inserts then fall back to the default container).
	/// </summary>
	private IReadOnlyDictionary<string, string> LoadMobileContainerParents(string mobileSchemaName, MobilePageConversionGuideArgs args) {
		var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(mobileSchemaName)) {
			return empty;
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
			lock (McpToolExecutionLock.SyncRoot) {
				try {
					command.TryGetPage(options, out templateResponse);
				} finally {
					_logger.ClearMessages();
				}
			}
			if (templateResponse?.Success == true && templateResponse.Bundle?.ViewConfig is { } viewConfig) {
				return WebToMobileAnalysisService.CollectParentByName(viewConfig);
			}
		} catch (Exception) {
			// Best-effort: a failed mobile-template read falls back to the default positional container.
		}
		return empty;
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

	private static MobilePageConversionGuideResponse Fail(MobilePageConversionGuideArgs args, string sourceType, string error) =>
		new() {
			Success = false,
			SourceSchemaName = args?.SchemaName,
			SourceType = sourceType,
			Error = error
		};

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
