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
		_resolver.Resolve(Package, Entity, Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
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

	private void PageReads(PageBundleInfo bundle, string schemaType = "web", string designPackage = Package,
		PageEditableSchemaInfo editable = null, string rawBody = null) {
		_pageReader.TryGetPage(Arg.Any<PageGetOptions>(), out Arg.Any<PageGetResponse>())
			.Returns(call => {
				call[1] = new PageGetResponse {
					Success = true,
					Page = new PageMetadataInfo {
						SchemaName = Page, SchemaType = schemaType, DesignPackageName = designPackage
					},
					Bundle = bundle,
					Raw = rawBody is null ? null : new PageRawInfo { Body = rawBody },
					Editable = editable
				};
				return true;
			});
	}

	private static PageEditableSchemaInfo Editable(string checksum = "abc123") =>
		new() { EditableSchemaExists = true, Checksum = checksum };

	[Test]
	[Description("A page stored in the full form, as create-app makes every record page, is planned as a REPLACE of its own body with the two inserts added to its viewConfigDiff, dry-run against the page with the checksum pinned, so it takes the bar in one call instead of being refused page-append-rejected.")]
	public void Plan_ShouldPlanAReplaceOfTheFullBody_WhenThePageIsStoredInTheFullForm() {
		// Arrange
		string stored = ProgressBarPlacementTests.FullFormBody(Entity);
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()), editable: Editable(),
			rawBody: stored);
		Saves();

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.Status.Should().Be(DynamicCasePageStep.StatusPlanned, because: "a full-form page can take the bar");
		plan.WriteMode.Should().Be("replace", because: "append cannot merge into a full-form body");
		plan.Fragment.Should().Be(ProgressBarFragment.BuildBody(Page, Entity, string.Empty),
			because: "page.fragment still names the two items, which undo step 4 removes");
		ProgressBarFragment.TryInsertIntoFullBody(stored, Entity, string.Empty, out string expected, out _);
		plan.WriteBody.Should().Be(expected, because: "the write sends the stored body with exactly the two inserts added");
		_writer.Received(1).TryUpdatePage(Arg.Is<PageUpdateOptions>(options =>
				options.DryRun && options.Mode == "replace" && options.Body == plan.WriteBody
				&& options.ExpectedChecksum == "abc123" && options.Validate && options.SchemaName == Page),
			out Arg.Any<PageUpdateResponse>());
		_writer.DidNotReceive().TryUpdatePage(Arg.Is<PageUpdateOptions>(options => options.Mode == "append"),
			out Arg.Any<PageUpdateResponse>());
		_writer.DidNotReceive().TryUpdatePage(Arg.Is<PageUpdateOptions>(options => !options.DryRun),
			out Arg.Any<PageUpdateResponse>());
	}

	[Test]
	[Description("A diff-form stored body keeps the append path: replace is only for the form append cannot merge into.")]
	public void Plan_ShouldKeepTheAppend_WhenTheStoredBodyIsInTheDiffForm() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()), editable: Editable(),
			rawBody: ProgressBarFragment.BuildBody(Page, "UsrOther", "_x"));
		Saves();

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.WriteMode.Should().Be("append", because: "a diff-form body is what append merges into");
		plan.WriteBody.Should().Be(plan.Fragment, because: "append sends the fragment itself");
	}

	[Test]
	[Description("A full-form page whose checksum could not be read is refused before anything is written: a replace sends the body read at plan time, so without a pin a designer save between the plan and the write would be overwritten.")]
	public void Plan_ShouldRefuse_WhenAFullFormPageHasNoChecksumToPin() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()), editable: null,
			rawBody: ProgressBarPlacementTests.FullFormBody(Entity));
		Saves();

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.Status.Should().Be(DynamicCasePageStep.StatusRefused, because: "an unpinned replace could lose a change");
		plan.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageAppendRejected, because: "it is a page-write refusal");
		plan.ErrorMessage.Should().Contain("checksum", because: "the refusal says what is missing");
		_writer.DidNotReceiveWithAnyArgs().TryUpdatePage(default!, out _);
	}

	[Test]
	[Description("A full-form page whose replace dry run fails is page-append-rejected with the save's own reason, before the case is written.")]
	public void Plan_ShouldRefuse_WhenTheReplaceDryRunIsRejected() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()), editable: Editable(),
			rawBody: ProgressBarPlacementTests.FullFormBody(Entity));
		Saves(dryRunSucceeds: false, error: "duplicate element name");

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.Status.Should().Be(DynamicCasePageStep.StatusRefused, because: "the save would refuse the same body");
		plan.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageAppendRejected, because: "that is the dry-run refusal code");
		plan.ErrorMessage.Should().Contain("replace dry run").And.Contain("duplicate element name",
			because: "the message names the write that was tried and relays the save's reason");
	}

	[Test]
	[Description("A full-form page whose stored viewConfigDiff cannot take the inserts is refused with the reason, and no dry run is sent.")]
	public void Plan_ShouldRefuse_WhenTheFullBodyHasNoViewConfigDiffArray() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()), editable: Editable(),
			rawBody: "define(\"P\", function() { return { modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/ }; });");
		Saves();

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageAppendRejected, because: "the bar cannot be placed");
		plan.ErrorMessage.Should().Contain("no viewConfigDiff", because: "the reason is relayed");
		_writer.DidNotReceiveWithAnyArgs().TryUpdatePage(default!, out _);
	}

	[Test]
	[Description("Write sends a full-form plan as the planned replace body with the plan's checksum, not as an append of the fragment.")]
	public void Write_ShouldSaveTheReplaceBody_WhenThePlanIsAReplace() {
		// Arrange
		Saves();
		DynamicCasePagePlan plan = new() {
			Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = Page,
			Fragment = ProgressBarFragment.BuildBody(Page, Entity, string.Empty),
			WriteMode = "replace", WriteBody = "the whole body", ExpectedChecksum = "abc123"
		};

		// Act
		DynamicCasePageWrite write = _step.Write(Env, plan);

		// Assert
		write.Inserted.Should().BeTrue(because: "the save succeeded");
		_writer.Received(1).TryUpdatePage(Arg.Is<PageUpdateOptions>(options =>
				!options.DryRun && options.Mode == "replace" && options.Body == "the whole body"
				&& options.ExpectedChecksum == "abc123"),
			out Arg.Any<PageUpdateResponse>());
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
	[Description("The plan pins the editable schema's checksum it read, on the dry run and in the plan the write sends, so a designer save between the plan and the write is refused instead of merged over.")]
	public void Plan_ShouldPinTheEditableChecksum_OnTheDryRunAndInThePlan() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()),
			editable: new PageEditableSchemaInfo { EditableSchemaExists = true, Checksum = "abc123" });
		Saves();

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);
		_step.Write(Env, plan);

		// Assert
		plan.ExpectedChecksum.Should().Be("abc123", because: "the plan carries the checksum it was made against");
		_writer.Received(1).TryUpdatePage(Arg.Is<PageUpdateOptions>(options =>
				options.DryRun && options.ExpectedChecksum == "abc123"),
			out Arg.Any<PageUpdateResponse>());
		_writer.Received(1).TryUpdatePage(Arg.Is<PageUpdateOptions>(options =>
				!options.DryRun && options.ExpectedChecksum == "abc123"),
			out Arg.Any<PageUpdateResponse>());
	}

	[Test]
	[Description("With no replacing schema in the design package there is no checksum to pin, and the save is not asked for one.")]
	public void Plan_ShouldPinNoChecksum_WhenNoEditableSchemaExists() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()),
			editable: new PageEditableSchemaInfo { EditableSchemaExists = false, Checksum = "ignored" });
		Saves();

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.ExpectedChecksum.Should().BeNull(because: "there is no editable schema to pin");
		_writer.Received(1).TryUpdatePage(Arg.Is<PageUpdateOptions>(options =>
				options.DryRun && options.ExpectedChecksum == null),
			out Arg.Any<PageUpdateResponse>());
	}

	[Test]
	[Description("The fallback packages are passed to the resolver unchanged.")]
	public void Plan_ShouldPassTheFallbackPackages_ToTheResolver() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()));
		Saves();
		IReadOnlyList<string> fallbacks = ["UsrOrdersApp"];

		// Act
		_step.Plan(Env, Package, Entity, null, fallbacks);

		// Assert
		_resolver.Received(1).Resolve(Package, Entity, null, fallbacks);
	}

	[Test]
	[Description("Write saves the planned fragment with append, not a dry run, and runs no gate of its own: every gate ran in Plan, before the case was written.")]
	public void Write_ShouldSaveTheSameFragment() {
		// Arrange
		Saves();
		DynamicCasePagePlan plan = new() {
			Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = Page,
			Fragment = ProgressBarFragment.BuildBody(Page, Entity, string.Empty)
		};

		// Act
		DynamicCasePageWrite write = _step.Write(Env, plan);

		// Assert
		write.Inserted.Should().BeTrue(because: "the save succeeded");
		_validator.DidNotReceiveWithAnyArgs().Validate(default!, default!);
		_writer.Received(1).TryUpdatePage(Arg.Is<PageUpdateOptions>(options =>
				!options.DryRun && options.Mode == "append" && options.Body == plan.Fragment),
			out Arg.Any<PageUpdateResponse>());
	}

	private static DynamicCasePagePlan PlannedBody() => new() {
		Status = DynamicCasePageStep.StatusPlanned, PageSchemaName = Page, Fragment = "body"
	};

	[Test]
	[Description("A data-source validator refusal is a page refusal in Plan, before the case is written, and no dry run is sent.")]
	public void Plan_ShouldRefuse_WhenTheValidatorRefusesTheFragment() {
		// Arrange
		PageReads(ProgressBarPlacementTests.Bundle(ProgressBarPlacementTests.TabbedView()));
		Saves();
		_validator.Validate(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns(new SchemaValidationResult {
			IsValid = false, Errors = ["undeclared data source"]
		});

		// Act
		DynamicCasePagePlan plan = _step.Plan(Env, Package, Entity, null);

		// Assert
		plan.Status.Should().Be(DynamicCasePageStep.StatusRefused, because: "the fragment references missing data");
		plan.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageAppendRejected, because: "the append would be rejected");
		plan.ErrorMessage.Should().Contain("undeclared data source", because: "its reason is relayed");
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
