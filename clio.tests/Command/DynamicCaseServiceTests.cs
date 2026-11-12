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
		_pageStep.Plan(Env, Package, Entity, Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Returns(PlannedPage());
		_dependencies.AddDependenciesReportingCompilation(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Returns(call => new PackageDependencyAddResult(
				[.. call.Arg<IEnumerable<PackageDependencySpec>>().Select(spec => spec.Name)], false));
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
		Template = "PageWithTabsFreedomTemplate", Fragment = ProgressBarFragment.BuildBody(Page, Entity, string.Empty)
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
				ReloadOpenPages = false,
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
			_pageStep.Plan(Env, Package, Entity, null, Arg.Any<IReadOnlyList<string>>());
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
		_client.DidNotReceive().Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
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
		_client.DidNotReceive().Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
		_dependencies.DidNotReceiveWithAnyArgs().AddDependenciesReportingCompilation(default!, default!);
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
			_dependencies.AddDependenciesReportingCompilation(Package, Arg.Is<IEnumerable<PackageDependencySpec>>(specs =>
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
		_dependencies.AddDependenciesReportingCompilation(Package, Arg.Any<IEnumerable<PackageDependencySpec>>())
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
		Passes(new BuildCaseReply(BuildCaseReplyKind.TransportFailure, null, "timeout"));

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
		Passes(new BuildCaseReply(BuildCaseReplyKind.NotAnEnvelope, null,
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
		_client.DidNotReceive().Send(Env, Arg.Is<BuildCaseRequest>(request => !request.ValidateOnly));
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
		Passes(new BuildCaseReply(BuildCaseReplyKind.RouteMissing, null, "Endpoint not found."));

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
		Passes(new BuildCaseReply(BuildCaseReplyKind.NotAnEnvelope, null, "<html>Login</html>"));

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
			new BuildCaseReply(BuildCaseReplyKind.NotAnEnvelope, null, "<html>"));
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
		_client.ReceivedWithAnyArgs(1).Send(default!, default!);
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
}
