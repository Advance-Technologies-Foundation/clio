using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Proves the ENG-95429 rule is enforced on the WRITE path, not only by the read-only validator.
/// <see cref="SchemaValidationServiceTests"/> pins what the rule decides; these tests pin that
/// <see cref="PageUpdateTool"/> turns that decision into an aborted save — the defect being fixed is a
/// write that SUCCEEDS while persisting an unrenderable element, so "the validator returns an error" is
/// not by itself evidence the save stops (raised in review of PR #1086).
/// </summary>
/// <remarks>
/// Exercised through the <c>ValidateBody</c> internal seam, mirroring how
/// <see cref="PageUpdateToolRunProcessTests"/> drives <c>ValidateRunProcessButtons</c>. The seam is the
/// last gate before the save: <c>TryCreatePreExecutionFailureAsync</c> returns its failure directly and
/// the command that writes to Creatio is never resolved. It runs entirely offline — no environment is
/// configured here, and the mobile catalogs are substitutes — which is exactly the point: the abort
/// happens before any network work.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageUpdateToolMobileTypePlacementTests {

	// Minimal mobile body (plain JSON, no AMD markers) whose single insert carries the component type on the
	// operation object instead of inside `values` — the exact shape persisted by the ENG-95429 run.
	private const string MobileBodyWithMisplacedType =
		"""
		{
		  "viewConfigDiff": [
		    { "operation": "insert", "name": "RunProcessButton", "type": "crt.Button",
		      "parentName": "Scaffold", "propertyName": "actions",
		      "values": { "clicked": { "request": "crt.RunBusinessProcessRequest",
		                              "params": { "processName": "UsrSomeProcess",
		                                          "processRunType": "RegardlessOfThePage" } } } }
		  ],
		  "viewModelConfigDiff": [],
		  "modelConfigDiff": []
		}
		""";

	private const string MobileBodyWithTypeInsideValues =
		"""
		{
		  "viewConfigDiff": [
		    { "operation": "insert", "name": "RunProcessButton",
		      "parentName": "Scaffold", "propertyName": "actions",
		      "values": { "type": "crt.Button", "clicked": { "request": "crt.RunBusinessProcessRequest",
		                              "params": { "processName": "UsrSomeProcess",
		                                          "processRunType": "RegardlessOfThePage" } } } }
		  ],
		  "viewModelConfigDiff": [],
		  "modelConfigDiff": []
		}
		""";

	// A merge on Scaffold whose values author a button inside the actions slot — the ENG-95429 shape the
	// insert-scoped slot rule cannot see. Stand-verified: the write succeeds and the button reaches nothing.
	private const string MobileBodyWithMergeAuthoredButton =
		"""
		{
		  "viewConfigDiff": [
		    { "operation": "merge", "name": "Scaffold",
		      "values": { "actions": [ { "type": "crt.Button", "name": "UsrMergeProbeButton",
		                                 "clicked": { "request": "crt.SaveRecordRequest" } } ] } }
		  ],
		  "viewModelConfigDiff": [],
		  "modelConfigDiff": []
		}
		""";

	// The same authoring through a slot the target may legitimately lack. Advisory, not blocking, because a
	// merge is often the only single-operation route there — so the save must NOT abort.
	private const string MobileBodyWithMergeAuthoredMenuItems =
		"""
		{
		  "viewConfigDiff": [
		    { "operation": "merge", "name": "UsrActionsButton",
		      "values": { "menuItems": [ { "type": "crt.MenuItem", "name": "UsrExport" } ] } }
		  ],
		  "viewModelConfigDiff": [],
		  "modelConfigDiff": []
		}
		""";

	private static PageUpdateTool BuildTool() =>
		new(
			command: null,
			logger: ConsoleLogger.Instance,
			commandResolver: Substitute.For<IToolCommandResolver>(),
			mobileComponentCatalog: Substitute.For<IMobileComponentInfoCatalog>(),
			webComponentCatalog: Substitute.For<IComponentInfoCatalog>(),
			samplingService: Substitute.For<IPageBodySamplingService>(),
			pageBaselineGuard: new PageBaselineGuard(Substitute.For<System.IO.Abstractions.IFileSystem>()));

	[Test]
	[Description("ENG-95429 on the write path: update-page refuses to save a mobile body whose insert carries its component type outside 'values', naming the element in the failure.")]
	public void ValidateBody_WhenMobileInsertTypeIsOnOperationObject_AbortsTheSave() {
		// Arrange
		PageUpdateTool tool = BuildTool();
		PageUpdateOptions options = new() { SchemaName = "UsrTest_MobileFormPage", Body = MobileBodyWithMisplacedType };

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> _) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().NotBeNull(
			because: "a blocking validation error must stop the write before the page command is ever resolved");
		failure.Success.Should().BeFalse(
			because: "the caller must see the save as failed, not as a success carrying a warning");
		failure.Error.Should().Contain("RunProcessButton",
			because: "the failure must name the element so the agent can fix the right entry");
		failure.Error.Should().Contain("values",
			because: "the failure must carry the actionable fix through to the write-path response");
	}

	[Test]
	[Description("ENG-95429 on the write path: the same body with its type inside 'values' is not blocked, so the new rule cannot stop a correctly authored save.")]
	public void ValidateBody_WhenMobileInsertTypeIsInsideValues_DoesNotAbortTheSave() {
		// Arrange
		PageUpdateTool tool = BuildTool();
		PageUpdateOptions options = new() { SchemaName = "UsrTest_MobileFormPage", Body = MobileBodyWithTypeInsideValues };

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> _) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().BeNull(
			because: "the canonical shape must reach the save path untouched — an A/B pair differing only in type placement");
	}

	[Test]
	[Description("ENG-95429 on the write path: update-page refuses a mobile body whose merge authors a button inside the Scaffold actions slot. Stand-verified that this write otherwise succeeds while the button reaches nothing, so only an aborted save proves the rule closes it.")]
	public void ValidateBody_WhenMobileMergeAuthorsChildrenInScaffoldSlot_AbortsTheSave() {
		// Arrange
		PageUpdateTool tool = BuildTool();
		PageUpdateOptions options = new() { SchemaName = "UsrTest_MobileFormPage", Body = MobileBodyWithMergeAuthoredButton };

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> _) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().NotBeNull(
			because: "a blocking validation error must stop the write before the page command is ever resolved");
		failure.Success.Should().BeFalse(
			because: "the caller must see the save as failed, not as a success carrying a warning");
		failure.Error.Should().Contain("UsrMergeProbeButton",
			because: "the failure must name the child that goes missing, since the entry is named after the merged element");
		failure.Error.Should().Contain("actions",
			because: "the author needs the slot to locate the defect in a body with several merges");
	}

	[Test]
	[Description("The advisory half does not abort the save: authoring into a slot the target may legitimately lack warns, because clio validates viewConfigDiff against an empty base and a merge is often the only single-operation route there.")]
	public void ValidateBody_WhenMobileMergeAuthorsChildrenInAnOptionalSlot_DoesNotAbortTheSave() {
		// Arrange
		PageUpdateTool tool = BuildTool();
		PageUpdateOptions options = new() { SchemaName = "UsrTest_MobileFormPage", Body = MobileBodyWithMergeAuthoredMenuItems };

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> _) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().BeNull(
			because: "refusing a shape that frequently applies correctly would break legitimate mobile authoring");
	}

	[Test]
	[Description("ENG-95429 on the write path: 'set' is blocked too, since JsonDiffApplier.Set is Remove followed by Insert on the same config and drops an operation-level type identically.")]
	public void ValidateBody_WhenMobileSetTypeIsOnOperationObject_AbortsTheSave() {
		// Arrange
		PageUpdateTool tool = BuildTool();
		string body = MobileBodyWithMisplacedType.Replace("\"operation\": \"insert\"", "\"operation\": \"set\"");
		PageUpdateOptions options = new() { SchemaName = "UsrTest_MobileFormPage", Body = body };

		// Act
		(PageUpdateResponse failure, IReadOnlyList<string> _) = tool.ValidateBody(options, requestedVersion: null);

		// Assert
		failure.Should().NotBeNull(because: "set authors the element through the same values-only build");
		failure.Success.Should().BeFalse(because: "the save must abort for set exactly as it does for insert");
		failure.Error.Should().Contain("RunProcessButton", because: "the failure must name the offending element");
	}
}
