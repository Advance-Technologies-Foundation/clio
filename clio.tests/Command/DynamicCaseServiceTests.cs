using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentAssertions.Execution;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class DynamicCaseServiceTests {

	private const string Env = "sandbox";
	private const string Package = "UsrOrders";
	private const string Entity = "UsrOrder";
	private const string Page = "UsrOrders_FormPage";
	private const string CaseUId = "5c58c4c4-134b-4744-9c67-96d9c69c9d55";

	private IBuildCaseClient _client;
	private IDynamicCaseSession _session;
	private IDynamicCasePageStep _pageStep;
	private IPackageDependencyManager _dependencies;
	private ISettingsRepository _settings;
	private IServiceUrlBuilder _urlBuilder;
	private DynamicCaseService _service;

	[SetUp]
	public void Setup() {
		_client = Substitute.For<IBuildCaseClient>();
		_session = Substitute.For<IDynamicCaseSession>();
		_client.OpenSession(Env).Returns(_session);
		_pageStep = Substitute.For<IDynamicCasePageStep>();
		_dependencies = Substitute.For<IPackageDependencyManager>();
		_settings = Substitute.For<ISettingsRepository>();
		EnvironmentSettings env = new() { Uri = "http://sandbox", IsNetCore = false };
		_settings.FindEnvironment(Env).Returns(env);
		_urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_urlBuilder.Build(Arg.Any<string>(), env)
			.Returns(call => "http://sandbox/0/" + call.ArgAt<string>(0));
		_service = new DynamicCaseService(_client, _pageStep, _dependencies, _settings, _urlBuilder);
		_pageStep.Plan(Env, Package, Entity, Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Returns(PlannedPage());
		_dependencies.AddDependenciesReportingCompilation(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Returns(call => new PackageDependencyAddResult(
				[.. call.Arg<IEnumerable<PackageDependencySpec>>().Select(spec => spec.Name)], false));
		_pageStep.Write(Env, Arg.Any<DynamicCasePagePlan>()).Returns(new DynamicCasePageWrite(true, null, []));
	}

	[TearDown]
	public void TearDown() {
		_client.ClearReceivedCalls();
		_session.ClearReceivedCalls();
		_pageStep.ClearReceivedCalls();
		_dependencies.ClearReceivedCalls();
		_session.Dispose();
	}

	private static CreateDynamicCaseOptions Options(bool dryRun = false, bool addProgressBar = true,
		bool addPackageDependencies = true, bool? saveOnChange = null) =>
		new() {
			Environment = Env, PackageName = Package, EntitySchemaName = Entity, StageColumnName = "UsrStatus",
			DryRun = dryRun, AddProgressBar = addProgressBar, AddPackageDependencies = addPackageDependencies,
			SaveOnChange = saveOnChange
		};

	private static DynamicCasePagePlan PlannedPage(string designPackage = Package) => new() {
		Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = Page,
		ResolvedBy = DynamicCaseRecordPageResolver.ResolvedByRelatedPageAddon, DesignPackageName = designPackage,
		Template = "PageWithTabsFreedomTemplate", Fragment = ProgressBarFragment.BuildBody(Page, Entity, string.Empty),
		SaveOnChange = false
	};

	private static DynamicCaseReply<BuildCaseResponse> Envelope(string outcome, bool success = true, string errorCode = null,
		List<string> missing = null, List<string> leftovers = null, List<string> deactivated = null) =>
		new(DynamicCaseReplyKind.Envelope, new BuildCaseResponse {
			Success = success,
			Outcome = outcome,
			ErrorCode = errorCode,
			ErrorMessage = errorCode is null ? null : $"{errorCode} happened",
			Case = new BuildCaseCaseInfo {
				Status = success ? outcome : null, SchemaName = "UsrOrderCase", SchemaUId = CaseUId,
				ConnectionCount = 6, Stages = [new DynamicCaseStage { Name = "Stage1", Color = "#0C8701" },
					new DynamicCaseStage { Name = "Stage2" }, new DynamicCaseStage { Name = "Stage3" }]
			},
			RequiredPackages = ["UsrOrders", "CrtBase"],
			MissingDependencies = missing ?? [],
			Obligations = new DynamicCaseObligations {
				ReloadOpenPages = false,
				Artifacts = [new DynamicCaseArtifactObligation { Kind = "dcm" },
					new DynamicCaseArtifactObligation { Kind = "settings-data" }]
			},
			Leftovers = leftovers,
			DeactivatedCases = deactivated,
			Notes = ["No compile or restart is needed: a case is interpreted."]
		}, null);

	private void Passes(DynamicCaseReply<BuildCaseResponse> validate, DynamicCaseReply<BuildCaseResponse> build = null) {
		_session.Build(Arg.Is<BuildCaseRequest>(request => request.ValidateOnly)).Returns(validate);
		_session.Build(Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly))
			.Returns(build ?? Envelope("created"));
	}

	[Test]
	[Description("The validate pass goes first with ValidateOnly and AllowMissingDependencies = add-package-dependencies; the build pass follows with ValidateOnly false and AllowMissingDependencies ALWAYS false, so a dependency add that did not take fails closed.")]
	public void Create_ShouldSendTheValidatePassFirst_AndTheBuildPassWithMissingDependenciesDisallowed() {
		// Arrange
		Passes(Envelope("planned"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.Created, because: "both passes and the page succeeded");
		Received.InOrder(() => {
			_session.Build(Arg.Is<BuildCaseRequest>(request => request.ValidateOnly && request.AllowMissingDependencies));
			_pageStep.Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>());
			_session.Build(Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly && !request.AllowMissingDependencies));
			_pageStep.Write(Env, Arg.Any<DynamicCasePagePlan>());
		});
	}

	[Test]
	[Description("With add-package-dependencies=false the validate pass carries AllowMissingDependencies false, so the server refuses on a missing dependency and names the calls.")]
	public void Create_ShouldDisallowMissingDependencies_OnTheValidatePass_WhenAddingIsOff() {
		// Arrange
		Passes(Envelope("refused", success: false, errorCode: "dependencies-missing", missing: ["CrtBase"]));

		// Act
		DynamicCaseResult result = _service.Create(Options(addPackageDependencies: false));

		// Assert
		_session.Received(1).Build(Arg.Is<BuildCaseRequest>(request =>
			request.ValidateOnly && !request.AllowMissingDependencies));
		result.ErrorCode.Should().Be("dependencies-missing", because: "the server's refusal is passed through");
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesMissing,
			because: "the missing dependencies were not added");
		_dependencies.DidNotReceiveWithAnyArgs().AddDependenciesReportingCompilation(default!, default!);
	}

	[Test]
	[Description("A server refusal on the validate pass stops everything: no page read, no dependency add, no build pass.")]
	public void Create_ShouldStopAtTheServerRefusal_BeforeAnyPageReadOrWrite() {
		// Arrange
		Passes(Envelope("refused", success: false, errorCode: "rights-missing"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.Refused, because: "the server refused");
		result.ErrorCode.Should().Be("rights-missing", because: "the server's code is the contract");
		result.Success.Should().BeFalse(because: "a refusal is not a success");
		_pageStep.DidNotReceiveWithAnyArgs().Plan(default!, default!, default!, default!, default!);
		_session.DidNotReceive().Build(Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		_dependencies.DidNotReceiveWithAnyArgs().AddDependenciesReportingCompilation(default!, default!);
	}

	[Test]
	[Description("A page refusal is returned before anything is written: the dependencies are not added and the build pass is never called.")]
	public void Create_ShouldNeverCallTheBuildPass_WhenThePageIsRefused() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));
		_pageStep.Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>()).Returns(new DynamicCasePagePlan {
			Status = DynamicCasePageStep.StatusRefused, ErrorCode = DynamicCaseErrorCodes.PageAppendRejected,
			ErrorMessage = "rejected", PageSchemaName = Page
		});

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageAppendRejected, because: "the page refusal is the answer");
		result.Outcome.Should().Be(DynamicCaseOutcomes.Refused, because: "nothing was written");
		_session.DidNotReceive().Build(Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		_dependencies.DidNotReceiveWithAnyArgs().AddDependenciesReportingCompilation(default!, default!);
	}

	[Test]
	[Description("save-on-change reaches the page step, and the page block reports the mode the bar was planned with, so the agent can tell the user before anything is written.")]
	public void Create_ShouldPassTheSaveModeToThePageStep_AndReportIt() {
		// Arrange
		Passes(Envelope("planned"));
		_pageStep.Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>(), true).Returns(new DynamicCasePagePlan {
			Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = Page, DesignPackageName = Package,
			Fragment = ProgressBarFragment.BuildBody(Page, Entity, string.Empty, saveOnChange: true), SaveOnChange = true
		});

		// Act
		DynamicCaseResult result = _service.Create(Options(dryRun: true, saveOnChange: true));

		// Assert
		_pageStep.Received(1).Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>(), true);
		result.Page.SaveOnChange.Should().BeTrue(because: "the page block names the planned mode");
	}

	[Test]
	[Description("A call that names no mode passes none to the page step, which plans the bar's default, and the page block reports that default.")]
	public void Create_ShouldPlanTheBarWithoutASaveOnClick_ByDefault() {
		// Arrange
		Passes(Envelope("planned"));

		// Act
		DynamicCaseResult result = _service.Create(Options(dryRun: true));

		// Assert
		_pageStep.Received(1).Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>(), null);
		result.Page.SaveOnChange.Should().BeFalse(because: "the bar's default: the user saves");
	}

	[Test]
	[Description("A dry run runs both read-only steps and calls no writer: no dependency add, no build pass, no page write. Missing dependencies are reported as planned.")]
	public void Create_ShouldCallNoWriter_OnADryRun() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));

		// Act
		DynamicCaseResult result = _service.Create(Options(dryRun: true));

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.Planned, because: "a dry run plans");
		result.Success.Should().BeTrue(because: "a plan is a success");
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesPlanned,
			because: "the dependency would be added by a real run");
		result.Page.Status.Should().Be(DynamicCasePageStep.StatusPlanned, because: "the page step was planned");
		result.Page.Fragment.Should().NotBeNullOrWhiteSpace(because: "the plan shows the fragment it would write");
		_pageStep.Received(1).Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>());
		_dependencies.DidNotReceiveWithAnyArgs().AddDependenciesReportingCompilation(default!, default!);
		_session.DidNotReceive().Build(Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		_pageStep.DidNotReceiveWithAnyArgs().Write(default!, default!);
	}

	[Test]
	[Description("A dry run with deactivate-unfiltered names the cases it would disable, so the agent can show them before the user's yes.")]
	public void Create_ShouldReportTheCasesItWouldDisable_OnADryRun() {
		// Arrange
		Passes(Envelope("planned", deactivated: ["UsrOrderCase_Old"]));

		// Act
		DynamicCaseResult result = _service.Create(Options(dryRun: true));

		// Assert
		result.DeactivatedCases.Should().Equal(["UsrOrderCase_Old"],
			because: "the plan names the unfiltered case the write would disable");
	}

	[Test]
	[Description("Missing dependencies are added between the passes when adding is allowed, and the result reports them as added.")]
	public void Create_ShouldAddTheMissingDependencies_BeforeTheBuildPass() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase", "CrtCoreBase"]));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		Received.InOrder(() => {
			_dependencies.AddDependenciesReportingCompilation(Package, Arg.Is<IEnumerable<PackageDependencySpec>>(specs =>
				specs.Select(spec => spec.Name).SequenceEqual(new[] { "CrtBase", "CrtCoreBase" })));
			_session.Build(Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		});
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesAdded, because: "the add took");
		result.Dependencies.Added.Should().Equal(["CrtBase", "CrtCoreBase"], because: "the added list is reported");
	}

	[Test]
	[Description("A dependency add the storage refuses with validation errors is dependency-cycle, and the build pass is never called.")]
	public void Create_ShouldRefuseWithDependencyCycle_WhenTheStorageRefusesTheAdd() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new InvalidOperationException("Failed to save. Validation errors: CrtBase/x: cycle"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.DependencyCycle, because: "a storage validation error is a cycle");
		result.Outcome.Should().Be(DynamicCaseOutcomes.Refused, because: "the case was not built");
		_session.DidNotReceive().Build(Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		_pageStep.DidNotReceiveWithAnyArgs().Write(default!, default!);
	}

	[TestCase("refused", "case-exists")]
	[TestCase("failed-rolled-back", "write-failed")]
	[Description("A build pass that does not create the case removes exactly the dependencies this call added, and says so.")]
	public void Create_ShouldRemoveTheAddedDependencies_WhenTheBuildPassDoesNotCreateTheCase(string outcome,
		string errorCode) {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]), Envelope(outcome, success: false, errorCode: errorCode));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		_dependencies.Received(1).RemoveDependencies(Package,
			Arg.Is<IEnumerable<string>>(names => names.SequenceEqual(new[] { "CrtBase" })));
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesRemovedAfterFailure,
			because: "the undo is reported");
		result.Outcome.Should().Be(outcome, because: "the server's outcome is kept");
		result.ErrorCode.Should().Be(errorCode, because: "the server's code is kept");
		_pageStep.DidNotReceiveWithAnyArgs().Write(default!, default!);
	}

	[Test]
	[Description("When removing the added dependencies fails they are reported as left-after-failure, with a warning that names them.")]
	public void Create_ShouldReportLeftAfterFailure_WhenTheUndoFails() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]),
			Envelope("refused", success: false, errorCode: "case-exists"));
		_dependencies.RemoveDependencies(Package, Arg.Any<IEnumerable<string>>())
			.Throws(new InvalidOperationException("save failed"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesLeftAfterFailure,
			because: "the dependencies are still declared");
		result.Warnings.Should().Contain(warning => warning.Contains("CrtBase") && warning.Contains("still declared"),
			because: "the caller must not be told nothing was written");
	}

	[Test]
	[Description("A page write that fails keeps the case: created-page-failed, success false, the fragment and the exact update-page retry arguments.")]
	public void Create_ShouldKeepTheCase_AndReturnRetryArgs_WhenThePageWriteFails() {
		// Arrange
		Passes(Envelope("planned"));
		_pageStep.Write(Env, Arg.Any<DynamicCasePagePlan>()).Returns(new DynamicCasePageWrite(false, "conflict", []));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.CreatedPageFailed, because: "the case exists, the bar does not");
		result.Success.Should().BeFalse(because: "the call did not do everything it was asked to");
		result.Case.SchemaName.Should().Be("UsrOrderCase", because: "the kept case is reported");
		result.Page.Status.Should().Be(DynamicCaseService.PageFailed, because: "the page step failed");
		result.Page.Retry.Tool.Should().Be("update-page", because: "the bar is added with update-page");
		result.Page.Retry.Args.Should().Contain("mode", "append", because: "the fragment is an append body");
		result.Page.Retry.Args.Should().Contain("schema-name", Page, because: "the retry targets the same page");
		result.Page.Retry.Args["body"].Should().Be(result.Page.Fragment,
			because: "the retry sends exactly the fragment that was checked");
		_dependencies.DidNotReceiveWithAnyArgs().RemoveDependencies(default!, default!);
	}

	[Test]
	[Description("A page write that fails still names the save mode the bar was planned with, next to the fragment and the retry that carry it.")]
	public void Create_ShouldReportTheSaveMode_WhenThePageWriteFails() {
		// Arrange
		Passes(Envelope("planned"));
		_pageStep.Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>(), true).Returns(new DynamicCasePagePlan {
			Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = Page, DesignPackageName = Package,
			Fragment = ProgressBarFragment.BuildBody(Page, Entity, string.Empty, saveOnChange: true), SaveOnChange = true
		});
		_pageStep.Write(Env, Arg.Any<DynamicCasePagePlan>()).Returns(new DynamicCasePageWrite(false, "conflict", []));

		// Act
		DynamicCaseResult result = _service.Create(Options(saveOnChange: true));

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.CreatedPageFailed, because: "the bar was not written");
		result.Page.SaveOnChange.Should().BeTrue(
			because: "the page block names the mode its fragment and retry carry, also when the write failed");
	}

	[Test]
	[Description("A full-form page whose write fails returns the exact replace that was planned: the whole body and the checksum it was made against, so the retry cannot overwrite a change made since.")]
	public void Create_ShouldReturnTheReplaceRetry_WithTheChecksum_WhenAFullFormPageWriteFails() {
		// Arrange
		Passes(Envelope("planned"));
		DynamicCasePagePlan planned = PlannedPage();
		_pageStep.Plan(Env, Package, Entity, Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Returns(
			new DynamicCasePagePlan {
				Status = planned.Status, PageSchemaName = planned.PageSchemaName, ResolvedBy = planned.ResolvedBy,
				DesignPackageName = planned.DesignPackageName, Template = planned.Template, Fragment = planned.Fragment,
				WriteMode = "replace", WriteBody = "the whole body", ExpectedChecksum = "abc123"
			});
		_pageStep.Write(Env, Arg.Any<DynamicCasePagePlan>()).Returns(new DynamicCasePageWrite(false, "timeout", []));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.CreatedPageFailed, because: "the case exists, the bar does not");
		result.Page.Fragment.Should().Be(planned.Fragment, because: "the fragment still names the two items for undo");
		result.Page.Retry.Args.Should().Contain("mode", "replace", because: "the page takes the bar by replace");
		result.Page.Retry.Args.Should().Contain("body", "the whole body", because: "the retry sends what was dry-run");
		result.Page.Retry.Args.Should().Contain("checksum", "abc123",
			because: "the replace is pinned to the page version its body was built from");
	}

	[Test]
	[Description("A page write that fails outside the case package does not claim the bar was written to the design package.")]
	public void Create_ShouldNotNoteTheDesignPackage_WhenThePageWriteFails() {
		// Arrange
		Passes(Envelope("planned"));
		_pageStep.Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>()).Returns(PlannedPage(designPackage: "Custom"));
		_pageStep.Write(Env, Arg.Any<DynamicCasePagePlan>()).Returns(new DynamicCasePageWrite(false, "conflict", []));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.CreatedPageFailed, because: "the bar was not written");
		result.Notes.Should().NotContain(note => note.Contains("Deploy 'Custom'"),
			because: "nothing was written to the design package");
	}

	[Test]
	[Description("A transport failure on the validate pass is request-failed: no page is read and nothing is written.")]
	public void Create_ShouldReportRequestFailed_WhenTheValidatePassCannotBeSent() {
		// Arrange
		Passes(new DynamicCaseReply<BuildCaseResponse>(DynamicCaseReplyKind.TransportFailure, null, "timeout"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed,
			because: "a timeout says nothing about the package, so no reinstall is advised");
		result.ErrorMessage.Should().NotContain("install-process-builder", because: "a reinstall would not help");
		_pageStep.DidNotReceiveWithAnyArgs().Plan(default!, default!, default!, default!, default!);
	}

	[Test]
	[Description("WCF's request-deserialization fault on the validate pass is request-failed, not a package without BuildCase: the operation exists and only the request shape was rejected.")]
	public void Create_ShouldReportRequestFailed_WhenTheServerCannotReadTheRequest() {
		// Arrange
		Passes(new DynamicCaseReply<BuildCaseResponse>(DynamicCaseReplyKind.NotAnEnvelope, null,
			"The formatter threw an exception while trying to deserialize the message"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the request shape was rejected");
		result.ErrorMessage.Should().NotContain("install-process-builder", because: "a reinstall restarts the site for nothing");
	}

	[Test]
	[Description("A dependency add that fails without storage validation errors is dependency-add-failed, the dependencies stay missing and the build pass is never called.")]
	public void Create_ShouldRefuseWithDependencyAddFailed_WhenTheAddFailsOtherwise() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new InvalidOperationException("Failed to save package dependencies."));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.DependencyAddFailed, because: "no validation error came back");
		result.Dependencies.Status.Should().Be("missing", because: "nothing was added");
		_session.DidNotReceive().Build(Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
	}

	[Test]
	[Description("A created case with the bar written reports the page artifact on top of the server's, the designer link with the 0/ alias of a .NET Framework environment, and no compile.")]
	public void Create_ShouldReportTheDesignerUrlAndThePageArtifact_WhenEverythingLands() {
		// Arrange
		Passes(Envelope("planned"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Case.DesignerUrl.Should().Be($"http://sandbox/0/Nui/ViewModule.aspx?vm=DcmDesigner#case/{CaseUId}",
			because: "the case opens in the DCM designer by UId");
		result.Case.Connections.Count.Should().Be(6, because: "the server's connection count is reported");
		result.Case.Connections.Kind.Should().Be("all-to-all", because: "every stage is reachable from every other");
		result.Obligations.Artifacts.Select(artifact => artifact.Kind).Should().Equal(["dcm", "settings-data", "page"],
			because: "clio adds the page artifact to the server's two");
		result.Obligations.CompileRequired.Should().BeFalse(because: "nothing written needs a compile");
		result.Obligations.RestartRequired.Should().BeFalse(because: "nothing written needs a restart");
		result.Obligations.ReloadOpenPages.Should().BeTrue(
			because: "clio adds the reload for the page it wrote, whatever the server said");
		result.Obligations.Artifacts.Single(artifact => artifact.Kind == "page")
			.Should().Match<DynamicCaseArtifactObligation>(artifact => artifact.Compile == false && artifact.Restart == false,
				because: "a Freedom UI page is served as saved");
		result.Page.Status.Should().Be(DynamicCaseService.PageInserted, because: "the bar was written");
	}

	[Test]
	[Description("A 404 on the validate pass is process-builder-without-build-case: the installed package passed the floor and still has no BuildCase.")]
	public void Create_ShouldReportProcessBuilderWithoutBuildCase_WhenTheValidatePassFindsNoOperation() {
		// Arrange
		Passes(new DynamicCaseReply<BuildCaseResponse>(DynamicCaseReplyKind.RouteMissing, null, "Endpoint not found."));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.ProcessBuilderWithoutBuildCase,
			because: "the package that answered does not carry the operation");
		result.ErrorMessage.Should().Contain("install-process-builder", because: "the remedy is named");
		result.ErrorMessage.Should().Contain("Nothing was written", because: "the validate pass writes nothing");
		_pageStep.DidNotReceiveWithAnyArgs().Plan(default!, default!, default!, default!, default!);
	}

	[Test]
	[Description("A validate-pass body that is not the envelope (a fault from inside BuildCase, an HTML error or login page) is request-failed: only a 404 proves the operation is missing, and a reinstall would restart the site for nothing.")]
	public void Create_ShouldReportRequestFailed_WhenTheValidatePassAnswersWithSomethingElse() {
		// Arrange
		Passes(new DynamicCaseReply<BuildCaseResponse>(DynamicCaseReplyKind.NotAnEnvelope, null, "<html>Login</html>"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed,
			because: "a body that is not the envelope does not prove the package lacks BuildCase");
		result.ErrorMessage.Should().NotContain("install-process-builder", because: "a reinstall would not help");
		_pageStep.DidNotReceiveWithAnyArgs().Plan(default!, default!, default!, default!, default!);
	}

	[Test]
	[Description("The record page is resolved with the missing packages as fallbacks for the add-on read: the case package does not reach the object until they are added, which happens after the plan.")]
	public void Create_ShouldPassTheMissingPackages_ToThePagePlan() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase", "UsrOrdersApp"]));

		// Act
		_service.Create(Options(dryRun: true));

		// Assert
		_pageStep.Received(1).Plan(Env, Package, Entity, null,
			Arg.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "CrtBase", "UsrOrdersApp" })));
	}

	[Test]
	[Description("A build pass that fails with leftovers keeps the dependencies this call added: the case schema or settings row it could not undo still references them.")]
	public void Create_ShouldKeepTheAddedDependencies_WhenTheBuildPassLeavesLeftovers() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]),
			Envelope("failed-leftover", success: false, errorCode: "write-failed",
				leftovers: ["delete-schema UsrOrderCase"]));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		_dependencies.DidNotReceiveWithAnyArgs().RemoveDependencies(default!, default!);
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesKeptForLeftovers,
			because: "the leftovers still need the added dependencies");
		result.Dependencies.Added.Should().Equal(["CrtBase"], because: "what was added is reported");
		result.Warnings.Should().Contain(warning => warning.Contains("CrtBase") && warning.Contains("remove-package-dependency"),
			because: "the caller removes them by hand after the leftovers");
		result.Leftovers.Should().ContainSingle(because: "the server's leftovers are passed on");
	}

	[Test]
	[Description("When the dependency save asks for a configuration compilation the result says so: the server's no-compile note is dropped, a compile obligation and a warning are added, and the case and page themselves still need none.")]
	public void Create_ShouldReportTheCompilation_WhenTheDependencyAddAsksForOne() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Returns(new PackageDependencyAddResult(["CrtBase"], true));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.Created, because: "the case and the bar were written");
		result.Dependencies.CompilationRequired.Should().BeTrue(because: "the server asked for a compilation");
		result.Obligations.CompileRequired.Should().BeTrue(because: "the added dependencies need a compilation");
		result.Obligations.Artifacts.Should().ContainSingle(artifact =>
				artifact.Kind == DynamicCaseService.DependenciesArtifact && artifact.Compile == true,
			because: "the obligation names what needs the compile");
		result.Obligations.Artifacts.Single(artifact => artifact.Kind == "page").Compile.Should().BeFalse(
			because: "the page itself is still served as saved");
		result.Notes.Should().NotContain(note => note.StartsWith("No compile or restart is needed"),
			because: "the server's note speaks for the case alone and would contradict the obligation");
		result.Warnings.Should().Contain(warning => warning.Contains("compile-creatio without package-name")
				&& warning.Contains("until the configuration is compiled"),
			because: "the wording carries the marker that keeps the tool's own no-compile note off");
	}

	[Test]
	[Description("An unreadable reply after the build pass is outcome unknown, worded so the caller re-reads before acting, and the added dependencies are left in place.")]
	public void Create_ShouldReportUnknown_WhenTheBuildPassReplyIsUnreadable() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]),
			new DynamicCaseReply<BuildCaseResponse>(DynamicCaseReplyKind.NotAnEnvelope, null, "<html>"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.Unknown, because: "the write may or may not have landed");
		result.ErrorMessage.Should().Contain("UNKNOWN", because: "the wording of the other process writes is kept");
		result.Success.Should().BeFalse(because: "an unconfirmed write is not a success");
		_dependencies.DidNotReceiveWithAnyArgs().RemoveDependencies(default!, default!);
		_pageStep.DidNotReceiveWithAnyArgs().Write(default!, default!);
	}

	[Test]
	[Description("Dependencies kept for the leftovers still wait for the compilation their add asked for: the obligations say so, not only dependencies.compilationRequired.")]
	public void Create_ShouldCarryTheCompilationIntoTheObligations_WhenTheDependenciesAreKeptForLeftovers() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]),
			Envelope("failed-leftover", success: false, errorCode: "write-failed",
				leftovers: ["delete-schema UsrOrderCase"]));
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Returns(new PackageDependencyAddResult(["CrtBase"], true));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesKeptForLeftovers,
			because: "the leftovers still need them");
		result.Obligations.CompileRequired.Should().BeTrue(because: "an agent that reads only obligations must compile");
		result.Obligations.Artifacts.Should().ContainSingle(artifact =>
				artifact.Kind == DynamicCaseService.DependenciesArtifact && artifact.Compile == true,
			because: "the obligation names what needs the compile");
		result.Warnings.Should().Contain(warning => warning.Contains("until the configuration is compiled"),
			because: "the words agree with the obligation");
	}

	[Test]
	[Description("After an unknown build outcome the dependencies stay declared, so the compilation their add asked for reaches the obligations.")]
	public void Create_ShouldCarryTheCompilationIntoTheObligations_WhenTheBuildOutcomeIsUnknown() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]),
			new DynamicCaseReply<BuildCaseResponse>(DynamicCaseReplyKind.NotAnEnvelope, null, "<html>"));
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Returns(new PackageDependencyAddResult(["CrtBase"], true));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.Unknown, because: "the build reply could not be read");
		result.Dependencies.CompilationRequired.Should().BeTrue(because: "the dependencies were left in place");
		result.Obligations.CompileRequired.Should().BeTrue(because: "an agent that reads only obligations must compile");
		result.Warnings.Should().Contain(warning => warning.Contains("compile-creatio without package-name"),
			because: "the compile is named");
	}

	[Test]
	[Description("Dependencies the undo removed after a failed build need no compile, and the obligations stay the server's.")]
	public void Create_ShouldNotAskForACompile_WhenTheAddedDependenciesWereRemovedAfterTheFailure() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]),
			Envelope("failed-rolled-back", success: false, errorCode: "write-failed"));
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Returns(new PackageDependencyAddResult(["CrtBase"], true));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesRemovedAfterFailure,
			because: "the undo removed them");
		result.Dependencies.CompilationRequired.Should().BeFalse(because: "nothing this call added is declared any more");
		result.Obligations?.CompileRequired.Should().NotBe(true, because: "no dependency waits for a compile");
	}

	[Test]
	[Description("A transport failure while adding the dependencies may come after the save took, so they are removed again; the answer is then the refusal with nothing of them declared.")]
	public void Create_ShouldRemoveTheDependenciesAgain_WhenTheAddFailsInTransport() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new System.Net.Http.HttpRequestException("connection reset"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		_dependencies.Received(1).RemoveDependencies(Package,
			Arg.Is<IEnumerable<string>>(names => names.SequenceEqual(new[] { "CrtBase" })));
		result.Outcome.Should().Be(DynamicCaseOutcomes.Refused, because: "the case was not built");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.DependencyAddFailed, because: "the add failed");
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesMissing,
			because: "after the removal none of them is declared");
		result.Dependencies.Added.Should().BeEmpty(because: "nothing this call added is left");
		_session.ReceivedWithAnyArgs(1).Build(default!);
	}

	[Test]
	[Description("When the removal after a transport failure fails too, the dependencies may be declared: they are reported as left, never as not added.")]
	public void Create_ShouldReportTheDependenciesLeft_WhenTheAddFailsInTransportAndTheRemovalFails() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new System.Net.Http.HttpRequestException("connection reset"));
		_dependencies.RemoveDependencies(Package, Arg.Any<IEnumerable<string>>())
			.Throws(new System.Net.Http.HttpRequestException("still down"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesLeftAfterFailure,
			because: "the save may have applied them and they could not be removed");
		result.Dependencies.Added.Should().Equal(["CrtBase"], because: "the ones that may be declared are named");
		result.Warnings.Should().Contain(warning => warning.Contains("CrtBase") && warning.Contains("remove-package-dependency"),
			because: "the caller is told how to check and remove them");
	}

	[Test]
	[Description("A dependency add the storage refused applied nothing, so nothing is removed afterwards.")]
	public void Create_ShouldNotRemoveAnything_WhenTheDependencyAddIsRefused() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new InvalidOperationException("Failed to save package dependencies."));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		_dependencies.DidNotReceiveWithAnyArgs().RemoveDependencies(default!, default!);
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesMissing, because: "the refused save added none");
	}

	[Test]
	[Description("add-progress-bar=false never touches the page and answers created-page-skipped.")]
	public void Create_ShouldSkipThePage_WhenTheBarIsNotWanted() {
		// Arrange
		Passes(Envelope("planned"));

		// Act
		DynamicCaseResult result = _service.Create(Options(addProgressBar: false));

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.CreatedPageSkipped, because: "only the case was asked for");
		result.Page.Status.Should().Be(DynamicCaseService.PageSkipped, because: "the page step was skipped");
		result.Notes.Should().NotContain(note => note.Contains("save-on-change", StringComparison.Ordinal),
			because: "no mode was named, so there is no request to report as not applied");
		_pageStep.DidNotReceiveWithAnyArgs().Plan(default!, default!, default!, default!, default!);
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("A save-on-change passed with add-progress-bar=false is reported in a note, in the real call and in the dry run, because no bar is added to carry it.")]
	public void Create_ShouldNoteTheUnappliedSaveMode_WhenTheBarIsNotWanted(bool dryRun) {
		// Arrange
		Passes(Envelope("planned"));

		// Act
		DynamicCaseResult result = _service.Create(Options(dryRun: dryRun, addProgressBar: false, saveOnChange: true));

		// Assert
		result.Page.Status.Should().Be(DynamicCaseService.PageSkipped, because: "the page step was skipped");
		result.Page.SaveOnChange.Should().BeNull(because: "no bar is planned, so no mode is reported as planned");
		result.Notes.Should().ContainSingle(note => note.Contains("save-on-change was not applied", StringComparison.Ordinal)
				&& note.Contains("add-progress-bar is false", StringComparison.Ordinal),
			because: "the caller named a mode that this call has no bar to apply to");
		_pageStep.DidNotReceiveWithAnyArgs().Plan(default!, default!, default!, default!, default!);
	}

	[Test]
	[Description("An equivalent case and a page that already has the bar answer already-present and write nothing to the page.")]
	public void Create_ShouldAnswerAlreadyPresent_WhenTheCaseAndTheBarExist() {
		// Arrange
		Passes(Envelope("already-present"), Envelope("already-present"));
		_pageStep.Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>()).Returns(new DynamicCasePagePlan {
			Status = DynamicCasePageStep.StatusAlreadyPresent, PageSchemaName = Page
		});

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.AlreadyPresent, because: "the equivalent case exists");
		result.Success.Should().BeTrue(because: "a safe re-run is a success");
		result.Page.Status.Should().Be(DynamicCasePageStep.StatusAlreadyPresent, because: "the bar is there");
		result.Page.SaveOnChange.Should().BeNull(because: "the existing bar keeps its own mode, which this call did not plan");
		_pageStep.DidNotReceiveWithAnyArgs().Write(default!, default!);
	}

	[Test]
	[Description("A bar written outside the case's package adds the note to deploy both packages together.")]
	public void Create_ShouldNoteTheSecondPackage_WhenThePageIsWrittenOutsideTheCasePackage() {
		// Arrange
		Passes(Envelope("planned"));
		_pageStep.Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>()).Returns(PlannedPage(designPackage: "Custom"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Notes.Should().Contain(note => note.Contains("Deploy 'Custom' together with 'UsrOrders'"),
			because: "the case and its bar now live in two packages");
	}

	[Test]
	[Description("A whole create opens ONE session for its validate and build passes, so the call logs in once, and releases it when the call ends.")]
	public void Create_ShouldSendBothPassesThroughOneSession() {
		// Arrange
		Passes(Envelope("planned"));

		// Act
		_service.Create(Options());

		// Assert
		_client.Received(1).OpenSession(Env);
		_session.ReceivedWithAnyArgs(2).Build(default!);
		_session.Received(1).Dispose();
	}

	[Test]
	[Description("A stage that names a group and successful reaches the build request with both, and a stage that names neither carries neither.")]
	public void Create_ShouldForwardTheStageGroupAndSuccessful_WhenAStageNamesThem() {
		// Arrange
		Passes(Envelope("planned"));
		CreateDynamicCaseOptions options = Options(dryRun: true);
		options.Stages = [
			new DynamicCaseStageItem("New"),
			new DynamicCaseStageItem("Rejected") { GroupSent = true, Group = "New", Successful = false }
		];

		// Act
		_service.Create(options);

		// Assert
		_session.Received(1).Build(Arg.Is<BuildCaseRequest>(request =>
			request.Stages.Count == 2
			&& request.Stages[0].Group == null && request.Stages[0].Successful == null
			&& request.Stages[1].Value == "Rejected" && request.Stages[1].Group == "New"
			&& request.Stages[1].Successful == false));
	}

	private static DescribeDynamicCaseOptions DescribeOptions() => new() { Environment = Env, EntitySchemaName = Entity };

	private static ModifyDynamicCaseOptions ModifyOptions(bool dryRun = false) => new() {
		Environment = Env, CaseName = "UsrOrderCase", DryRun = dryRun
	};

	private static BuildCaseCaseInfo ExistingCase(int connectionCount = 2, DynamicCaseFilter filter = null) => new() {
		Status = "existing", SchemaName = "UsrOrderCase", SchemaUId = CaseUId, PackageName = Package,
		EntitySchemaName = Entity, StageColumnName = "UsrStatus", Enabled = true, ActiveVersion = true,
		ConnectionCount = connectionCount, FinalStages = ["Done"], RunningInstances = 1, PackageReadOnly = false,
		Filter = filter,
		Stages = [
			new DynamicCaseStage { Name = "Stage1", Value = "v1", Caption = "New", RunningInstances = 1 },
			new DynamicCaseStage { Name = "Stage2", Value = "v2", Caption = "Done", IsFinal = true, Successful = true }
		]
	};

	[Test]
	[Description("A describe passes the server's answer through - success, cases, settings rows, notes and warnings - adds the designer link per case, and sends the identity the call named through one session.")]
	public void Describe_ShouldPassTheServerAnswerThrough_WithTheDesignerLink() {
		// Arrange
		_session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.Envelope, new DescribeCaseResponse {
				Success = true,
				Cases = [ExistingCase()],
				Settings = [new DynamicCaseSettingsRow { SysDcmSettingsId = "s1", StageColumnName = "UsrStatus" }],
				Notes = ["note"],
				Warnings = ["warning"]
			}, null));

		// Act
		DescribeDynamicCaseResult result = _service.Describe(DescribeOptions());

		// Assert
		result.Success.Should().BeTrue(because: "the server read the object");
		result.Cases.Should().ContainSingle(because: "the object has one case");
		result.Cases[0].DesignerUrl.Should().Be("http://sandbox/0/Nui/ViewModule.aspx?vm=DcmDesigner#case/" + CaseUId,
			because: "every described case exists and opens in the designer by UId");
		result.Cases[0].FinalStages.Should().Equal(["Done"], because: "the final stages are passed through");
		result.Cases[0].RunningInstances.Should().Be(1, because: "the running cases are passed through");
		result.Cases[0].Stages[0].RunningInstances.Should().Be(1, because: "the per-stage running cases are passed through");
		result.Settings.Should().ContainSingle(row => row.SysDcmSettingsId == "s1", because: "the settings rows are passed through");
		result.Notes.Should().Equal(["note"], because: "the notes are relayed");
		result.Warnings.Should().Equal(["warning"], because: "the warnings are relayed");
		_session.Received(1).Describe(Arg.Is<DescribeCaseRequest>(request =>
			request.EntitySchemaName == Entity && request.CaseName == null && request.CaseUId == null));
		_session.Received(1).Dispose();
	}

	[Test]
	[Description("A described case made in the designer with fewer connections than every ordered pair of stages reports restricted connections, not all-to-all.")]
	public void Describe_ShouldReportRestrictedConnections_WhenNotEveryPairIsConnected() {
		// Arrange
		_session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.Envelope, new DescribeCaseResponse {
				Success = true,
				Cases = [ExistingCase(connectionCount: 1)]
			}, null));

		// Act
		DescribeDynamicCaseResult result = _service.Describe(DescribeOptions());

		// Assert
		result.Cases[0].Connections.Count.Should().Be(1, because: "the server's connection count is reported");
		result.Cases[0].Connections.Kind.Should().Be("restricted",
			because: "two stages have two ordered pairs, and only one of them is connected");
	}

	[Test]
	[Description("A describe passes each case's filter through, the unreadable designer-built one included, and the settings row's filter column.")]
	public void Describe_ShouldPassTheFiltersThrough() {
		// Arrange
		DynamicCaseFilter b2b = new() {
			Column = "UsrType", Values = [new DynamicCaseFilterValue { Value = "t1", Caption = "B2B" }]
		};
		_session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.Envelope, new DescribeCaseResponse {
				Success = true,
				Cases = [ExistingCase(filter: b2b), ExistingCase(filter: new DynamicCaseFilter { Unreadable = true })],
				Settings = [new DynamicCaseSettingsRow {
					SysDcmSettingsId = "s1", StageColumnName = "UsrStatus", HasFilterColumn = true,
					FilterColumnName = "UsrType"
				}]
			}, null));

		// Act
		DescribeDynamicCaseResult result = _service.Describe(DescribeOptions());

		// Assert
		result.Cases[0].Filter.Should().BeSameAs(b2b, because: "the case's filter is passed through as the server read it");
		result.Cases[1].Filter.Unreadable.Should().BeTrue(because: "a designer-built filter is reported, not dropped");
		result.Settings[0].FilterColumnName.Should().Be("UsrType", because: "the settings row names the filter column");
	}

	[Test]
	[Description("An answer whose case block lists no stages leaves the connection kind out rather than guessing it.")]
	public void Describe_ShouldLeaveTheConnectionKindOut_WhenTheAnswerListsNoStages() {
		// Arrange
		BuildCaseCaseInfo noStages = new() {
			Status = "existing", SchemaName = "UsrOrderCase", SchemaUId = CaseUId, ConnectionCount = 6
		};
		_session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.Envelope, new DescribeCaseResponse { Success = true, Cases = [noStages] }, null));

		// Act
		DescribeDynamicCaseResult result = _service.Describe(DescribeOptions());

		// Assert
		result.Cases[0].Connections.Count.Should().Be(6, because: "the server's count is still reported");
		result.Cases[0].Connections.Kind.Should().BeNull(because: "without the stages nothing says which kind it is");
	}

	[Test]
	[Description("A server refusal of a describe is passed through with its code, and no case is invented.")]
	public void Describe_ShouldPassTheRefusalThrough_WhenTheServerRefuses() {
		// Arrange
		_session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.Envelope, new DescribeCaseResponse {
				Success = false, ErrorCode = "case-not-found", ErrorMessage = "No case named UsrNope."
			}, null));

		// Act
		DescribeDynamicCaseResult result = _service.Describe(DescribeOptions());

		// Assert
		result.Success.Should().BeFalse(because: "the server refused");
		result.ErrorCode.Should().Be("case-not-found", because: "the server's code is the contract");
		result.Cases.Should().BeEmpty(because: "nothing was found");
	}

	[Test]
	[Description("A 404 to DescribeCase is process-builder-without-case-change, whose message sends the agent to install-process-builder after the user's approval.")]
	public void Describe_ShouldReportProcessBuilderWithoutCaseChange_WhenTheRouteIsMissing() {
		// Arrange
		_session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.RouteMissing, null, "Endpoint not found."));

		// Act
		DescribeDynamicCaseResult result = _service.Describe(DescribeOptions());

		// Assert
		result.Success.Should().BeFalse(because: "the operation is missing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.ProcessBuilderWithoutCaseChange,
			because: "only a 404 proves the installed package lacks the operation");
		result.ErrorMessage.Should().Contain("install-process-builder").And.Contain("Ask the user",
			because: "the remedy restarts the site, so it needs the user's approval");
	}

	[TestCase(DynamicCaseReplyKind.TransportFailure, "timeout")]
	[TestCase(DynamicCaseReplyKind.NotAnEnvelope, "<html>Login</html>")]
	[Description("Any other describe fault is request-failed and says nothing was changed: a describe never writes.")]
	public void Describe_ShouldReportRequestFailed_WhenTheAnswerCannotBeRead(DynamicCaseReplyKind kind, string detail) {
		// Arrange
		_session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(
			new DynamicCaseReply<DescribeCaseResponse>(kind, null, detail));

		// Act
		DescribeDynamicCaseResult result = _service.Describe(DescribeOptions());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the fault does not prove a missing operation");
		result.ErrorMessage.Should().Contain("nothing was changed").And.Contain(detail, because: "the server's text is relayed");
	}

	[Test]
	[Description("A modify maps the call onto the request: the identity, every stage member with an explicit null group kept, the caption, enabled, the user's yes and dry-run as validateOnly.")]
	public void Modify_ShouldMapTheCallOntoTheRequest() {
		// Arrange
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.Envelope, new ModifyCaseResponse { Success = true, Outcome = "planned" }, null));
		ModifyDynamicCaseOptions options = ModifyOptions(dryRun: true);
		options.Stages = [
			new DynamicCaseStageItem("New") { GroupSent = true, Group = null },
			new DynamicCaseStageItem("Done") { Caption = "Closed", Color = "#FF0000", Successful = false }
		];
		options.Caption = "Order flow";
		options.Enabled = null;
		options.ConfirmRunningInstances = true;
		options.ConfirmStageRecords = true;
		options.ConfirmValueRename = true;

		// Act
		_service.Modify(options);

		// Assert
		_session.Received(1).Modify(Arg.Is<ModifyCaseRequest>(request =>
			request.CaseName == "UsrOrderCase" && request.EntitySchemaName == null
			&& request.ValidateOnly && request.ConfirmRunningInstances && request.Enabled == null
			&& request.ConfirmStageRecords == true && request.ConfirmValueRename == true
			&& request.Caption == "Order flow"
			&& request.Stages.Count == 2
			&& request.Stages[0].GroupSent && request.Stages[0].Group == null
			&& !request.Stages[1].GroupSent && request.Stages[1].Caption == "Closed"
			&& request.Stages[1].Color == "#FF0000" && request.Stages[1].Successful == false));
	}

	[Test]
	[Description("A yes not given maps to an absent key, not false: a package whose contract lacks the key would refuse it as unknown.")]
	public void Modify_ShouldLeaveTheNewConfirmationsOut_WhenNotGiven() {
		// Arrange
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.Envelope, new ModifyCaseResponse { Success = true, Outcome = "planned" }, null));
		ModifyDynamicCaseOptions options = ModifyOptions(dryRun: true);

		// Act
		_service.Modify(options);

		// Assert
		_session.Received(1).Modify(Arg.Is<ModifyCaseRequest>(request =>
			request.ConfirmStageRecords == null && request.ConfirmValueRename == null));
	}

	[Test]
	[Description("A modify that names no stages leaves the stages out of the request, so the server keeps them.")]
	public void Modify_ShouldSendNoStages_WhenTheCallNamesNone() {
		// Arrange
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.Envelope, new ModifyCaseResponse { Success = true, Outcome = "modified" }, null));
		ModifyDynamicCaseOptions options = ModifyOptions();
		options.Enabled = false;

		// Act
		_service.Modify(options);

		// Assert
		_session.Received(1).Modify(Arg.Is<ModifyCaseRequest>(request =>
			request.Stages == null && request.Enabled == false && !request.ValidateOnly));
	}

	[TestCase("planned", true)]
	[TestCase("modified", true)]
	[TestCase("unchanged", true)]
	[TestCase("refused", false)]
	[TestCase("failed-restored", false)]
	[TestCase("failed-leftover", false)]
	[Description("A modify passes the server's answer through - outcome, code, case with its designer link, diff, running instances, obligations, leftovers, notes and warnings - and succeeds only for planned, modified and unchanged.")]
	public void Modify_ShouldPassTheServerAnswerThrough(string outcome, bool success) {
		// Arrange
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.Envelope, new ModifyCaseResponse {
				Success = success,
				Outcome = outcome,
				ErrorCode = success ? null : "stage-in-use",
				ErrorMessage = success ? null : "A removed stage holds running cases.",
				Case = ExistingCase(),
				Diff = new DynamicCaseDiff { Moved = [new DynamicCaseStageChange { Value = "v1", From = "1", To = "2" }] },
				RunningInstances = new DynamicCaseRunningInstances { Total = 1 },
				Obligations = new DynamicCaseObligations { ReloadOpenPages = true },
				Leftovers = success ? null : ["UsrOrderCase metadata"],
				Notes = ["Reload (F5) open pages."],
				Warnings = ["warning"]
			}, null));

		// Act
		ModifyDynamicCaseResult result = _service.Modify(ModifyOptions());

		// Assert
		result.Success.Should().Be(success, because: "only planned, modified and unchanged are a success");
		result.Outcome.Should().Be(outcome, because: "the server's outcome is passed through");
		result.ErrorCode.Should().Be(success ? null : "stage-in-use", because: "the server's code is the contract");
		result.Case.DesignerUrl.Should().EndWith("#case/" + CaseUId, because: "the changed case opens in the designer");
		result.Diff.Moved.Should().ContainSingle(because: "the diff is passed through");
		result.RunningInstances.Total.Should().Be(1, because: "the running cases are passed through");
		result.Obligations.ReloadOpenPages.Should().BeTrue(because: "the obligations are passed through");
		result.Leftovers.Should().HaveCount(success ? 0 : 1, because: "what a failed restore left is passed through");
		result.Notes.Should().Equal(["Reload (F5) open pages."], because: "the notes are relayed");
		result.Warnings.Should().Equal(["warning"], because: "the warnings are relayed");
		_session.Received(1).Dispose();
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("A 404 to ModifyCase is refused as process-builder-without-case-change on a dry run and on a write alike: a missing route proves nothing ran.")]
	public void Modify_ShouldReportProcessBuilderWithoutCaseChange_WhenTheRouteIsMissing(bool dryRun) {
		// Arrange
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.RouteMissing, null, "Endpoint not found."));

		// Act
		ModifyDynamicCaseResult result = _service.Modify(ModifyOptions(dryRun));

		// Assert
		result.Outcome.Should().Be(DynamicCaseModifyOutcomes.Refused, because: "a missing operation wrote nothing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.ProcessBuilderWithoutCaseChange,
			because: "only a 404 proves the installed package lacks the operation");
		result.ErrorMessage.Should().Contain("install-process-builder", because: "the remedy is named");
	}

	[TestCase(DynamicCaseReplyKind.TransportFailure, "timeout")]
	[TestCase(DynamicCaseReplyKind.NotAnEnvelope, "<html>")]
	[Description("A write whose answer could not be read is outcome unknown with request-failed, and the message sends the agent to a dry run before deciding.")]
	public void Modify_ShouldReportUnknown_WhenAWriteReplyCannotBeRead(DynamicCaseReplyKind kind, string detail) {
		// Arrange
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(kind, null, detail));

		// Act
		ModifyDynamicCaseResult result = _service.Modify(ModifyOptions());

		// Assert
		result.Outcome.Should().Be(DynamicCaseModifyOutcomes.Unknown, because: "the write may have been saved");
		result.Success.Should().BeFalse(because: "an unknown outcome is not a success");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the answer could not be read");
		result.ErrorMessage.Should().Contain("UNKNOWN").And.Contain("dry-run=true",
			because: "only a dry run tells what is still to change");
	}

	[Test]
	[Description("On a dry run an unreadable answer is refused with request-failed, not unknown: a dry run writes nothing.")]
	public void Modify_ShouldReportRequestFailed_WhenADryRunReplyCannotBeRead() {
		// Arrange
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.TransportFailure, null, "timeout"));

		// Act
		ModifyDynamicCaseResult result = _service.Modify(ModifyOptions(dryRun: true));

		// Assert
		result.Outcome.Should().Be(DynamicCaseModifyOutcomes.Refused, because: "a dry run writes nothing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the answer could not be read");
		result.ErrorMessage.Should().Contain("nothing was written", because: "the caller must know nothing changed");
	}

	[Test]
	[Description("A request WCF could not read is refused with request-failed even on a write: no operation ran, so the outcome is known.")]
	public void Modify_ShouldReportRequestFailed_WhenTheServerCannotReadTheRequest() {
		// Arrange
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.NotAnEnvelope, null,
			"The formatter threw an exception while trying to deserialize the message"));

		// Act
		ModifyDynamicCaseResult result = _service.Modify(ModifyOptions());

		// Assert
		result.Outcome.Should().Be(DynamicCaseModifyOutcomes.Refused, because: "an unread request ran nothing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the request could not be read");
		result.ErrorMessage.Should().Contain("get-tool-contract modify-dynamic-case",
			because: "the arguments are what to check");
	}

	[Test]
	[Description("A filtered create carries the filter and the user's yes to deactivating onto both passes, and the result passes the case filter, the settings row's filter column and the deactivated cases through.")]
	public void Create_ShouldCarryTheFilter_AndPassItsAnswerThrough() {
		// Arrange
		DynamicCaseReply<BuildCaseResponse> answer(string outcome) => new(DynamicCaseReplyKind.Envelope,
			new BuildCaseResponse {
				Success = true, Outcome = outcome,
				Case = new BuildCaseCaseInfo {
					Status = outcome, SchemaName = "UsrOrderB2BCase", SchemaUId = CaseUId, Stages = [],
					Filter = new DynamicCaseFilter { Column = "UsrType", Values = [new DynamicCaseFilterValue { Value = "1", Caption = "B2B" }] }
				},
				Settings = new DynamicCaseSettings { Status = "reused", FilterColumnName = "UsrType", FilterColumnSet = true },
				DeactivatedCases = ["UsrOrderCase"],
				RequiredPackages = [], MissingDependencies = []
			}, null);
		Passes(answer("planned"), answer("created"));
		CreateDynamicCaseOptions options = Options(addProgressBar: false);
		options.Filter = new DynamicCaseFilterArgument("UsrType", ["B2B"], false);
		options.DeactivateUnfiltered = true;

		// Act
		DynamicCaseResult result = _service.Create(options);

		// Assert
		_session.Received(2).Build(Arg.Is<BuildCaseRequest>(request =>
			request.Filter.Column == "UsrType" && request.Filter.Values[0] == "B2B" && request.Filter.Empty == null
			&& request.DeactivateUnfiltered == true));
		result.Case.Filter.Values[0].Caption.Should().Be("B2B", because: "the case filter is shown");
		result.Settings.FilterColumnSet.Should().BeTrue(because: "the settings row got the filter column");
		result.DeactivatedCases.Should().Equal(["UsrOrderCase"], because: "the disabled case is named");
	}

	[Test]
	[Description("A create without a filter sends no filter and no deactivateUnfiltered, as before.")]
	public void Create_ShouldSendNoFilter_WhenTheCallNamesNone() {
		// Arrange
		Passes(Envelope("planned"), Envelope("created"));

		// Act
		_service.Create(Options(addProgressBar: false));

		// Assert
		_session.Received(2).Build(Arg.Is<BuildCaseRequest>(request =>
			request.Filter == null && request.DeactivateUnfiltered == null));
	}

	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(false, false)]
	[Description("A modify sends a new filter, a removal as a patch without a filter, or no filter key at all; the settings block of the answer is passed through.")]
	public void Modify_ShouldMapTheFilterChange(bool remove, bool set) {
		// Arrange
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(new DynamicCaseReply<ModifyCaseResponse>(
			DynamicCaseReplyKind.Envelope, new ModifyCaseResponse {
				Success = true, Outcome = "planned",
				Settings = new DynamicCaseSettings { FilterColumnName = "UsrType", FilterColumnSet = true }
			}, null));
		ModifyDynamicCaseOptions options = ModifyOptions(dryRun: true);
		options.RemoveFilter = remove;
		options.Filter = set ? new DynamicCaseFilterArgument("UsrType", [], true) : null;

		// Act
		ModifyDynamicCaseResult result = _service.Modify(options);

		// Assert
		if (remove) {
			_session.Received(1).Modify(Arg.Is<ModifyCaseRequest>(request => request.Filter != null && request.Filter.NewFilter == null));
		} else if (set) {
			_session.Received(1).Modify(Arg.Is<ModifyCaseRequest>(request =>
				request.Filter.NewFilter.Column == "UsrType" && request.Filter.NewFilter.Empty == true));
		} else {
			_session.Received(1).Modify(Arg.Is<ModifyCaseRequest>(request => request.Filter == null));
		}
		result.Settings.FilterColumnName.Should().Be("UsrType", because: "the settings block is passed through");
	}

	private const string VersionUId = "9a1b2c3d-0000-4000-8000-000000000001";
	private const string DesignerPrefix = "http://sandbox/0/Nui/ViewModule.aspx?vm=DcmDesigner#case/";

	private static ModifyDynamicCaseAsNewVersionOptions NewVersionOptions(bool dryRun = false) => new() {
		Environment = Env, CaseName = "UsrOrderCase", DryRun = dryRun
	};

	private static SetActiveDynamicCaseVersionOptions ActivationOptions(bool dryRun = false) => new() {
		Environment = Env, VersionName = "UsrOrderCaseCustom1", DryRun = dryRun
	};

	private static CaseVersionInfo NewVersion(bool active = false) => new() {
		Name = "UsrOrderCaseCustom1", UId = VersionUId, Version = 2, PackageName = "UsrOrdersV2",
		RootName = "UsrOrderCase", Active = active
	};

	private static CaseVersionInfo RootVersion() => new() {
		Name = "UsrOrderCase", UId = CaseUId, Version = 1, PackageName = Package, RootName = "UsrOrderCase",
		Active = true
	};

	private static CaseVersionMemberInfo Member(string name, string uId, int version, bool active) => new() {
		Name = name, UId = uId, Version = version, PackageName = Package, PackageReadOnly = !active,
		Active = active, RunningInstances = version * 3, HasLinkRow = active, IsDelivered = !active
	};

	private static ModifyCaseAsNewVersionResponse NewVersionResponse(string outcome, bool success) => new() {
		Success = success,
		Outcome = outcome,
		ErrorCode = "code-" + outcome,
		ErrorMessage = "message of " + outcome,
		Version = NewVersion(),
		Case = new BuildCaseCaseInfo {
			Status = "existing", SchemaName = "UsrOrderCaseCustom1", SchemaUId = VersionUId, PackageName = "UsrOrdersV2",
			EntitySchemaName = Entity, StageColumnName = "UsrStatus", Caption = "Orders v2",
			Stages = [new DynamicCaseStage { Name = "Stage1", Value = "v1", Caption = "New" }],
			Filter = new DynamicCaseFilter { Column = "UsrType" },
			Versions = [Member("UsrOrderCase", CaseUId, 1, true), Member("UsrOrderCaseCustom1", VersionUId, 2, false)]
		},
		SourceName = "UsrOrderCase",
		Diff = new DynamicCaseDiff { Added = [new DynamicCaseStageChange { Value = "v3", Caption = "Paid" }] },
		Settings = new DynamicCaseSettings { Status = "added", SysDcmSettingsId = "s1", Bindings = ["UsrOrdersV2"] },
		Obligations = new DynamicCaseObligations { ReloadOpenPages = true },
		Leftovers = ["UsrOrderCaseCustom1 schema"],
		Notes = ["note"],
		Warnings = ["warning"]
	};

	private void AnswerNewVersion(ModifyCaseAsNewVersionResponse response) =>
		_session.ModifyAsNewVersion(Arg.Any<ModifyCaseAsNewVersionRequest>()).Returns(
			new DynamicCaseReply<ModifyCaseAsNewVersionResponse>(DynamicCaseReplyKind.Envelope, response, null));

	private void AnswerActivation(SetActiveCaseVersionResponse response) =>
		_session.SetActiveVersion(Arg.Any<SetActiveCaseVersionRequest>()).Returns(
			new DynamicCaseReply<SetActiveCaseVersionResponse>(DynamicCaseReplyKind.Envelope, response, null));

	private static IEnumerable<string> NullMembers(object value) =>
		value.GetType().GetProperties()
			.Where(property => !property.PropertyType.IsValueType && property.GetValue(value) is null)
			.Select(property => property.Name);

	[Test]
	[Description("A new-version call maps the call onto the request: the identity trimmed, the package, every stage member, the caption, the user's yes as true and dry-run as validateOnly, through one session.")]
	public void ModifyAsNewVersion_ShouldMapTheCallOntoTheRequest() {
		// Arrange
		AnswerNewVersion(new ModifyCaseAsNewVersionResponse { Success = true, Outcome = "planned" });
		ModifyDynamicCaseAsNewVersionOptions options = NewVersionOptions(dryRun: true);
		options.CaseName = " UsrOrderCase ";
		options.CaseUId = " ";
		options.EntitySchemaName = " UsrOrder ";
		options.PackageName = " UsrOrdersV2 ";
		options.Caption = " Orders v2 ";
		options.Stages = [
			new DynamicCaseStageItem("New") { GroupSent = true, Group = null },
			new DynamicCaseStageItem("Done") { Caption = "Closed", Color = "#FF0000", Successful = false }
		];
		options.ConfirmStageRecords = true;
		options.ConfirmValueRename = true;

		// Act
		_service.ModifyAsNewVersion(options);

		// Assert
		_session.Received(1).ModifyAsNewVersion(Arg.Is<ModifyCaseAsNewVersionRequest>(request =>
			request.CaseName == "UsrOrderCase" && request.CaseUId == null && request.EntitySchemaName == "UsrOrder"
			&& request.PackageName == "UsrOrdersV2" && request.Caption == "Orders v2"
			&& request.ValidateOnly && request.ConfirmStageRecords == true && request.ConfirmValueRename == true
			&& request.Filter == null
			&& request.Stages.Count == 2
			&& request.Stages[0].Value == "New" && request.Stages[0].GroupSent && request.Stages[0].Group == null
			&& !request.Stages[1].GroupSent && request.Stages[1].Caption == "Closed"
			&& request.Stages[1].Color == "#FF0000" && request.Stages[1].Successful == false));
		_client.Received(1).OpenSession(Env);
		_session.Received(1).Dispose();
	}

	[Test]
	[Description("A new-version call that names only the case sends no package, stages, caption or filter, leaves a yes not given out of the request rather than false, and writes.")]
	public void ModifyAsNewVersion_ShouldLeaveOutWhatTheCallDoesNotName() {
		// Arrange
		AnswerNewVersion(new ModifyCaseAsNewVersionResponse { Success = true, Outcome = "version-created" });

		// Act
		_service.ModifyAsNewVersion(NewVersionOptions());

		// Assert
		_session.Received(1).ModifyAsNewVersion(Arg.Is<ModifyCaseAsNewVersionRequest>(request =>
			request.CaseName == "UsrOrderCase" && request.PackageName == null && request.Stages == null
			&& request.Caption == null && request.Filter == null
			&& request.ConfirmStageRecords == null && request.ConfirmValueRename == null && !request.ValidateOnly));
	}

	[TestCase(true, false)]
	[TestCase(false, true)]
	[Description("A new-version call sends a removal as a patch without a filter (JSON null on the wire) and a new filter as a patch carrying it.")]
	public void ModifyAsNewVersion_ShouldMapTheFilterChange(bool remove, bool set) {
		// Arrange
		AnswerNewVersion(new ModifyCaseAsNewVersionResponse { Success = true, Outcome = "planned" });
		ModifyDynamicCaseAsNewVersionOptions options = NewVersionOptions(dryRun: true);
		options.RemoveFilter = remove;
		options.Filter = set ? new DynamicCaseFilterArgument("UsrType", ["B2B"], false) : null;

		// Act
		_service.ModifyAsNewVersion(options);

		// Assert
		if (remove) {
			_session.Received(1).ModifyAsNewVersion(Arg.Is<ModifyCaseAsNewVersionRequest>(request =>
				request.Filter != null && request.Filter.NewFilter == null));
		} else {
			_session.Received(1).ModifyAsNewVersion(Arg.Is<ModifyCaseAsNewVersionRequest>(request =>
				request.Filter.NewFilter.Column == "UsrType" && request.Filter.NewFilter.Values[0] == "B2B"));
		}
	}

	[Test]
	[Description("The new-version result maps every field of the server's answer - nothing reference-typed is left null when the server sent it - and each field carries the server's value.")]
	public void ModifyAsNewVersion_ShouldMapEveryFieldOfTheAnswer() {
		// Arrange
		ModifyCaseAsNewVersionResponse response = NewVersionResponse("version-created", success: true);
		AnswerNewVersion(response);

		// Act
		ModifyDynamicCaseAsNewVersionResult result = _service.ModifyAsNewVersion(NewVersionOptions());

		// Assert
		NullMembers(result).Should().BeEmpty(because: "every field of a complete answer reaches the result");
		NullMembers(result.Version).Should().BeEmpty(because: "every field of the version block reaches the result");
		result.Success.Should().BeTrue(because: "version-created is a success");
		result.Outcome.Should().Be("version-created", because: "the server's outcome is passed through");
		result.ErrorCode.Should().Be("code-version-created", because: "the server's code is passed through");
		result.ErrorMessage.Should().Be("message of version-created", because: "the server's text is passed through");
		result.Version.Name.Should().Be("UsrOrderCaseCustom1", because: "the version's name is mapped");
		result.Version.UId.Should().Be(VersionUId, because: "the version's UId is mapped");
		result.Version.Version.Should().Be(2, because: "the stored version number is mapped");
		result.Version.PackageName.Should().Be("UsrOrdersV2", because: "the version's package is mapped");
		result.Version.RootName.Should().Be("UsrOrderCase", because: "the family root is mapped");
		result.Version.Active.Should().BeFalse(because: "a new version is always inactive");
		result.Case.SchemaName.Should().Be("UsrOrderCaseCustom1", because: "the version as a case is mapped");
		result.Case.PackageName.Should().Be("UsrOrdersV2", because: "the case block's package is mapped");
		result.Case.Caption.Should().Be("Orders v2", because: "the case caption is mapped");
		result.Case.Filter.Column.Should().Be("UsrType", because: "the case filter is mapped");
		result.Case.Versions.Select(member => member.Name).Should().Equal(["UsrOrderCase", "UsrOrderCaseCustom1"],
			because: "the family listed in the case block is mapped in order");
		result.SourceName.Should().Be("UsrOrderCase", because: "the source member is named");
		result.Diff.Should().BeSameAs(response.Diff, because: "the diff is passed through");
		result.Settings.Should().BeSameAs(response.Settings, because: "the link row block is passed through");
		result.Obligations.Should().BeSameAs(response.Obligations, because: "the obligations are passed through");
		result.Leftovers.Should().Equal(["UsrOrderCaseCustom1 schema"], because: "the leftovers are passed through");
		result.Notes.Should().Equal(["note"], because: "the notes are relayed");
		result.Warnings.Should().Equal(["warning"], because: "the warnings are relayed");
	}

	[Test]
	[Description("The result's version blocks inherit the server's fields, so every server field must be MAPPED: each one is set to a value that is not its default, and the result must carry it, or a field added to the wire type for reading would reach the agent at its default (false, 0) as a fact.")]
	public void VersionBlocks_ShouldMapEveryServerField() {
		// Arrange
		ModifyCaseAsNewVersionResponse created = NewVersionResponse("version-created", true);
		CaseVersionInfo version = Filled<CaseVersionInfo>("version");
		CaseVersionMemberInfo member = Filled<CaseVersionMemberInfo>("member");
		CaseVersionInfo previous = Filled<CaseVersionInfo>("previous");
		AnswerNewVersion(Rebuilt(created, version, member));
		AnswerActivation(new SetActiveCaseVersionResponse {
			Success = true, Outcome = "activated", ActiveVersion = version, PreviousActiveVersion = previous
		});

		// Act
		ModifyDynamicCaseAsNewVersionResult saved = _service.ModifyAsNewVersion(NewVersionOptions());
		SetActiveDynamicCaseVersionResult switched = _service.SetActiveVersion(ActivationOptions());

		// Assert
		using (new AssertionScope()) {
			foreach (System.Reflection.PropertyInfo property in typeof(CaseVersionInfo).GetProperties()) {
				property.GetValue(saved.Version).Should().Be(property.GetValue(version),
					because: $"the new version's '{property.Name}' is a server field the mapping must carry");
				property.GetValue(switched.ActiveVersion).Should().Be(property.GetValue(version),
					because: $"the active version's '{property.Name}' is a server field the mapping must carry");
				property.GetValue(switched.PreviousActiveVersion).Should().Be(property.GetValue(previous),
					because: $"the previous version's '{property.Name}' is a server field the mapping must carry");
			}
			foreach (System.Reflection.PropertyInfo property in typeof(CaseVersionMemberInfo).GetProperties()) {
				property.GetValue(saved.Case.Versions.Single()).Should().Be(property.GetValue(member),
					because: $"a family member's '{property.Name}' is a server field the mapping must carry");
			}
		}
	}

	// Every property set to a value that differs from its default and from the same property of another instance.
	private static T Filled<T>(string tag) where T : new() {
		T value = new();
		int index = 0;
		foreach (System.Reflection.PropertyInfo property in typeof(T).GetProperties()) {
			index++;
			object filled = property.PropertyType == typeof(string) ? $"{tag}-{property.Name}"
				: property.PropertyType == typeof(int) ? index + 40
				: property.PropertyType == typeof(bool) ? true
				: throw new InvalidOperationException($"No filler for {property.PropertyType} '{property.Name}'.");
			property.SetValue(value, filled);
		}
		return value;
	}

	private static ModifyCaseAsNewVersionResponse Rebuilt(ModifyCaseAsNewVersionResponse response,
		CaseVersionInfo version, CaseVersionMemberInfo member) => new() {
		Success = response.Success, Outcome = response.Outcome, ErrorCode = response.ErrorCode,
		ErrorMessage = response.ErrorMessage, Version = version,
		Case = new BuildCaseCaseInfo {
			Status = response.Case.Status, SchemaName = response.Case.SchemaName, SchemaUId = response.Case.SchemaUId,
			PackageName = response.Case.PackageName, EntitySchemaName = response.Case.EntitySchemaName,
			StageColumnName = response.Case.StageColumnName, Caption = response.Case.Caption,
			Stages = response.Case.Stages, Filter = response.Case.Filter, Versions = [member]
		},
		SourceName = response.SourceName, Diff = response.Diff, Settings = response.Settings,
		Obligations = response.Obligations, Leftovers = response.Leftovers, Notes = response.Notes,
		Warnings = response.Warnings
	};

	[TestCase("version-created", true, true)]
	[TestCase("failed-leftover", false, true)]
	[TestCase("planned", true, false)]
	[TestCase("failed-rolled-back", false, false)]
	[TestCase("refused", false, false)]
	[Description("Only a saved version gets a designer link, on the version and on the case block: version-created and failed-leftover; a dry run's UId names nothing yet and a rolled-back or refused one does not exist.")]
	public void ModifyAsNewVersion_ShouldLinkTheDesigner_OnlyForASavedVersion(string outcome, bool success,
		bool linked) {
		// Arrange
		AnswerNewVersion(NewVersionResponse(outcome, success));

		// Act
		ModifyDynamicCaseAsNewVersionResult result = _service.ModifyAsNewVersion(NewVersionOptions());

		// Assert
		result.Success.Should().Be(success, because: "only planned and version-created are a success");
		if (linked) {
			result.Version.DesignerUrl.Should().Be(DesignerPrefix + VersionUId,
				because: "a saved version opens in the designer by its UId");
			result.Case.DesignerUrl.Should().Be(DesignerPrefix + VersionUId,
				because: "the version as a case opens in the designer too");
		} else {
			result.Version.DesignerUrl.Should().BeNull(because: "a version that was not saved has nothing to open");
			result.Case.DesignerUrl.Should().BeNull(because: "a case that was not saved has nothing to open");
		}
		result.Case.Versions.Should().OnlyContain(
			member => member.DesignerUrl == (member.HasLinkRow ? DesignerPrefix + member.UId : null),
			because: "every member the family lists exists, but the designer opens only a member with its own link row");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("A new-version write answered without an outcome: a failed one is refused, so a refusal never passes as a write, and a successful one is unknown, because the version may have been saved and a blind retry makes another one.")]
	public void ModifyAsNewVersion_ShouldReadAMissingOutcome_FromTheSuccessFlag(bool success) {
		// Arrange
		AnswerNewVersion(new ModifyCaseAsNewVersionResponse { Success = success, ErrorCode = "x", Version = NewVersion() });

		// Act
		ModifyDynamicCaseAsNewVersionResult result = _service.ModifyAsNewVersion(NewVersionOptions());

		// Assert
		result.Outcome.Should().Be(success ? DynamicCaseVersionOutcomes.Unknown : DynamicCaseVersionOutcomes.Refused,
			because: "a failed answer without an outcome is a refusal, and a successful one may have written");
		if (success) {
			result.ErrorMessage.Should().Contain("Do NOT retry yet").And.Contain("describe-dynamic-case",
				because: "a retry of a write whose result is unknown would make one more version");
		}
		result.Success.Should().BeFalse(because: "an answer that names no success outcome is never a success");
		result.Version.DesignerUrl.Should().BeNull(because: "nothing says the version was saved");
		result.Leftovers.Should().BeEmpty(because: "absent lists are answered empty");
		result.Notes.Should().BeEmpty(because: "absent lists are answered empty");
		result.Warnings.Should().BeEmpty(because: "absent lists are answered empty");
	}

	[TestCase(false, DynamicCaseVersionOutcomes.Unknown)]
	[TestCase(true, DynamicCaseVersionOutcomes.Refused)]
	[Description("An outcome clio does not know (a newer package) is unknown on a write and refused on a dry run, never a success and never a plain refusal of a write that may have saved a version.")]
	public void ModifyAsNewVersion_ShouldReadAnUnknownOutcome_AsUnknownOnAWrite(bool dryRun, string expected) {
		// Arrange
		AnswerNewVersion(new ModifyCaseAsNewVersionResponse {
			Success = true, Outcome = "version-created-elsewhere", Version = NewVersion()
		});

		// Act
		ModifyDynamicCaseAsNewVersionResult result = _service.ModifyAsNewVersion(NewVersionOptions(dryRun));

		// Assert
		result.Outcome.Should().Be(expected, because: "only a dry run is known to have written nothing");
		result.Success.Should().BeFalse(because: "an outcome clio cannot read is never a success");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the answer could not be read");
		result.ErrorMessage.Should().Contain("version-created-elsewhere", because: "the unread outcome is named");
		result.Version.DesignerUrl.Should().BeNull(because: "nothing says the version was saved");
	}

	[TestCase(false, DynamicCaseActivationOutcomes.Unknown)]
	[TestCase(true, DynamicCaseActivationOutcomes.Refused)]
	[Description("An activation outcome clio does not know is unknown on a write and refused on a dry run, never a success.")]
	public void SetActiveVersion_ShouldReadAnUnknownOutcome_AsUnknownOnAWrite(bool dryRun, string expected) {
		// Arrange
		AnswerActivation(new SetActiveCaseVersionResponse { Success = true, Outcome = "switched" });

		// Act
		SetActiveDynamicCaseVersionResult result = _service.SetActiveVersion(ActivationOptions(dryRun));

		// Assert
		result.Outcome.Should().Be(expected, because: "only a dry run is known to have written nothing");
		result.Success.Should().BeFalse(because: "an outcome clio cannot read is never a success");
		result.ErrorMessage.Should().Contain("switched", because: "the unread outcome is named");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("A 404 to ModifyCaseAsNewVersion is refused as process-builder-without-case-change on a dry run and on a write alike: a missing route proves nothing ran.")]
	public void ModifyAsNewVersion_ShouldReportProcessBuilderWithoutCaseChange_WhenTheRouteIsMissing(bool dryRun) {
		// Arrange
		_session.ModifyAsNewVersion(Arg.Any<ModifyCaseAsNewVersionRequest>()).Returns(
			new DynamicCaseReply<ModifyCaseAsNewVersionResponse>(DynamicCaseReplyKind.RouteMissing, null,
				"Endpoint not found."));

		// Act
		ModifyDynamicCaseAsNewVersionResult result = _service.ModifyAsNewVersion(NewVersionOptions(dryRun));

		// Assert
		result.Success.Should().BeFalse(because: "the operation is missing");
		result.Outcome.Should().Be(DynamicCaseVersionOutcomes.Refused, because: "a missing operation wrote nothing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.ProcessBuilderWithoutCaseChange,
			because: "only a 404 proves the installed package lacks the operation");
		result.ErrorMessage.Should().Contain("ModifyCaseAsNewVersion").And.Contain("install-process-builder")
			.And.Contain("Nothing was written.", because: "the operation, the remedy and the state are named");
		_session.Received(1).Dispose();
	}

	[Test]
	[Description("A request WCF could not read is refused with request-failed even on a write: no operation ran, so the outcome is known.")]
	public void ModifyAsNewVersion_ShouldReportRequestFailed_WhenTheServerCannotReadTheRequest() {
		// Arrange
		_session.ModifyAsNewVersion(Arg.Any<ModifyCaseAsNewVersionRequest>()).Returns(
			new DynamicCaseReply<ModifyCaseAsNewVersionResponse>(DynamicCaseReplyKind.NotAnEnvelope, null,
				"The formatter threw an exception while trying to deserialize the message"));

		// Act
		ModifyDynamicCaseAsNewVersionResult result = _service.ModifyAsNewVersion(NewVersionOptions());

		// Assert
		result.Outcome.Should().Be(DynamicCaseVersionOutcomes.Refused, because: "an unread request ran nothing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the request could not be read");
		result.ErrorMessage.Should().Contain("get-tool-contract modify-dynamic-case-as-new-version",
			because: "the arguments are what to check");
	}

	[TestCase(DynamicCaseReplyKind.TransportFailure, "timeout")]
	[TestCase(DynamicCaseReplyKind.NotAnEnvelope, "<html>")]
	[Description("A write whose answer could not be read is outcome unknown with request-failed, and the message forbids a blind retry and sends the agent to describe-dynamic-case first, because a retry makes one more version that is never removed.")]
	public void ModifyAsNewVersion_ShouldReportUnknown_WhenAWriteReplyCannotBeRead(DynamicCaseReplyKind kind,
		string detail) {
		// Arrange
		_session.ModifyAsNewVersion(Arg.Any<ModifyCaseAsNewVersionRequest>()).Returns(
			new DynamicCaseReply<ModifyCaseAsNewVersionResponse>(kind, null, detail));

		// Act
		ModifyDynamicCaseAsNewVersionResult result = _service.ModifyAsNewVersion(NewVersionOptions());

		// Assert
		result.Outcome.Should().Be(DynamicCaseVersionOutcomes.Unknown, because: "the version may have been saved");
		result.Success.Should().BeFalse(because: "an unknown outcome is not a success");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the answer could not be read");
		result.ErrorMessage.Should().Contain("UNKNOWN").And.Contain("Do NOT retry")
			.And.Contain("Run describe-dynamic-case on the case first").And.Contain(detail,
				because: "only the family's members tell whether the version was saved");
		result.ErrorMessage.Should().NotContain("dry-run=true",
			because: "every dry run plans one more version, so it cannot tell whether this one was saved");
		result.Leftovers.Should().BeEmpty(because: "nothing is known to be left");
		result.Notes.Should().BeEmpty(because: "there is no answer to relay");
		result.Warnings.Should().BeEmpty(because: "there is no answer to relay");
	}

	[Test]
	[Description("On a dry run an unreadable answer is refused with request-failed, not unknown: a dry run writes nothing.")]
	public void ModifyAsNewVersion_ShouldReportRequestFailed_WhenADryRunReplyCannotBeRead() {
		// Arrange
		_session.ModifyAsNewVersion(Arg.Any<ModifyCaseAsNewVersionRequest>()).Returns(
			new DynamicCaseReply<ModifyCaseAsNewVersionResponse>(DynamicCaseReplyKind.TransportFailure, null, "timeout"));

		// Act
		ModifyDynamicCaseAsNewVersionResult result = _service.ModifyAsNewVersion(NewVersionOptions(dryRun: true));

		// Assert
		result.Outcome.Should().Be(DynamicCaseVersionOutcomes.Refused, because: "a dry run writes nothing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the answer could not be read");
		result.ErrorMessage.Should().Contain("nothing was written").And.Contain("timeout",
			because: "the caller must know nothing changed, and why the call failed");
	}

	[Test]
	[Description("An activation maps the call onto the request: the member trimmed, a blank identity left out and dry-run as validateOnly, through one session.")]
	public void SetActiveVersion_ShouldMapTheCallOntoTheRequest() {
		// Arrange
		AnswerActivation(new SetActiveCaseVersionResponse { Success = true, Outcome = "planned" });
		SetActiveDynamicCaseVersionOptions byName = ActivationOptions(dryRun: true);
		byName.VersionName = " UsrOrderCaseCustom1 ";
		byName.VersionUId = " ";
		SetActiveDynamicCaseVersionOptions byUId = ActivationOptions();
		byUId.VersionName = null;
		byUId.VersionUId = " " + VersionUId + " ";

		// Act
		_service.SetActiveVersion(byName);
		_service.SetActiveVersion(byUId);

		// Assert
		_session.Received(1).SetActiveVersion(Arg.Is<SetActiveCaseVersionRequest>(request =>
			request.VersionName == "UsrOrderCaseCustom1" && request.VersionUId == null && request.ValidateOnly));
		_session.Received(1).SetActiveVersion(Arg.Is<SetActiveCaseVersionRequest>(request =>
			request.VersionName == null && request.VersionUId == VersionUId && !request.ValidateOnly));
		_session.Received(2).Dispose();
	}

	[Test]
	[Description("The activation result maps every field of the server's read-back and links the designer on both the active and the previously active member.")]
	public void SetActiveVersion_ShouldMapEveryFieldOfTheAnswer_WithTheDesignerLinks() {
		// Arrange
		SetActiveCaseVersionResponse response = new() {
			Success = true,
			Outcome = "activated",
			ErrorCode = "code",
			ErrorMessage = "message",
			ActiveVersion = NewVersion(active: true),
			PreviousActiveVersion = RootVersion(),
			SiblingsStillFlagged = ["UsrOrderCaseCustom2"],
			DecidedByPackagePosition = ["UsrOrderCaseVendor"],
			LinkRow = new DynamicCaseSettings { Status = "added", SysDcmSettingsId = "s2" },
			Obligations = new DynamicCaseObligations { ReloadOpenPages = true },
			Leftovers = ["link row"],
			Notes = ["note"],
			Warnings = ["warning"]
		};
		AnswerActivation(response);

		// Act
		SetActiveDynamicCaseVersionResult result = _service.SetActiveVersion(ActivationOptions());

		// Assert
		NullMembers(result).Should().BeEmpty(because: "every field of a complete read-back reaches the result");
		NullMembers(result.ActiveVersion).Should().BeEmpty(because: "every field of the active member is mapped");
		NullMembers(result.PreviousActiveVersion).Should().BeEmpty(because: "every field of the previous member is mapped");
		result.Success.Should().BeTrue(because: "activated is a success");
		result.Outcome.Should().Be("activated", because: "the server's outcome is passed through");
		result.ErrorCode.Should().Be("code", because: "the server's code is passed through");
		result.ErrorMessage.Should().Be("message", because: "the server's text is passed through");
		result.ActiveVersion.Name.Should().Be("UsrOrderCaseCustom1", because: "the active member is named");
		result.ActiveVersion.Version.Should().Be(2, because: "the stored version number is mapped");
		result.ActiveVersion.PackageName.Should().Be("UsrOrdersV2", because: "the member's package is mapped");
		result.ActiveVersion.RootName.Should().Be("UsrOrderCase", because: "the family root is mapped");
		result.ActiveVersion.Active.Should().BeTrue(because: "the read-back says it is active");
		result.ActiveVersion.DesignerUrl.Should().Be(DesignerPrefix + VersionUId,
			because: "the active member exists and opens in the designer");
		result.PreviousActiveVersion.Name.Should().Be("UsrOrderCase", because: "the previous member is named");
		result.PreviousActiveVersion.DesignerUrl.Should().Be(DesignerPrefix + CaseUId,
			because: "the previous member exists and opens in the designer");
		result.SiblingsStillFlagged.Should().Equal(["UsrOrderCaseCustom2"], because: "still-flagged siblings are named");
		result.DecidedByPackagePosition.Should().Equal(["UsrOrderCaseVendor"],
			because: "members in read-only packages are named");
		result.LinkRow.Should().BeSameAs(response.LinkRow, because: "the link row added for the member is passed through");
		result.Obligations.Should().BeSameAs(response.Obligations, because: "the obligations are passed through");
		result.Leftovers.Should().Equal(["link row"], because: "the leftovers are passed through");
		result.Notes.Should().Equal(["note"], because: "the notes are relayed");
		result.Warnings.Should().Equal(["warning"], because: "the warnings are relayed");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("An activation write answered without an outcome: a failed one is refused, a successful one is unknown; absent lists are answered empty, and absent members stay absent.")]
	public void SetActiveVersion_ShouldReadAMissingOutcome_FromTheSuccessFlag(bool success) {
		// Arrange
		AnswerActivation(new SetActiveCaseVersionResponse { Success = success, ErrorCode = "x" });

		// Act
		SetActiveDynamicCaseVersionResult result = _service.SetActiveVersion(ActivationOptions());

		// Assert
		result.Outcome.Should().Be(success ? DynamicCaseActivationOutcomes.Unknown : DynamicCaseActivationOutcomes.Refused,
			because: "a failed answer without an outcome is a refusal, and a successful one may have switched");
		result.Success.Should().BeFalse(because: "an answer that names no success outcome is never a success");
		result.ActiveVersion.Should().BeNull(because: "a member the server did not name is not invented");
		result.PreviousActiveVersion.Should().BeNull(because: "a member the server did not name is not invented");
		result.SiblingsStillFlagged.Should().BeEmpty(because: "absent lists are answered empty");
		result.DecidedByPackagePosition.Should().BeEmpty(because: "absent lists are answered empty");
		result.Leftovers.Should().BeEmpty(because: "absent lists are answered empty");
		result.Notes.Should().BeEmpty(because: "absent lists are answered empty");
		result.Warnings.Should().BeEmpty(because: "absent lists are answered empty");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("A 404 to SetActiveCaseVersion is refused as process-builder-without-case-change on a dry run and on a write alike.")]
	public void SetActiveVersion_ShouldReportProcessBuilderWithoutCaseChange_WhenTheRouteIsMissing(bool dryRun) {
		// Arrange
		_session.SetActiveVersion(Arg.Any<SetActiveCaseVersionRequest>()).Returns(
			new DynamicCaseReply<SetActiveCaseVersionResponse>(DynamicCaseReplyKind.RouteMissing, null,
				"Endpoint not found."));

		// Act
		SetActiveDynamicCaseVersionResult result = _service.SetActiveVersion(ActivationOptions(dryRun));

		// Assert
		result.Outcome.Should().Be(DynamicCaseActivationOutcomes.Refused, because: "a missing operation wrote nothing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.ProcessBuilderWithoutCaseChange,
			because: "only a 404 proves the installed package lacks the operation");
		result.ErrorMessage.Should().Contain("SetActiveCaseVersion").And.Contain("install-process-builder",
			because: "the operation and the remedy are named");
		_session.Received(1).Dispose();
	}

	[Test]
	[Description("A request WCF could not read is refused with request-failed even on a write: no switch ran.")]
	public void SetActiveVersion_ShouldReportRequestFailed_WhenTheServerCannotReadTheRequest() {
		// Arrange
		_session.SetActiveVersion(Arg.Any<SetActiveCaseVersionRequest>()).Returns(
			new DynamicCaseReply<SetActiveCaseVersionResponse>(DynamicCaseReplyKind.NotAnEnvelope, null,
				"The formatter threw an exception while trying to deserialize the message"));

		// Act
		SetActiveDynamicCaseVersionResult result = _service.SetActiveVersion(ActivationOptions());

		// Assert
		result.Outcome.Should().Be(DynamicCaseActivationOutcomes.Refused, because: "an unread request ran nothing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the request could not be read");
		result.ErrorMessage.Should().Contain("get-tool-contract set-active-dynamic-case-version",
			because: "the arguments are what to check");
	}

	[TestCase(DynamicCaseReplyKind.TransportFailure, "timeout")]
	[TestCase(DynamicCaseReplyKind.NotAnEnvelope, "<html>")]
	[Description("An activation whose answer could not be read is outcome unknown with request-failed, and the message sends the agent to describe-dynamic-case and says repeating the same activation is safe.")]
	public void SetActiveVersion_ShouldReportUnknown_WhenAWriteReplyCannotBeRead(DynamicCaseReplyKind kind,
		string detail) {
		// Arrange
		_session.SetActiveVersion(Arg.Any<SetActiveCaseVersionRequest>()).Returns(
			new DynamicCaseReply<SetActiveCaseVersionResponse>(kind, null, detail));

		// Act
		SetActiveDynamicCaseVersionResult result = _service.SetActiveVersion(ActivationOptions());

		// Assert
		result.Outcome.Should().Be(DynamicCaseActivationOutcomes.Unknown, because: "the switch may have taken");
		result.Success.Should().BeFalse(because: "an unknown outcome is not a success");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the answer could not be read");
		result.ErrorMessage.Should().Contain("UNKNOWN").And.Contain("Run describe-dynamic-case")
			.And.Contain("repeating the same activation is safe").And.Contain(detail,
				because: "an activation is idempotent, so a repeat answers unchanged once the member is active");
		result.SiblingsStillFlagged.Should().BeEmpty(because: "nothing is known about the family");
		result.DecidedByPackagePosition.Should().BeEmpty(because: "nothing is known about the family");
		result.Leftovers.Should().BeEmpty(because: "nothing is known to be left");
	}

	[Test]
	[Description("On a dry run an unreadable activation answer is refused with request-failed, not unknown: a dry run writes nothing.")]
	public void SetActiveVersion_ShouldReportRequestFailed_WhenADryRunReplyCannotBeRead() {
		// Arrange
		_session.SetActiveVersion(Arg.Any<SetActiveCaseVersionRequest>()).Returns(
			new DynamicCaseReply<SetActiveCaseVersionResponse>(DynamicCaseReplyKind.TransportFailure, null, "timeout"));

		// Act
		SetActiveDynamicCaseVersionResult result = _service.SetActiveVersion(ActivationOptions(dryRun: true));

		// Assert
		result.Outcome.Should().Be(DynamicCaseActivationOutcomes.Refused, because: "a dry run writes nothing");
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.RequestFailed, because: "the answer could not be read");
		result.ErrorMessage.Should().Contain("nothing was written", because: "the caller must know nothing changed");
	}

	[Test]
	[Description("A described member that its family lists without a link row gets no designer link on the case block either: the case designer opens only a member that has one.")]
	public void Describe_ShouldLeaveTheCaseLinkOut_WhenTheDescribedMemberHasNoLinkRow() {
		// Arrange
		BuildCaseCaseInfo draft = new() {
			Status = "existing", SchemaName = "UsrOrderCaseCustom1", SchemaUId = VersionUId,
			Stages = ExistingCase().Stages,
			Versions = [Member("UsrOrderCase", CaseUId, 1, true), Member("UsrOrderCaseCustom1", VersionUId, 2, false)]
		};
		_session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.Envelope, new DescribeCaseResponse { Success = true, Cases = [draft] }, null));

		// Act
		DescribeDynamicCaseResult result = _service.Describe(DescribeOptions());

		// Assert
		result.Cases[0].DesignerUrl.Should().BeNull(
			because: "the described member has no link row, so a link to it would not open");
		result.Cases[0].Versions[0].DesignerUrl.Should().Be(DesignerPrefix + CaseUId,
			because: "the member that has a link row keeps its link");
	}

	[TestCase("planned", false)]
	[TestCase("present", true)]
	[Description("An activation dry run that plans the target's link row gives the target no designer link (it has no row yet), while one whose target already has a row links it.")]
	public void SetActiveVersion_ShouldLinkTheTarget_OnlyWhenItHasALinkRow(string linkRowStatus, bool linked) {
		// Arrange
		AnswerActivation(new SetActiveCaseVersionResponse {
			Success = true, Outcome = "planned", ActiveVersion = NewVersion(active: true),
			PreviousActiveVersion = RootVersion(),
			LinkRow = new DynamicCaseSettings { Status = linkRowStatus, SysDcmSettingsId = "s2" }
		});

		// Act
		SetActiveDynamicCaseVersionResult result = _service.SetActiveVersion(ActivationOptions(dryRun: true));

		// Assert
		result.ActiveVersion.DesignerUrl.Should().Be(linked ? DesignerPrefix + VersionUId : null,
			because: "the case designer opens only a member that has its own link row");
		result.PreviousActiveVersion.DesignerUrl.Should().Be(DesignerPrefix + CaseUId,
			because: "the member that was active keeps its link");
	}

	[Test]
	[Description("A describe maps each case's version family with every member field and a designer link per member that has its own link row, and leaves versions absent for a case that lists none.")]
	public void Describe_ShouldMapTheVersions_WithADesignerLinkPerMember() {
		// Arrange
		BuildCaseCaseInfo withFamily = ExistingCase();
		BuildCaseCaseInfo family = new() {
			Status = withFamily.Status, SchemaName = withFamily.SchemaName, SchemaUId = withFamily.SchemaUId,
			Stages = withFamily.Stages,
			Versions = [Member("UsrOrderCase", CaseUId, 1, true), Member("UsrOrderCaseCustom1", VersionUId, 2, false)]
		};
		_session.Describe(Arg.Any<DescribeCaseRequest>()).Returns(new DynamicCaseReply<DescribeCaseResponse>(
			DynamicCaseReplyKind.Envelope, new DescribeCaseResponse {
				Success = true, Cases = [family, ExistingCase()]
			}, null));

		// Act
		DescribeDynamicCaseResult result = _service.Describe(DescribeOptions());

		// Assert
		List<DynamicCaseVersionMember> versions = result.Cases[0].Versions;
		versions.Should().HaveCount(2, because: "every member of the family is listed");
		NullMembers(versions[1]).Should().Equal(["DesignerUrl"],
			because: "every field of a member reaches the result, and only the link is left out for a member the designer cannot open");
		versions[0].Name.Should().Be("UsrOrderCase", because: "the member's name is mapped");
		versions[0].UId.Should().Be(CaseUId, because: "the member's UId is mapped");
		versions[0].Version.Should().Be(1, because: "the stored version number is mapped");
		versions[0].PackageName.Should().Be(Package, because: "the member's package is mapped");
		versions[0].PackageReadOnly.Should().BeFalse(because: "the member's package accepts changes");
		versions[0].Active.Should().BeTrue(because: "the platform's pick is mapped");
		versions[0].RunningInstances.Should().Be(3, because: "the member's running cases are mapped");
		versions[0].HasLinkRow.Should().BeTrue(because: "the member's link row is mapped");
		versions[0].IsDelivered.Should().BeFalse(because: "the stored IsDelivered is mapped");
		versions[1].PackageReadOnly.Should().BeTrue(because: "a read-only package is reported");
		versions[1].Active.Should().BeFalse(because: "an inactive member is reported");
		versions[1].RunningInstances.Should().Be(6, because: "each member's own running cases are mapped");
		versions[1].HasLinkRow.Should().BeFalse(because: "a member without a link row is reported");
		versions[1].IsDelivered.Should().BeTrue(because: "the stored IsDelivered is mapped");
		versions[0].DesignerUrl.Should().Be(DesignerPrefix + CaseUId, because: "a member with a link row opens in the designer");
		versions[1].DesignerUrl.Should().BeNull(because: "the case designer opens only a member that has its own link row");
		result.Cases[1].Versions.Should().BeNull(
			because: "a case that is the only member of its family lists no versions");
	}

	[Test]
	[Description("A modify passes the link row the server added for an inactive member through, and leaves it absent when the server sent none.")]
	public void Modify_ShouldPassTheLinkRowThrough() {
		// Arrange
		DynamicCaseSettings linkRow = new() { Status = "added", SysDcmSettingsId = "s2", Bindings = ["UsrOrders"] };
		_session.Modify(Arg.Any<ModifyCaseRequest>()).Returns(
			new DynamicCaseReply<ModifyCaseResponse>(DynamicCaseReplyKind.Envelope,
				new ModifyCaseResponse { Success = true, Outcome = "modified", Case = ExistingCase(), LinkRow = linkRow },
				null),
			new DynamicCaseReply<ModifyCaseResponse>(DynamicCaseReplyKind.Envelope,
				new ModifyCaseResponse { Success = true, Outcome = "modified", Case = ExistingCase() }, null));

		// Act
		ModifyDynamicCaseResult withLinkRow = _service.Modify(ModifyOptions());
		ModifyDynamicCaseResult withoutLinkRow = _service.Modify(ModifyOptions());

		// Assert
		withLinkRow.LinkRow.Should().BeSameAs(linkRow, because: "the designer needs the added link row to open the member");
		withoutLinkRow.LinkRow.Should().BeNull(because: "a member that had a link row gets none added");
	}
}
