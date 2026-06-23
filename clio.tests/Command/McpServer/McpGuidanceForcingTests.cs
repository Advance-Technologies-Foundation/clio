using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

	private const int RouterCharCeiling = 2900;

	private static readonly string[] NewCreatioGuides = ["analytics-widgets"];

	// Guides rolled back at business request (must NOT be registered or routed).
	private static readonly string[] RolledBackGuides = ["ui-guidelines", "schema-naming"];

	// Guide names referenced by the router routing table and/or the touched tool descriptions.
	// Drift guard: every one must resolve in GuidanceCatalog.
	private static readonly string[] ReferencedGuideNames = [
		"page-modification", "business-rules", "business-rule-filters", "dashboards", "indicator-widget",
		"analytics-widgets", "app-modeling", "esq", "esq-filters", "data-bindings"
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
			because: "the router must stay far below the observed ~1000-token truncation point; the baseline was ~9.4k chars / ~2.2k tokens (ceiling raised to fit the merged-in product-telemetry advertisement, still far below baseline)");
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
	[Description("Keeps the hard invariants in the always-on router so they survive even if the rest is truncated.")]
	public void Router_ShouldContainHardInvariants_WhenInspected() {
		// Arrange
		string router = McpServerInstructions.Text;

		// Assert
		router.Should().Contain("compile-creatio is NOT needed",
			because: "the compile-not-required invariant must survive truncation to stop needless compile/restart");
		router.Should().Contain("progress notification is NOT a timeout",
			because: "the long-running invariant must survive truncation to stop cancel/retry of create-app*");
		router.Should().Contain("get-user-culture",
			because: "the profile-culture invariant must survive truncation so captions use the right language");
		router.Should().Contain("Destructive tools",
			because: "the destructive-confirmation invariant must survive truncation");
		router.Should().Contain("correlation-id",
			because: "the error-handling pointer must survive truncation");
	}

	[Test]
	[Category("Unit")]
	[Description("Ensures every guide name referenced in the router routing table resolves in GuidanceCatalog (no dangling routes).")]
	public void Router_ShouldOnlyReferenceResolvableGuideNames_WhenParsed() {
		// Arrange
		string router = McpServerInstructions.Text;
		IEnumerable<string> routedNames = Regex.Matches(router, @"name=([a-z0-9-]+)")
			.Select(match => match.Groups[1].Value)
			.Distinct();

		// Act / Assert
		routedNames.Should().NotBeEmpty(
			because: "the router routing table must list at least one get-guidance target");
		foreach (string name in routedNames) {
			GuidanceCatalog.TryGet(name, out _).Should().BeTrue(
				because: $"router routes to get-guidance name={name}, which must exist in GuidanceCatalog");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Confirms the router routes to the kept creatio-* guide(s) and drops the rolled-back ui-guidelines/schema-naming routes.")]
	public void Router_ShouldRouteToKeptCreatioGuides_AndDropRolledBackOnes_WhenInspected() {
		// Arrange
		string router = McpServerInstructions.Text;

		// Assert
		foreach (string name in NewCreatioGuides) {
			router.Should().Contain($"name={name}",
				because: $"the router must route work to the kept {name} guide");
		}
		foreach (string name in RolledBackGuides) {
			router.Should().NotContain($"name={name}",
				because: $"{name} was rolled back at business request and must not be routed");
		}
	}

	// ---- Pt 4: catalog registration + guide content ----

	[Test]
	[Category("Unit")]
	[Description("Registers the three new creatio-* guides in GuidanceCatalog alongside the existing static guides.")]
	public void GuidanceCatalog_ShouldRegisterNewCreatioGuides_WhenQueried() {
		// Act
		IReadOnlyList<string> names = GuidanceCatalog.GetNames();

		// Assert
		names.Should().Contain(NewCreatioGuides,
			because: "Pt 4 keeps analytics-widgets as a static guide");
		names.Should().NotContain(RolledBackGuides,
			because: "ui-guidelines and schema-naming were rolled back at business request");
		names.Should().Contain(["dashboards", "indicator-widget", "page-modification", "app-modeling"],
			because: "the original static guides must remain registered");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a non-empty article with the canonical URI for each new creatio-* guide via get-guidance.")]
	public async Task GuidanceGet_ShouldReturnArticle_ForEachNewCreatioGuide() {
		// Arrange
		GuidanceGetTool tool = new();

		foreach (string name in NewCreatioGuides) {
			// Act
			GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs(name));

			// Assert
			result.Success.Should().BeTrue(
				because: $"{name} is a registered guidance name after Pt 4");
			result.Article.Should().NotBeNull(
				because: $"a successful {name} lookup must return the resolved article");
			result.Article!.Uri.Should().Be($"docs://mcp/guides/{name}",
				because: $"the {name} guide must expose its canonical docs:// URI");
			result.Article.Text.Should().NotBeNullOrWhiteSpace(
				because: $"the {name} guide must carry article content");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies the analytics-widgets guide references dashboards and indicator-widget by name (see-also) instead of duplicating their content.")]
	public async Task AnalyticsWidgetsGuide_ShouldReferenceNeighborsByName_WhenInspected() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("analytics-widgets"));
		string article = result.Article!.Text;

		// Assert
		article.Should().Contain("See also",
			because: "analytics-widgets is a pointer guide that must route to its neighbors");
		article.Should().Contain("name=dashboards",
			because: "the dashboards layout math lives in the dashboards guide and must be referenced by name, not copied");
		article.Should().Contain("name=indicator-widget",
			because: "the metric aggregation detail lives in the indicator-widget guide and must be referenced by name, not copied");
	}

	// ---- Pt 2: forcing triggers on guaranteed (tool description) channels ----

	[Test]
	[Category("Unit")]
	[Description("Analytics routing is centralized in the page-modification GATE (scalable), not hardcoded into the general page tool descriptions; page write/read tools delegate to that checklist.")]
	public async Task AnalyticsRouting_ShouldLiveInPageModificationGate_NotInGeneralToolDescriptions() {
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
		GuidanceGetTool tool = new();
		GuidanceGetResponse pageMod = await tool.GetGuidance(new GuidanceGetArgs("page-modification"));
		pageMod.Article!.Text.Should().Contain("dashboards",
			because: "the page-modification GATE must dispatch dashboard work to the dashboards guide");
		pageMod.Article.Text.Should().Contain("analytics-widgets",
			because: "the page-modification GATE must make the analytics-widgets guide reachable from page work");
	}

	[Test]
	[Category("Unit")]
	[Description("Confirms the entity tools trigger app-modeling (create/update) and business-rules (create/update/lookup/column); schema-naming was rolled back.")]
	public void EntityTools_ShouldTriggerModelingAndBusinessRulesGuidance_WhenDescriptionInspected() {
		// Assert
		ToolDescription<CreateEntitySchemaTool>().Should().Contain("app-modeling",
			because: "create-entity-schema must route schema-design workflow to app-modeling");
		ToolDescription<UpdateEntitySchemaTool>().Should().Contain("app-modeling",
			because: "update-entity-schema must route schema-design workflow to app-modeling");
		ToolDescription<CreateLookupTool>().Should().Contain("business-rules",
			because: "create-lookup must route conditional behavior to business-rules for consistency with create-entity-schema");
		ToolDescription<ModifyEntitySchemaColumnTool>().Should().Contain("business-rules",
			because: "modify-entity-schema-column must route conditional behavior to business-rules for consistency");
		foreach (string name in RolledBackGuides) {
			ToolDescription<CreateEntitySchemaTool>().Should().NotContain(name,
				because: $"{name} was rolled back and must not be referenced by entity tool descriptions");
		}
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
		response.Note.Should().Be("compile-creatio not required",
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
		CommandExecutionResult withNote = withoutNote with { Note = "compile-creatio not required" };

		// Act
		string jsonWithoutNote = JsonSerializer.Serialize(withoutNote);
		string jsonWithNote = JsonSerializer.Serialize(withNote);

		// Assert
		jsonWithoutNote.Should().NotContain("\"note\"",
			because: "a null note must be omitted from the wire output so the agent does not see an empty signal");
		jsonWithNote.Should().Contain("\"note\":\"compile-creatio not required\"",
			because: "a set note must reach the agent verbatim under its wire key");
	}

	[Test]
	[Description("PageCreateResponse omits the 'note' key when null and emits it when set under BOTH serializers (System.Text.Json and Newtonsoft), so whichever serializer the host uses produces a consistent agent-consumed signal.")]
	public void PageCreateResponseNote_ShouldBeOmittedWhenNullAndPresentWhenSet_OnBothSerializers() {
		// Arrange
		PageCreateResponse withoutNote = new() { Success = true, SchemaName = "UsrTestPage" };
		PageCreateResponse withNote = new() { Success = true, SchemaName = "UsrTestPage", Note = "compile-creatio not required" };

		// Act
		string stjWithoutNote = JsonSerializer.Serialize(withoutNote);
		string stjWithNote = JsonSerializer.Serialize(withNote);
		string newtonsoftWithoutNote = Newtonsoft.Json.JsonConvert.SerializeObject(withoutNote);
		string newtonsoftWithNote = Newtonsoft.Json.JsonConvert.SerializeObject(withNote);

		// Assert
		stjWithoutNote.Should().NotContain("\"note\"",
			because: "System.Text.Json must omit a null note (JsonIgnore WhenWritingNull)");
		stjWithNote.Should().Contain("\"note\":\"compile-creatio not required\"",
			because: "System.Text.Json must emit a set note under its wire key");
		newtonsoftWithoutNote.Should().NotContain("\"note\"",
			because: "Newtonsoft must omit a null note (NullValueHandling.Ignore)");
		newtonsoftWithNote.Should().Contain("\"note\":\"compile-creatio not required\"",
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
