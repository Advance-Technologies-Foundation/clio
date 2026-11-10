using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class DynamicCasePageStepTests {

	private const string Env = "sandbox";
	private const string Package = "UsrOrders";
	private const string Entity = "UsrOrder";
	private const string Page = "UsrOrders_FormPage";

	private IDynamicCaseRecordPageResolver _resolver;
	private IProcessPageReader _pageReader;
	private IPageBodyWriter _writer;
	private IPageDataSourceReferenceValidator _validator;
	private DynamicCasePageStep _step;

	[SetUp]
	public void Setup() {
		_resolver = Substitute.For<IDynamicCaseRecordPageResolver>();
		_pageReader = Substitute.For<IProcessPageReader>();
		_writer = Substitute.For<IPageBodyWriter>();
		_validator = Substitute.For<IPageDataSourceReferenceValidator>();
		_validator.Validate(Arg.Any<string>(), Arg.Any<Func<string>>())
			.Returns(new SchemaValidationResult { IsValid = true });
		_resolver.Resolve(Package, Entity, Arg.Any<string>())
			.Returns(new RecordPageResolution([Page], DynamicCaseRecordPageResolver.ResolvedByRelatedPageAddon, null,
				null, []));
		_step = new DynamicCasePageStep(_resolver, _pageReader, _writer, _validator);
	}

	[TearDown]
	public void TearDown() {
		_resolver.ClearReceivedCalls();
		_pageReader.ClearReceivedCalls();
		_writer.ClearReceivedCalls();
		_validator.ClearReceivedCalls();
	}

	private void PageReads(PageBundleInfo bundle, string schemaType = "web", string designPackage = Package) {
		_pageReader.TryGetPage(Arg.Any<PageGetOptions>(), out Arg.Any<PageGetResponse>())
			.Returns(call => {
				call[1] = new PageGetResponse {
					Success = true,
					Page = new PageMetadataInfo {
						SchemaName = Page, SchemaType = schemaType, DesignPackageName = designPackage
					},
					Bundle = bundle
				};
				return true;
			});
	}

	private void Saves(bool dryRunSucceeds = true, bool writeSucceeds = true, string error = "rejected") {
		_writer.TryUpdatePage(Arg.Any<PageUpdateOptions>(), out Arg.Any<PageUpdateResponse>())
			.Returns(call => {
				PageUpdateOptions options = call.Arg<PageUpdateOptions>();
				bool ok = options.DryRun ? dryRunSucceeds : writeSucceeds;
				call[1] = new PageUpdateResponse { Success = ok, Error = ok ? null : error, DryRun = options.DryRun };
				return ok;
			});
	}

	[Test]
	[Description("A ready page is planned with the exact marker-wrapped fragment, after an append DRY RUN of that same fragment against the resolved page, and nothing is saved.")]
	public void Plan_ShouldDryRunTheExactFragment_AndPlanIt_WhenThePageIsReady() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()));
		Saves();

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.Status.Should().Be(DynamicCasePageStep.StatusPlanned, because: "every check passed");
		plan.Fragment.Should().Be(ProgressBarFragment.BuildBody(Page, Entity, string.Empty),
			because: "the fragment is the constant with the page and the object filled in");
		plan.ResolvedBy.Should().Be(DynamicCaseRecordPageResolver.ResolvedByRelatedPageAddon,
			because: "the resolution is reported");
		_writer.Received(1).TryUpdatePage(Arg.Is<PageUpdateOptions>(options =>
				options.DryRun && options.Mode == "append" && options.SchemaName == Page
				&& options.Body == plan.Fragment && options.Validate && options.Environment == Env),
			out Arg.Any<PageUpdateResponse>());
		_writer.DidNotReceive().TryUpdatePage(Arg.Is<PageUpdateOptions>(options => !options.DryRun),
			out Arg.Any<PageUpdateResponse>());
	}

	[Test]
	[Description("An append dry run that fails one of the save's own gates becomes page-append-rejected, so the caller hears it before the case is written.")]
	public void Plan_ShouldRefuse_WhenTheAppendDryRunIsRejected() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()));
		Saves(dryRunSucceeds: false, error: "parentName 'MainContainer' does not resolve");

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.Status.Should().Be(DynamicCasePageStep.StatusRefused, because: "the save would refuse the same body");
		plan.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageAppendRejected, because: "that is the dry-run refusal code");
		plan.ErrorMessage.Should().Contain("does not resolve", because: "the save's own reason is relayed");
	}

	[Test]
	[Description("A page that already carries a bar is planned as already-present and the dry run is not even sent.")]
	public void Plan_ShouldReportAlreadyPresent_WithoutADryRun_WhenTheBarIsThere() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView(new JsonObject {
			["name"] = "ProgressBar", ["type"] = "crt.EntityStageProgressBar"
		})));

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.Status.Should().Be(DynamicCasePageStep.StatusAlreadyPresent, because: "the page already shows the stages");
		plan.Fragment.Should().BeNull(because: "nothing is going to be written");
		_writer.DidNotReceiveWithAnyArgs().TryUpdatePage(default!, out _);
	}

	[Test]
	[Description("When the page already uses the fragment's names both are suffixed with the same _<8 hex>, so the append adds the bar instead of replacing the page's own elements.")]
	public void Plan_ShouldSuffixBothNames_WhenThePageAlreadyUsesThem() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView(new JsonObject {
			["name"] = "ProgressBar", ["type"] = "crt.Label"
		})));
		Saves();

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		List<string> names = ProgressBarPlacementTests.NamesIn(plan.Fragment);
		names.Should().HaveCount(2, because: "the fragment still holds the container and the bar");
		names[0].Should().MatchRegex("^ProgressBarContainer_[0-9a-f]{8}$", because: "the container is suffixed");
		names[1].Should().MatchRegex("^ProgressBar_[0-9a-f]{8}$", because: "the bar is suffixed");
		Regex.Match(names[0], "_[0-9a-f]{8}$").Value.Should().Be(Regex.Match(names[1], "_[0-9a-f]{8}$").Value,
			because: "one suffix keeps the pair recognisable");
	}

	[Test]
	[Description("A refusal from the record-page resolver is passed through with its candidates and no page is read.")]
	public void Plan_ShouldPassTheResolverRefusalThrough_WithoutReadingAPage() {
		// Arrange
		_resolver.Resolve(Package, "Activity", null).Returns(new RecordPageResolution([],
			DynamicCaseRecordPageResolver.ResolvedByRelatedPageAddon, DynamicCaseErrorCodes.RecordPageAmbiguous,
			"typed", [new DynamicCasePageCandidate { PageSchemaName = "Tasks_FormPage", TypeColumnValue = "t" }]));

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, "Activity", null);

		// Assert
		plan.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageAmbiguous, because: "the resolver decided it");
		plan.Candidates.Should().ContainSingle(candidate => candidate.PageSchemaName == "Tasks_FormPage",
			because: "the caller needs the choices to name one");
		_pageReader.DidNotReceiveWithAnyArgs().TryGetPage(default!, out _);
	}

	[Test]
	[Description("A page that cannot be read is record-page-not-found, and a page that is not a Freedom UI web page is record-page-not-freedom.")]
	public void Plan_ShouldRefuse_WhenThePageIsUnreadableOrNotAWebPage() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()), schemaType: "mobile");
		_pageReader.TryGetPage(Arg.Is<PageGetOptions>(options => options.SchemaName == "Missing_FormPage"),
				out Arg.Any<PageGetResponse>())
			.Returns(call => {
				call[1] = new PageGetResponse { Success = false, Error = "Schema not found" };
				return false;
			});
		_resolver.Resolve(Package, Entity, "Missing_FormPage").Returns(new RecordPageResolution(["Missing_FormPage"],
			DynamicCaseRecordPageResolver.ResolvedByExplicit, null, null, []));

		// Act
		DynamicCasePagePlan unreadable = _step.Plan(Env, Package, Entity, "Missing_FormPage");
		DynamicCasePagePlan mobile = _step.Plan(Env, Package, Entity, null);

		// Assert
		unreadable.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageNotFound, because: "the page could not be read");
		unreadable.ErrorMessage.Should().Contain("Schema not found", because: "the read's own reason is relayed");
		mobile.ErrorCode.Should().Be(DynamicCaseErrorCodes.RecordPageNotFreedom,
			because: "the stage bar exists on Freedom UI web pages only");
	}

	[Test]
	[Description("A page refusal from the bundle checks (another object's page) is returned before any dry run.")]
	public void Plan_ShouldRefuse_WhenThePageIsForAnotherObject() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView(), primaryEntity: "Account"));

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageEntityMismatch, because: "the page is not the object's");
		_writer.DidNotReceiveWithAnyArgs().TryUpdatePage(default!, out _);
	}

	[Test]
	[Description("A page built on a template outside the tabbed family is planned with a note to check the layout.")]
	public void Plan_ShouldNoteTheLayout_WhenTheTemplateIsNotTabbed() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView(),
			template: "PageWithAreaFreedomTemplate"));
		Saves();

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.Status.Should().Be(DynamicCasePageStep.StatusPlanned, because: "the template alone does not refuse the bar");
		plan.Notes.Should().ContainSingle(note => note.Contains("PageWithAreaFreedomTemplate") && note.Contains("tabbed"),
			because: "the product adds the bar only to tabbed pages, so the caller is told to check");
	}

	[Test]
	[Description("Write runs the data-source validator on the fragment, then the append save with the same body, not a dry run.")]
	public void Write_ShouldValidateThenSaveTheSameFragment() {
		// Arrange
		Saves();
		DynamicCasePagePlan plan = new() {
			Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = Page,
			Fragment = ProgressBarFragment.BuildBody(Page, Entity, string.Empty), ModelConfigJson = "{}"
		};

		// Act
		DynamicCasePageWrite write = _step.Write(Env, plan);

		// Assert
		write.Inserted.Should().BeTrue(because: "the save succeeded");
		_validator.Received(1).Validate(plan.Fragment, Arg.Any<Func<string>>());
		_writer.Received(1).TryUpdatePage(Arg.Is<PageUpdateOptions>(options =>
				!options.DryRun && options.Mode == "append" && options.Body == plan.Fragment),
			out Arg.Any<PageUpdateResponse>());
	}

	private static DynamicCasePagePlan PlannedBody() => new() {
		Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = Page, Fragment = "body", ModelConfigJson = "{}"
	};

	[Test]
	[Description("A data-source validator refusal is a failed write and nothing is saved.")]
	public void Write_ShouldNotSave_WhenTheValidatorRefuses() {
		// Arrange
		_validator.Validate("body", Arg.Any<Func<string>>()).Returns(new SchemaValidationResult {
			IsValid = false, Errors = ["undeclared data source"]
		});

		// Act
		DynamicCasePageWrite write = _step.Write(Env, PlannedBody());

		// Assert
		write.Inserted.Should().BeFalse(because: "the validator refused the fragment");
		write.Error.Should().Contain("undeclared data source", because: "its reason is relayed");
		_writer.DidNotReceiveWithAnyArgs().TryUpdatePage(default!, out _);
	}

	[Test]
	[Description("A save the page designer refuses is a failed write carrying the save's own reason.")]
	public void Write_ShouldReportTheSaveError_WhenTheSaveFails() {
		// Arrange
		Saves(writeSucceeds: false, error: "save failed");

		// Act
		DynamicCasePageWrite write = _step.Write(Env, PlannedBody());

		// Assert
		write.Inserted.Should().BeFalse(because: "the save did not land");
		write.Error.Should().Be("save failed", because: "the save's own reason is relayed");
	}

	[Test]
	[Description("A save that throws is reported as a failed write rather than thrown, because the case is already written when this runs.")]
	public void Write_ShouldReportFailure_WithoutThrowing_WhenTheSaveThrows() {
		// Arrange
		_writer.TryUpdatePage(Arg.Any<PageUpdateOptions>(), out Arg.Any<PageUpdateResponse>())
			.Throws(new InvalidOperationException("connection reset"));

		// Act
		DynamicCasePageWrite write = _step.Write(Env, PlannedBody());

		// Assert
		write.Inserted.Should().BeFalse(because: "a thrown save did not write the bar");
		write.Error.Should().Contain("connection reset", because: "the exception text is relayed");
	}
}
