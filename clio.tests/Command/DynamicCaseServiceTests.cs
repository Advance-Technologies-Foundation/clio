using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using FluentAssertions;
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
	private IDynamicCasePageStep _pageStep;
	private IPackageDependencyManager _dependencies;
	private ISettingsRepository _settings;
	private IServiceUrlBuilder _urlBuilder;
	private DynamicCaseService _service;

	[SetUp]
	public void Setup() {
		_client = Substitute.For<IBuildCaseClient>();
		_pageStep = Substitute.For<IDynamicCasePageStep>();
		_dependencies = Substitute.For<IPackageDependencyManager>();
		_settings = Substitute.For<ISettingsRepository>();
		EnvironmentSettings env = new() { Uri = "http://sandbox", IsNetCore = false };
		_settings.FindEnvironment(Env).Returns(env);
		_urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_urlBuilder.Build(Arg.Any<string>(), env)
			.Returns(call => "http://sandbox/0/" + call.ArgAt<string>(0));
		_service = new DynamicCaseService(_client, _pageStep, _dependencies, _settings, _urlBuilder);
		_pageStep.Plan(Env, Package, Entity, Arg.Any<string>()).Returns(PlannedPage());
		_pageStep.Write(Env, Arg.Any<DynamicCasePagePlan>()).Returns(new DynamicCasePageWrite(true, null, []));
	}

	[TearDown]
	public void TearDown() {
		_client.ClearReceivedCalls();
		_pageStep.ClearReceivedCalls();
		_dependencies.ClearReceivedCalls();
	}

	private static CreateDynamicCaseOptions Options(bool dryRun = false, bool addProgressBar = true,
		bool addPackageDependencies = true) =>
		new() {
			Environment = Env, PackageName = Package, EntitySchemaName = Entity, StageColumnName = "UsrStatus",
			DryRun = dryRun, AddProgressBar = addProgressBar, AddPackageDependencies = addPackageDependencies
		};

	private static DynamicCasePagePlan PlannedPage(string designPackage = Package) => new() {
		Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = Page,
		ResolvedBy = DynamicCaseRecordPageResolver.ResolvedByRelatedPageAddon, DesignPackageName = designPackage,
		Template = "PageWithTabsFreedomTemplate", Fragment = ProgressBarFragment.BuildBody(Page, Entity, string.Empty),
		ModelConfigJson = "{}"
	};

	private static BuildCaseReply Envelope(string outcome, bool success = true, string errorCode = null,
		List<string> missing = null, List<string> leftovers = null) =>
		new(BuildCaseReplyKind.Envelope, new BuildCaseResponse {
			Success = success,
			Outcome = outcome,
			ErrorCode = errorCode,
			ErrorMessage = errorCode is null ? null : $"{errorCode} happened",
			Case = new BuildCaseCaseInfo {
				Status = success ? outcome : null, SchemaName = "UsrOrderCase", SchemaUId = CaseUId,
				ConnectionCount = 6, Stages = [new DynamicCaseStage { Name = "Stage1", Color = "#0C8701" }]
			},
			RequiredPackages = ["UsrOrders", "CrtBase"],
			MissingDependencies = missing ?? [],
			Obligations = new DynamicCaseObligations {
				ReloadOpenPages = true,
				Artifacts = [new DynamicCaseArtifactObligation { Kind = "dcm" },
					new DynamicCaseArtifactObligation { Kind = "settings-data" }]
			},
			Leftovers = leftovers,
			Notes = ["No compile or restart is needed: a case is interpreted."]
		}, null);

	private void Passes(BuildCaseReply validate, BuildCaseReply build = null) {
		_client.Send(Env, Arg.Is<BuildCaseRequest>(request => request.ValidateOnly)).Returns(validate);
		_client.Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly))
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
			_client.Send(Env, Arg.Is<BuildCaseRequest>(request => request.ValidateOnly && request.AllowMissingDependencies));
			_pageStep.Plan(Env, Package, Entity, null);
			_client.Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly && !request.AllowMissingDependencies));
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
		_client.Received(1).Send(Env, Arg.Is<BuildCaseRequest>(request =>
			request.ValidateOnly && !request.AllowMissingDependencies));
		result.ErrorCode.Should().Be("dependencies-missing", because: "the server's refusal is passed through");
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesMissing,
			because: "the missing dependencies were not added");
		_dependencies.DidNotReceiveWithAnyArgs().AddDependencies(default!, default!);
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
		_pageStep.DidNotReceiveWithAnyArgs().Plan(default!, default!, default!, default!);
		_client.DidNotReceive().Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		_dependencies.DidNotReceiveWithAnyArgs().AddDependencies(default!, default!);
	}

	[Test]
	[Description("A page refusal is returned before anything is written: the dependencies are not added and the build pass is never called.")]
	public void Create_ShouldNeverCallTheBuildPass_WhenThePageIsRefused() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));
		_pageStep.Plan(Env, Package, Entity, null).Returns(new DynamicCasePagePlan {
			Status = DynamicCasePageStep.StatusRefused, ErrorCode = DynamicCaseErrorCodes.PageAppendRejected,
			ErrorMessage = "rejected", PageSchemaName = Page
		});

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageAppendRejected, because: "the page refusal is the answer");
		result.Outcome.Should().Be(DynamicCaseOutcomes.Refused, because: "nothing was written");
		_client.DidNotReceive().Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		_dependencies.DidNotReceiveWithAnyArgs().AddDependencies(default!, default!);
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
		_pageStep.Received(1).Plan(Env, Package, Entity, null);
		_dependencies.DidNotReceiveWithAnyArgs().AddDependencies(default!, default!);
		_client.DidNotReceive().Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		_pageStep.DidNotReceiveWithAnyArgs().Write(default!, default!);
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
			_dependencies.AddDependencies(Package, Arg.Is<IEnumerable<PackageDependencySpec>>(specs =>
				specs.Select(spec => spec.Name).SequenceEqual(new[] { "CrtBase", "CrtCoreBase" })));
			_client.Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		});
		result.Dependencies.Status.Should().Be(DynamicCaseService.DependenciesAdded, because: "the add took");
		result.Dependencies.Added.Should().Equal(["CrtBase", "CrtCoreBase"], because: "the added list is reported");
	}

	[Test]
	[Description("A dependency add the storage refuses with validation errors is dependency-cycle, and the build pass is never called.")]
	public void Create_ShouldRefuseWithDependencyCycle_WhenTheStorageRefusesTheAdd() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]));
		_dependencies.AddDependencies(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new InvalidOperationException("Failed to save. Validation errors: CrtBase/x: cycle"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.DependencyCycle, because: "a storage validation error is a cycle");
		result.Outcome.Should().Be(DynamicCaseOutcomes.Refused, because: "the case was not built");
		_client.DidNotReceive().Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
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
		result.Page.Status.Should().Be(DynamicCaseService.PageInserted, because: "the bar was written");
	}

	[TestCase(BuildCaseReplyKind.RouteMissing)]
	[TestCase(BuildCaseReplyKind.NotAnEnvelope)]
	[Description("A 404 or a non-envelope reply on the validate pass is process-builder-without-build-case: the installed package passed the floor and still has no BuildCase.")]
	public void Create_ShouldReportProcessBuilderWithoutBuildCase_WhenTheValidatePassFindsNoOperation(
		BuildCaseReplyKind kind) {
		// Arrange
		Passes(new BuildCaseReply(kind, null, "Endpoint not found."));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.ErrorCode.Should().Be(DynamicCaseErrorCodes.ProcessBuilderWithoutBuildCase,
			because: "the package that answered does not carry the operation");
		result.ErrorMessage.Should().Contain("install-process-builder", because: "the remedy is named");
		result.ErrorMessage.Should().Contain("Nothing was written", because: "the validate pass writes nothing");
		_pageStep.DidNotReceiveWithAnyArgs().Plan(default!, default!, default!, default!);
	}

	[Test]
	[Description("An unreadable reply after the build pass is outcome unknown, worded so the caller re-reads before acting, and the added dependencies are left in place.")]
	public void Create_ShouldReportUnknown_WhenTheBuildPassReplyIsUnreadable() {
		// Arrange
		Passes(Envelope("planned", missing: ["CrtBase"]),
			new BuildCaseReply(BuildCaseReplyKind.NotAnEnvelope, null, "<html>"));

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
	[Description("add-progress-bar=false never touches the page and answers created-page-skipped.")]
	public void Create_ShouldSkipThePage_WhenTheBarIsNotWanted() {
		// Arrange
		Passes(Envelope("planned"));

		// Act
		DynamicCaseResult result = _service.Create(Options(addProgressBar: false));

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.CreatedPageSkipped, because: "only the case was asked for");
		result.Page.Status.Should().Be(DynamicCaseService.PageSkipped, because: "the page step was skipped");
		_pageStep.DidNotReceiveWithAnyArgs().Plan(default!, default!, default!, default!);
	}

	[Test]
	[Description("An equivalent case and a page that already has the bar answer already-present and write nothing to the page.")]
	public void Create_ShouldAnswerAlreadyPresent_WhenTheCaseAndTheBarExist() {
		// Arrange
		Passes(Envelope("already-present"), Envelope("already-present"));
		_pageStep.Plan(Env, Package, Entity, null).Returns(new DynamicCasePagePlan {
			Status = DynamicCasePageStep.StatusAlreadyPresent, PageSchemaName = Page
		});

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Outcome.Should().Be(DynamicCaseOutcomes.AlreadyPresent, because: "the equivalent case exists");
		result.Success.Should().BeTrue(because: "a safe re-run is a success");
		result.Page.Status.Should().Be(DynamicCasePageStep.StatusAlreadyPresent, because: "the bar is there");
		_pageStep.DidNotReceiveWithAnyArgs().Write(default!, default!);
	}

	[Test]
	[Description("A bar written outside the case's package adds the note to deploy both packages together.")]
	public void Create_ShouldNoteTheSecondPackage_WhenThePageIsWrittenOutsideTheCasePackage() {
		// Arrange
		Passes(Envelope("planned"));
		_pageStep.Plan(Env, Package, Entity, null).Returns(PlannedPage(designPackage: "Custom"));

		// Act
		DynamicCaseResult result = _service.Create(Options());

		// Assert
		result.Notes.Should().Contain(note => note.Contains("Deploy 'Custom' together with 'UsrOrders'"),
			because: "the case and its bar now live in two packages");
	}
}
