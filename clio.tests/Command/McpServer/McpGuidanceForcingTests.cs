using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Properties of the guidance-forcing mechanism: the thin instructions router, the tool-description
/// triggers on guaranteed channels, the three new creatio-* guides, and the deterministic note signal.
/// These tests assert mechanism properties only — never external-agent behavior.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpGuidanceForcingTests {

	// 1400 chars, far below the ~1000-token client-side truncation point. The invariants and the routing
	// table now live in the lazily-loaded core-rules / routing guides, so the always-on instructions carry
	// only the mandatory pointers to those two guides + the telemetry advertisement (~1.2k chars).
	// Any bump here requires re-evaluating truncation safety — do not raise without checking that the new router
	// still fits in one untruncated context window on the lowest-tier MCP client.
	private const int RouterCharCeiling = 1400;

	// Guide names referenced by the always-on instructions (routing), the routing map, and/or the touched
	// tool descriptions. Drift guard: every one must resolve in GuidanceCatalog.
	private static readonly string[] ReferencedGuideNames = [
		"core-rules", "routing", "page-modification",
		"page-modification-overview", "page-modification-field-contract", "page-modification-containers", "page-modification-components",
		"business-rules", "business-rule-filters", "dashboards", "dashboard-creation", "dashboard-design", "widget-layout", "indicator-widget",
		"app-modeling", "esq", "esq-filters", "data-bindings"
	];

	private static string ToolDescription<TTool>() {
		MethodInfo toolMethod = typeof(TTool)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.Single(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);
		return toolMethod.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;
	}

	// ---- Pt 1: router ----

	[Test]
	[Category("Unit")]
	[Description("Keeps the always-on instructions router within the committed char ceiling so it survives client truncation.")]
	public void Router_ShouldStayWithinCharCeiling_WhenMeasured() {
		// Arrange
		string router = McpServerInstructions.Text;

		// Act
		int length = router.Length;

		// Assert
		length.Should().BeLessThanOrEqualTo(RouterCharCeiling,
			because: "the always-on instructions must stay far below the observed ~1000-token truncation point; the baseline was ~9.4k chars / ~2.2k tokens. The invariants and the routing table now live in the lazily-loaded core-rules / routing guides, so instructions carry only the mandatory pointers to those two guides + telemetry (~1.2k chars), and the ceiling was tightened to match");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps the router pure ASCII (English-only) to match every other server instruction and avoid encoding surprises.")]
	public void Router_ShouldBePureAscii_WhenScanned() {
		// Arrange
		string router = McpServerInstructions.Text;

		// Act
		char[] nonAscii = router.Where(c => c > 0x7F).ToArray();

		// Assert
		nonAscii.Should().BeEmpty(
			because: "the router is English-only; a stray non-ASCII char (e.g. an em-dash) signals copy-paste drift");
	}

	[Test]
	[Category("Unit")]
	[Description("The core-rules guide carries every hard invariant; the always-on instructions now mandate reading it first rather than inlining the rules.")]
	public void CoreRulesGuide_ShouldContainHardInvariants_WhenInspected() {
		// Arrange
		GuidanceGetTool tool = new(Substitute.For<IFeatureToggleService>());
		// GetGuidance returns an already-completed Task (synchronous lookup), so resolve it inline.
		GuidanceGetResponse coreRules = tool.GetGuidance(new GuidanceGetArgs("core-rules")).GetAwaiter().GetResult();
		coreRules.Success.Should().BeTrue(because: "core-rules is a registered guidance name");
		string rules = coreRules.Article!.Text;

		// Assert
		rules.Should().Contain("compile-creatio is NOT needed",
			because: "the compile-not-required invariant must be carried by the core-rules guide");
		rules.Should().Contain("progress notification is NOT a timeout",
			because: "the long-running invariant must be carried by the core-rules guide");
		rules.Should().Contain("get-user-culture",
			because: "the profile-culture invariant must be carried by the core-rules guide");
		rules.Should().Contain("Destructive tools",
			because: "the destructive-confirmation invariant must be carried by the core-rules guide");
		rules.Should().Contain("correlation-id",
			because: "the error-handling pointer must be carried by the core-rules guide");
	}

	[Test]
	[Category("Unit")]
	[Description("Ensures every get-guidance name referenced by the always-on instructions resolves in GuidanceCatalog (no dangling pointer); after the routing-table extraction this is the routing pointer.")]
	public void Router_ShouldOnlyReferenceResolvableGuideNames_WhenParsed() {
		// Arrange
		string router = McpServerInstructions.Text;
		IEnumerable<string> routedNames = Regex.Matches(router, @"name=([a-z0-9-]+)")
			.Select(match => match.Groups[1].Value)
			.Distinct();

		// Act / Assert
		routedNames.Should().NotBeEmpty(
			because: "the instructions must point at at least one get-guidance target (the routing map)");
		foreach (string name in routedNames) {
			GuidanceCatalog.TryGet(name, out _).Should().BeTrue(
				because: $"instructions reference get-guidance name={name}, which must exist in GuidanceCatalog");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The always-on instructions must mandate loading the core-rules and routing guides first on any operation, so the lazily-loaded invariants and routing table do not weaken the forcing function.")]
	public void Instructions_ShouldMandateReadingCoreRulesAndRoutingFirst_WhenInspected() {
		// Arrange
		string instructions = McpServerInstructions.Text;

		// Assert
		instructions.Should().Contain("name=core-rules",
			because: "the instructions must point at the core-rules guide by its get-guidance name");
		instructions.Should().Contain("name=routing",
			because: "the instructions must point at the routing guide by its get-guidance name");
		instructions.Should().Contain("FIRST",
			because: "loading core-rules and the routing map must be framed as a mandatory first step, not optional");
	}

	[Test]
	[Category("Unit")]
	[Description("The routing map (now the home of the routing table) must only route to guide names that resolve in GuidanceCatalog (no dangling routes after the extraction).")]
	public void RoutingGuide_ShouldOnlyReferenceResolvableGuideNames_WhenParsed() {
		// Arrange
		GuidanceGetTool tool = new(Substitute.For<IFeatureToggleService>());
		// GetGuidance returns an already-completed Task (synchronous lookup), so resolve it inline
		// rather than marking the test async over a non-awaiting call.
		GuidanceGetResponse routing = tool.GetGuidance(new GuidanceGetArgs("routing")).GetAwaiter().GetResult();
		routing.Success.Should().BeTrue(because: "routing is a registered guidance name");
		IEnumerable<string> routedNames = Regex.Matches(routing.Article!.Text, @"name=([a-z0-9-]+)")
			.Select(match => match.Groups[1].Value)
			.Distinct();

		// Act / Assert
		routedNames.Should().NotBeEmpty(
			because: "the routing map must list at least one get-guidance target");
		foreach (string name in routedNames) {
			GuidanceCatalog.TryGet(name, out _).Should().BeTrue(
				because: $"the routing map routes to get-guidance name={name}, which must exist in GuidanceCatalog");
		}
	}

	// ---- Pt 2: forcing triggers on guaranteed (tool description) channels ----

	[Test]
	[Category("Unit")]
	[Description("Analytics routing is centralized in the page-modification GATE (scalable), not hardcoded into the general page tool descriptions; page write/read tools delegate to that checklist.")]
	public void AnalyticsRouting_ShouldLiveInPageModificationGate_NotInGeneralToolDescriptions() {
		// Arrange
		string[] allPageTools = [
			ToolDescription<PageCreateTool>(),
			ToolDescription<PageUpdateTool>(),
			ToolDescription<PageSyncTool>(),
			ToolDescription<PageGetTool>()
		];
		string[] dispatcherTools = [
			ToolDescription<PageUpdateTool>(),
			ToolDescription<PageSyncTool>(),
			ToolDescription<PageGetTool>()
		];

		// Assert: general tools must NOT bake in specific component types or widget-guide lists
		foreach (string description in allPageTools) {
			description.Should().NotContain("crt.IndicatorWidget",
				because: "a specific component type must not live in a general page tool description (not scalable, truncation-prone)");
			description.Should().NotContain("analytics-widgets",
				because: "analytics routing belongs in the page-modification GATE, not duplicated across every general tool description");
		}

		// Assert: page write/read tools delegate to the stable page-modification pre-edit checklist
		foreach (string description in dispatcherTools) {
			description.Should().Contain("page-modification",
				because: "page write/read tools must route body work through the page-modification pre-edit checklist (the dispatcher)");
		}

		// Assert: the page-modification GATE is what routes dashboard/analytics work to the widget guides
		GuidanceGetTool tool = new(Substitute.For<IFeatureToggleService>());
		// GetGuidance returns an already-completed Task (synchronous lookup), so resolve it inline.
		GuidanceGetResponse pageMod = tool.GetGuidance(new GuidanceGetArgs("page-modification")).GetAwaiter().GetResult();
		pageMod.Article!.Text.Should().Contain("dashboards",
			because: "the page-modification GATE must dispatch dashboard/analytics-widget work to the dashboards guide (which routes onward to indicator-widget + get-component-info)");
	}

	[Test]
	[Category("Unit")]
	[Description("Confirms create-entity-business-rule carries both the business-rules and business-rule-filters triggers on its own description.")]
	public void CreateEntityBusinessRule_ShouldTriggerBothBusinessRuleGuides_WhenDescriptionInspected() {
		// Act
		string description = ToolDescription<CreateEntityBusinessRuleTool>();

		// Assert
		description.Should().Contain("business-rules",
			because: "the rule tool must route declarative rule basics to business-rules");
		description.Should().Contain("business-rule-filters",
			because: "an apply-static-filter action requires the full filter contract from business-rule-filters");
	}

	[Test]
	[Category("Unit")]
	[Description("Drift guard: every guide name referenced by the router or the touched tool triggers resolves in GuidanceCatalog.")]
	public void ReferencedGuideNames_ShouldAllResolve_InGuidanceCatalog() {
		// Assert
		foreach (string name in ReferencedGuideNames) {
			GuidanceCatalog.TryGet(name, out GuidanceCatalogEntry entry).Should().BeTrue(
				because: $"a trigger references get-guidance name={name}; a rename without updating the trigger must fail this test");
			entry.Article.Text.Should().NotBeNullOrWhiteSpace(
				because: $"the {name} guide must carry content");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Companion drift guard (ENG-91556 review follow-up): every page-modification family guide name embedded as a textual cross-reference inside ANY registered guide body must resolve in GuidanceCatalog, so renaming or removing a split sub-guide cannot silently leave a stale prose cross-reference that the name-only ReferencedGuideNames guard would miss.")]
	public void PageModificationFamilyCrossReferences_InGuideBodies_ShouldAllResolve() {
		// Arrange: family tokens are lowercase-hyphen, optionally prefixed with `mobile-`.
		Regex familyToken = new(@"(?:mobile-)?page-modification(?:-[a-z]+)*", RegexOptions.Compiled);

		// Act / Assert: scan every registered guide's article body for family cross-references.
		foreach (string guideName in GuidanceCatalog.GetNames()) {
			GuidanceCatalog.TryGet(guideName, out GuidanceCatalogEntry entry).Should().BeTrue(
				because: $"{guideName} is returned by GetNames so it must resolve in the catalog");
			foreach (Match match in familyToken.Matches(entry.Article.Text)) {
				GuidanceCatalog.TryGet(match.Value, out _).Should().BeTrue(
					because: $"guide '{guideName}' cross-references '{match.Value}' in its text, so that name must exist in GuidanceCatalog — a renamed or removed sub-guide must not leave a stale prose cross-reference");
			}
		}
	}

	// ---- Pt 2: deterministic observability note (response-shape regression) ----

	[Test]
	[Category("Unit")]
	[Description("Confirms the response types carry a nullable note property so the deterministic compile-not-required signal can be emitted.")]
	public void ResponseTypes_ShouldExposeNoteProperty_ForDeterministicSignal() {
		// Assert
		typeof(PageCreateResponse).GetProperty("Note").Should().NotBeNull(
			because: "create-page emits note: 'compile-creatio not required' on success");
		typeof(CommandExecutionResult).GetProperty("Note").Should().NotBeNull(
			because: "update-entity-schema emits note: 'compile-creatio not required' on success");
	}

	[Test]
	[Description("create-page emits the deterministic 'compile-creatio not required' note on a successful create so agents do not run a needless compile after a Freedom UI page save.")]
	public void CreatePage_ShouldEmitCompileNotRequiredNote_WhenCreateSucceeds() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakePageCreateCommand command = new(success: true);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageCreateCommand>(Arg.Any<PageCreateOptions>()).Returns(command);
		PageCreateTool tool = new(command, ConsoleLogger.Instance, commandResolver);

		// Act
		PageCreateResponse response = tool.CreatePage(BuildPageCreateArgs());

		// Assert
		response.Success.Should().BeTrue(
			because: "the fake command reports a successful page create");
		response.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "Freedom UI page bodies are AMD modules served at runtime, so the deterministic note must steer agents away from a needless compile-creatio");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("create-page suppresses the deterministic note when the create fails, so agents are not misled into skipping a compile after an unsuccessful page save.")]
	public void CreatePage_ShouldNotEmitCompileNotRequiredNote_WhenCreateFails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakePageCreateCommand command = new(success: false);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageCreateCommand>(Arg.Any<PageCreateOptions>()).Returns(command);
		PageCreateTool tool = new(command, ConsoleLogger.Instance, commandResolver);

		// Act
		PageCreateResponse response = tool.CreatePage(BuildPageCreateArgs());

		// Assert
		response.Success.Should().BeFalse(
			because: "the fake command reports an unsuccessful page create");
		response.Note.Should().BeNull(
			because: "the deterministic note is gated on success and must not ride a failed create");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("CommandExecutionResult omits the 'note' key from its System.Text.Json wire output when null and emits it when set, so the agent-consumed JSON matches the C# signal.")]
	public void CommandExecutionResultNote_ShouldBeOmittedWhenNullAndPresentWhenSet_OnSystemTextJson() {
		// Arrange
		CommandExecutionResult withoutNote = new(0, []);
		CommandExecutionResult withNote = withoutNote with { Note = CommandExecutionResult.CompileNotRequiredNote };

		// Act
		string jsonWithoutNote = JsonSerializer.Serialize(withoutNote);
		string jsonWithNote = JsonSerializer.Serialize(withNote);

		// Assert
		jsonWithoutNote.Should().NotContain("\"note\"",
			because: "a null note must be omitted from the wire output so the agent does not see an empty signal");
		jsonWithNote.Should().Contain($"\"note\":\"{CommandExecutionResult.CompileNotRequiredNote}\"",
			because: "a set note must reach the agent verbatim under its wire key");
	}

	[Test]
	[Description("PageCreateResponse omits the 'note' key when null and emits it when set under BOTH serializers (System.Text.Json and Newtonsoft), so whichever serializer the host uses produces a consistent agent-consumed signal.")]
	public void PageCreateResponseNote_ShouldBeOmittedWhenNullAndPresentWhenSet_OnBothSerializers() {
		// Arrange
		PageCreateResponse withoutNote = new() { Success = true, SchemaName = "UsrTestPage" };
		PageCreateResponse withNote = new() { Success = true, SchemaName = "UsrTestPage", Note = CommandExecutionResult.CompileNotRequiredNote };

		// Act
		string stjWithoutNote = JsonSerializer.Serialize(withoutNote);
		string stjWithNote = JsonSerializer.Serialize(withNote);
		string newtonsoftWithoutNote = Newtonsoft.Json.JsonConvert.SerializeObject(withoutNote);
		string newtonsoftWithNote = Newtonsoft.Json.JsonConvert.SerializeObject(withNote);

		// Assert
		stjWithoutNote.Should().NotContain("\"note\"",
			because: "System.Text.Json must omit a null note (JsonIgnore WhenWritingNull)");
		stjWithNote.Should().Contain($"\"note\":\"{CommandExecutionResult.CompileNotRequiredNote}\"",
			because: "System.Text.Json must emit a set note under its wire key");
		newtonsoftWithoutNote.Should().NotContain("\"note\"",
			because: "Newtonsoft must omit a null note (NullValueHandling.Ignore)");
		newtonsoftWithNote.Should().Contain($"\"note\":\"{CommandExecutionResult.CompileNotRequiredNote}\"",
			because: "Newtonsoft must emit a set note under its wire key");
	}

	private static PageCreateArgs BuildPageCreateArgs() =>
		new("UsrTestPage", "FormPage", "UsrPackage",
			Caption: null, Description: null, EntitySchemaName: null,
			EnvironmentName: "dev", Uri: null, Login: null, Password: null);

	private sealed class FakePageCreateCommand : PageCreateCommand {
		private readonly bool _success;

		public FakePageCreateCommand(bool success)
			: base(
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<ISchemaTemplateCatalog>(),
				Substitute.For<ILogger>(),
				Substitute.For<Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver>()) {
			_success = success;
		}

		public override bool TryCreatePage(PageCreateOptions options, out PageCreateResponse response) {
			response = _success
				? new PageCreateResponse { Success = true, SchemaName = options.SchemaName }
				: new PageCreateResponse { Success = false, Error = "create failed" };
			return _success;
		}
	}
}
