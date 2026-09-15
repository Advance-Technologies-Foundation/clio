using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Unit tests for <see cref="MobilePageConversionGuideTool.ClassifyMissingTargetCandidates"/> and
/// <see cref="MobilePageConversionGuideTool.ClassifyCandidateSourceType"/> — the best-effort
/// post-pass that turns a missing-target candidate's schema name into a source-type classification and a
/// <see cref="MissingTargetCandidateAction"/>. Both members are internal and reachable via
/// InternalsVisibleTo("clio.tests"), driven through a <see cref="MobilePageConversionGuideTool"/> instance
/// whose <see cref="PageGetCommand"/> reads are fully substituted, so no live environment is needed.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobilePageConversionGuideToolCandidateClassificationTests {

	private IToolCommandResolver _commandResolver;
	private MobilePageConversionGuideTool _sut;

	[SetUp]
	public void SetUp() {
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("candidate-classification-tenant");
		_sut = new MobilePageConversionGuideTool(
			_commandResolver,
			Substitute.For<ILogger>(),
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IWebToMobilePageConversionRulesCatalog>(),
			Substitute.For<IPlatformVersionResolverFactory>(),
			Substitute.For<ISettingsRepository>());
	}

	private static MobilePageConversionGuideArgs Args(string schemaName = "UsrLeads_FormPage") =>
		new(schemaName, TargetSchemaName: null, Version: null, EnvironmentName: null, Uri: null, Login: null, Password: null);

	/// <summary>A real, working PageGetCommand whose hierarchy read resolves ONE schema with the given numeric schema type.</summary>
	private static PageGetCommand CreateWorkingPageGetCommand(string schemaName, int schemaTypeNumericValue) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(
				$$"""{"success":true,"rows":[{"Name":"{{schemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"pkg-1","ParentSchemaName":null}]}""");

		string body = "define(\"" + schemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { "
			+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
			+ "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
			+ "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, "
			+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
			+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
			+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = schemaName, PackageUId = "pkg-1", PackageName = "UsrPkg",
				SchemaVersion = 1, Body = body, SchemaType = schemaTypeNumericValue
			}
		]);

		return new PageGetCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(),
			hierarchyClient, new PageSchemaBodyParser(),
			new PageBundleBuilder(() => new JsonDiffApplier(), () => new JsonPathDiffApplier()),
			Substitute.For<IPageFileWriter>());
	}

	/// <summary>A PageGetCommand whose collaborators are all unconfigured substitutes: TryGetPage degrades to Success=false without any network access.</summary>
	private static PageGetCommand CreateNotFoundPageGetCommand() => new(
		Substitute.For<IApplicationClient>(),
		Substitute.For<IServiceUrlBuilder>(),
		Substitute.For<ILogger>(),
		Substitute.For<IPageDesignerHierarchyClient>(),
		Substitute.For<IPageSchemaBodyParser>(),
		Substitute.For<IPageBundleBuilder>(),
		Substitute.For<IPageFileWriter>());

	// ── ClassifyCandidateSourceType ──────────────────────────────────────────────────────────────

	[Test]
	[Description("A candidate that reads back as a Freedom UI web page classifies as freedom-web and recommends converting it directly.")]
	public void ClassifyCandidateSourceType_FreedomWebCandidate_RecommendsConvertDirectly() {
		// Arrange — computed BEFORE .Returns(): NSubstitute tracks the "last call" on the thread, and
		// CreateWorkingPageGetCommand configures substitutes of its own, which would otherwise steal it.
		PageGetCommand command = CreateWorkingPageGetCommand("CandidatePage", schemaTypeNumericValue: 9);
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);

		// Act
		(string sourceType, string action) = _sut.ClassifyCandidateSourceType("CandidatePage", Args());

		// Assert
		sourceType.Should().Be(WebToMobileAnalysisService.SourceTypeFreedomWeb,
			because: "schema type 9 is the platform's Freedom UI web value");
		action.Should().Be(MissingTargetCandidateAction.ConvertDirectly,
			because: "a freedom-web candidate is ready for a direct conversion");
	}

	[Test]
	[Description("A candidate that reads back as a Freedom UI MOBILE page classifies as mobile and recommends skipping it — a degenerate case, never offered for conversion.")]
	public void ClassifyCandidateSourceType_MobileCandidate_RecommendsSkip() {
		// Arrange
		PageGetCommand command = CreateWorkingPageGetCommand("CandidatePage", schemaTypeNumericValue: 10);
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);

		// Act
		(string sourceType, string action) = _sut.ClassifyCandidateSourceType("CandidatePage", Args());

		// Assert
		sourceType.Should().Be("mobile", because: "schema type 10 is the platform's Freedom UI mobile value");
		action.Should().Be(MissingTargetCandidateAction.SkipAlreadyMobile,
			because: "the candidate already has a mobile page — proposing to convert it again would be nonsense");
	}

	[Test]
	[Description("A candidate whose schema type is neither web nor mobile (Classic UI, or unrecognized) classifies outside both buckets and recommends a classic->freedom pass first.")]
	public void ClassifyCandidateSourceType_ClassicOrUnrecognizedCandidate_RecommendsClassicFirst() {
		// Arrange — schema type 3 is neither the web (9) nor mobile (10) platform value.
		PageGetCommand command = CreateWorkingPageGetCommand("CandidatePage", schemaTypeNumericValue: 3);
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);

		// Act
		(string sourceType, string action) = _sut.ClassifyCandidateSourceType("CandidatePage", Args());

		// Assert
		sourceType.Should().Be("unknown",
			because: "PageSchemaTypeExtensions.ToLabel maps every non-web/mobile value to 'unknown'");
		action.Should().Be(MissingTargetCandidateAction.ConvertClassicFirst,
			because: "neither web nor mobile means the candidate needs a classic->freedom migration before it can become a mobile page");
	}

	[Test]
	[Description("A candidate schema that cannot be read at all (renamed, deleted, transport failure) gets no source type and is flagged for a manual decision — never a guess.")]
	public void ClassifyCandidateSourceType_UnreadableCandidate_RecommendsManualDecision() {
		// Arrange
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(CreateNotFoundPageGetCommand());

		// Act
		(string sourceType, string action) = _sut.ClassifyCandidateSourceType("GoneCandidatePage", Args());

		// Assert
		sourceType.Should().BeNull(because: "an unreadable schema has no classification to report");
		action.Should().Be(MissingTargetCandidateAction.ManualCandidateNotFound,
			because: "the caller must be told to decide manually rather than being handed an invented source type");
	}

	// ── ClassifyMissingTargetCandidates ──────────────────────────────────────────────────────────

	private static MobilePageConversionGuide GuideWith(
		IReadOnlyList<MissingTargetPage> missingTargetPages = null,
		IReadOnlyList<UnresolvedTargetRequest> unresolvedTargetRequests = null) =>
		new() {
			SourcePage = "SourcePage",
			SourceType = WebToMobileAnalysisService.SourceTypeFreedomWeb,
			RequestConversions = new RequestConversionInfo {
				TargetsProbed = true,
				MissingTargetPages = missingTargetPages ?? [],
				UnresolvedTargetRequests = unresolvedTargetRequests ?? []
			}
		};

	[Test]
	[Description("A guide with no missing-target candidates at all triggers no read whatsoever.")]
	public void ClassifyMissingTargetCandidates_NoCandidates_ReadsNothing() {
		// Arrange
		MobilePageConversionGuide guide = GuideWith();

		// Act
		_sut.ClassifyMissingTargetCandidates(guide, Args());

		// Assert
		_commandResolver.DidNotReceive().Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("A web-page missingTargetPages entry gets its resolvedSourceType/recommendedAction filled in place.")]
	public void ClassifyMissingTargetCandidates_WebPageCandidate_FillsClassificationInPlace() {
		// Arrange
		MissingTargetPage candidate = new() { Target = "LegacyPage", TargetKind = MobileActionTargetProbe.KindWebPage };
		MobilePageConversionGuide guide = GuideWith(missingTargetPages: [candidate]);
		PageGetCommand command = CreateWorkingPageGetCommand("LegacyPage", schemaTypeNumericValue: 9);
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);

		// Act
		_sut.ClassifyMissingTargetCandidates(guide, Args());

		// Assert
		candidate.ResolvedSourceType.Should().Be(WebToMobileAnalysisService.SourceTypeFreedomWeb);
		candidate.RecommendedAction.Should().Be(MissingTargetCandidateAction.ConvertDirectly);
	}

	[Test]
	[Description("An entity-kind finding's resolvedCandidateSchemaName gets classified the same way as a web-page missingTargetPages entry.")]
	public void ClassifyMissingTargetCandidates_EntityCandidate_FillsClassificationInPlace() {
		// Arrange
		UnresolvedTargetRequest finding = new() {
			ElementName = "AddButton", Binding = "clicked", WebRequest = "crt.CreateRecordRequest",
			TargetKind = MobileActionTargetProbe.KindEntityDefaultMobilePage, Target = "LeadProduct",
			State = UnresolvedTargetRequest.StateMissing, BindingRemoved = false,
			ResolvedCandidateSchemaName = "LeadProduct_FormPage"
		};
		MobilePageConversionGuide guide = GuideWith(unresolvedTargetRequests: [finding]);
		PageGetCommand command = CreateWorkingPageGetCommand("LeadProduct_FormPage", schemaTypeNumericValue: 9);
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);

		// Act
		_sut.ClassifyMissingTargetCandidates(guide, Args());

		// Assert
		finding.ResolvedSourceType.Should().Be(WebToMobileAnalysisService.SourceTypeFreedomWeb);
		finding.RecommendedAction.Should().Be(MissingTargetCandidateAction.ConvertDirectly);
	}

	[Test]
	[Description("A finding with no resolved candidate (ResolvedCandidateSchemaName null) is skipped entirely: there is nothing to classify, and no read is issued for it.")]
	public void ClassifyMissingTargetCandidates_FindingWithoutCandidate_IsSkipped() {
		// Arrange
		UnresolvedTargetRequest finding = new() {
			ElementName = "AddButton", Binding = "clicked", WebRequest = "crt.CreateRecordRequest",
			TargetKind = MobileActionTargetProbe.KindEntityDefaultMobilePage, Target = "LeadProduct",
			State = UnresolvedTargetRequest.StateMissing, BindingRemoved = false,
			ResolvedCandidateSchemaName = null
		};
		MobilePageConversionGuide guide = GuideWith(unresolvedTargetRequests: [finding]);

		// Act
		_sut.ClassifyMissingTargetCandidates(guide, Args());

		// Assert
		finding.ResolvedSourceType.Should().BeNull();
		finding.RecommendedAction.Should().BeNull();
		_commandResolver.DidNotReceive().Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("The SAME candidate name referenced from BOTH a missingTargetPages entry and a resolved entity finding is read exactly once and propagated to both.")]
	public void ClassifyMissingTargetCandidates_SameNameFromBothSources_ReadsOnceAndPropagatesToBoth() {
		// Arrange
		MissingTargetPage webPageCandidate = new() { Target = "SharedPage", TargetKind = MobileActionTargetProbe.KindWebPage };
		UnresolvedTargetRequest entityFinding = new() {
			ElementName = "AddButton", Binding = "clicked", WebRequest = "crt.CreateRecordRequest",
			TargetKind = MobileActionTargetProbe.KindEntityDefaultMobilePage, Target = "SomeEntity",
			State = UnresolvedTargetRequest.StateMissing, BindingRemoved = false,
			ResolvedCandidateSchemaName = "SharedPage"
		};
		MobilePageConversionGuide guide = GuideWith(
			missingTargetPages: [webPageCandidate], unresolvedTargetRequests: [entityFinding]);
		PageGetCommand command = CreateWorkingPageGetCommand("SharedPage", schemaTypeNumericValue: 9);
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);

		// Act
		_sut.ClassifyMissingTargetCandidates(guide, Args());

		// Assert
		webPageCandidate.ResolvedSourceType.Should().Be(WebToMobileAnalysisService.SourceTypeFreedomWeb);
		entityFinding.ResolvedSourceType.Should().Be(WebToMobileAnalysisService.SourceTypeFreedomWeb,
			because: "the two sources named the same candidate, so one read must classify both");
		_commandResolver.Received(1).Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("Two entity-default-mobile-page findings from DIFFERENT buttons that resolved the SAME candidate (two buttons creating the same missing object) are read exactly once and classified identically — the invariant a caller relies on to group them into one offer instead of two.")]
	public void ClassifyMissingTargetCandidates_TwoEntityFindingsSameCandidate_ReadOnceAndClassifyIdentically() {
		// Arrange — two buttons, same missing object, same resolved candidate (as WebToMobileAnalysisService
		// would produce for two crt.CreateRecordRequest buttons on the same entity).
		UnresolvedTargetRequest firstFinding = new() {
			ElementName = "FirstAddButton", Binding = "clicked", WebRequest = "crt.CreateRecordRequest",
			TargetKind = MobileActionTargetProbe.KindEntityDefaultMobilePage, Target = "LeadProduct",
			State = UnresolvedTargetRequest.StateMissing, BindingRemoved = false,
			ResolvedCandidateSchemaName = "LeadProduct_FormPage"
		};
		UnresolvedTargetRequest secondFinding = new() {
			ElementName = "SecondAddButton", Binding = "clicked", WebRequest = "crt.CreateRecordRequest",
			TargetKind = MobileActionTargetProbe.KindEntityDefaultMobilePage, Target = "LeadProduct",
			State = UnresolvedTargetRequest.StateMissing, BindingRemoved = false,
			ResolvedCandidateSchemaName = "LeadProduct_FormPage"
		};
		MobilePageConversionGuide guide = GuideWith(unresolvedTargetRequests: [firstFinding, secondFinding]);
		PageGetCommand command = CreateWorkingPageGetCommand("LeadProduct_FormPage", schemaTypeNumericValue: 9);
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);

		// Act
		_sut.ClassifyMissingTargetCandidates(guide, Args());

		// Assert
		firstFinding.ResolvedSourceType.Should().Be(WebToMobileAnalysisService.SourceTypeFreedomWeb);
		firstFinding.RecommendedAction.Should().Be(MissingTargetCandidateAction.ConvertDirectly);
		secondFinding.ResolvedSourceType.Should().Be(WebToMobileAnalysisService.SourceTypeFreedomWeb,
			because: "both findings named the same distinct candidate, so classifying one read must classify both "
				+ "identically — a caller grouping by resolvedCandidateSchemaName cannot offer two DIFFERENT "
				+ "classifications for what is really one candidate page");
		secondFinding.RecommendedAction.Should().Be(MissingTargetCandidateAction.ConvertDirectly);
		// NSubstitute's Received() carries no because overload: one candidate name must cost one page read,
		// however many findings share it — otherwise the per-guide-call ceiling would be spent per FINDING
		// instead of per DISTINCT candidate.
		_commandResolver.Received(1).Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("Past the classification read ceiling the remaining DISTINCT candidates are left unclassified rather than triggering an unbounded fan of page reads.")]
	public void ClassifyMissingTargetCandidates_MoreCandidatesThanTheCeiling_LeavesTheRestUnclassified() {
		// Arrange — nine distinct web-page candidates against a ceiling of eight.
		List<MissingTargetPage> candidates = [];
		for (int i = 0; i < 9; i++) {
			candidates.Add(new MissingTargetPage { Target = $"Page{i}", TargetKind = MobileActionTargetProbe.KindWebPage });
		}
		MobilePageConversionGuide guide = GuideWith(missingTargetPages: candidates);
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(callInfo => {
				string schemaName = ((PageGetOptions)callInfo.Arg<EnvironmentOptions>()).SchemaName;
				return CreateWorkingPageGetCommand(schemaName, schemaTypeNumericValue: 9);
			});

		// Act
		_sut.ClassifyMissingTargetCandidates(guide, Args());

		// Assert
		candidates.FindAll(c => c.ResolvedSourceType is not null).Should().HaveCount(8,
			because: "the ceiling bounds the number of page reads issued for one guide call");
		candidates.FindAll(c => c.ResolvedSourceType is null).Should().HaveCount(1,
			because: "the candidate past the ceiling must stay unclassified rather than guessed");
	}
}
