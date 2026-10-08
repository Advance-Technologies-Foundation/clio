using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Covers <see cref="PageUnresolvedMergeDetector"/> (GH-1753): which viewModelConfigDiff / modelConfigDiff
/// merges the platform differ skips because their path does not resolve against the parent schemas plus the
/// body's earlier merges, and which ones it applies.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUnresolvedMergeDetectorTests {

	private const string TemplateModelConfigDiff = """
		[{ "operation": "merge", "path": [], "values": { "dataSources": { "AttachmentListDS": {
			"type": "crt.EntityDataSource", "scope": "viewElement",
			"config": { "entitySchemaName": "SysFile", "attributes": { "Name": { "path": "Name" } } } } } } }]
		""";

	private const string TemplateViewModelConfigDiff = """
		[{ "operation": "merge", "path": [], "values": { "attributes": {
			"AttachmentList": { "isCollection": true, "modelConfig": { "path": "AttachmentListDS" } } } } }]
		""";

	private static readonly PageUnresolvedMergeDetector Detector =
		new(new PageSchemaBodyParser(), () => new JsonPathDiffApplier());

	private static string Body(string viewModelConfigDiff = "[]", string modelConfigDiff = "[]") =>
		"define(\"UsrPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { "
		+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
		+ "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/" + viewModelConfigDiff + "/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
		+ "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/" + modelConfigDiff + "/**SCHEMA_MODEL_CONFIG_DIFF*/, "
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
		+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static IReadOnlyList<PageDesignerHierarchySchema> TemplateParent() => [
		new PageDesignerHierarchySchema {
			UId = "template", Name = "PageWithTabsFreedomTemplate",
			Body = Body(TemplateViewModelConfigDiff, TemplateModelConfigDiff)
		}
	];

	[TestCase("""[{ "operation": "merge", "path": ["dataSources", "UsrNewDS"], "values": { "type": "crt.EntityDataSource" } }]""",
		"[\"dataSources\",\"UsrNewDS\"]")]
	[TestCase("""[{ "operation": "merge", "path": ["dependencies"], "values": { "UsrNewDS": [] } }]""",
		"[\"dependencies\"]")]
	[TestCase("""[{ "operation": "merge", "path": ["dataSources", "AttachmentListDS", "config", "sortingConfig"], "values": { "default": [] } }]""",
		"[\"dataSources\",\"AttachmentListDS\",\"config\",\"sortingConfig\"]")]
	[Description("A modelConfigDiff merge whose path names a key that does not exist yet - a new data source key, an absent top-level container, or a missing segment under a parent-layer data source - is reported.")]
	public void Detect_ShouldReportModelConfigMerge_WhenPathDoesNotResolve(string modelConfigDiff, string expectedPath) {
		// Arrange
		string body = Body(modelConfigDiff: modelConfigDiff);

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, TemplateParent).Warnings;

		// Assert
		warnings.Should().ContainSingle(because: "the differ skips exactly this one merge");
		warnings[0].Should().StartWith("modelConfigDiff merge at path " + expectedPath,
			because: "the warning must name the section and the path of the operation that has no effect");
		warnings[0].Should().Contain("is not applied", because: "the caller must learn the save did not apply the operation");
	}

	[TestCase("""[{ "operation": "merge", "path": ["dataSources"], "values": { "UsrNewDS": { "type": "crt.EntityDataSource" } } }]""")]
	[TestCase("""[{ "operation": "merge", "path": [], "values": { "dataSources": { "UsrNewDS": { "type": "crt.EntityDataSource" } } } }]""")]
	[TestCase("""[{ "operation": "merge", "path": ["dataSources", "AttachmentListDS", "config"], "values": { "entitySchemaName": "ContactFile" } }]""")]
	[TestCase("""[{ "operation": "merge", "path": ["dataSources", "AttachmentListDS", "config", "attributes"], "values": { "Version": { "path": "Version" } } }]""")]
	[Description("A merge that adds a new key through its existing parent, merges at the root, or patches an existing segment of a parent-layer data source applies and is not reported.")]
	public void Detect_ShouldNotReport_WhenModelConfigMergePathResolves(string modelConfigDiff) {
		// Arrange
		string body = Body(modelConfigDiff: modelConfigDiff);

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, TemplateParent).Warnings;

		// Assert
		warnings.Should().BeEmpty(because: "every segment of the path exists in the config the template produces");
	}

	[Test]
	[Description("A viewModelConfigDiff merge that addresses a missing attribute or a missing nested key of an inherited attribute is reported, while the merge beside it that resolves is not.")]
	public void Detect_ShouldReportViewModelConfigMerge_WhenPathDoesNotResolve() {
		// Arrange
		string body = Body(viewModelConfigDiff: """
			[
				{ "operation": "merge", "path": ["attributes", "UsrNewList"], "values": { "isCollection": true } },
				{ "operation": "merge", "path": ["attributes", "AttachmentList", "modelConfig", "pagingConfig"], "values": { "rowCount": 5 } },
				{ "operation": "merge", "path": ["attributes", "AttachmentList", "modelConfig"], "values": { "filterAttributes": [] } }
			]
			""");

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, TemplateParent).Warnings;

		// Assert
		warnings.Should().HaveCount(2, because: "the two merges into missing keys are skipped and the third applies");
		warnings.Should().OnlyContain(w => w.StartsWith("viewModelConfigDiff merge at path "),
			because: "both findings belong to the viewModelConfigDiff section");
		warnings.Should().Contain(w => w.Contains("[\"attributes\",\"UsrNewList\"]"),
			because: "the merge into the new attribute key is skipped");
		warnings.Should().Contain(w => w.Contains("[\"attributes\",\"AttachmentList\",\"modelConfig\",\"pagingConfig\"]"),
			because: "the merge into the missing nested key of the inherited attribute is skipped");
	}

	[Test]
	[Description("Merges run in array order inside their group: a nested merge placed AFTER the merge that creates its target resolves, the same merge placed BEFORE it does not.")]
	public void Detect_ShouldFollowArrayOrder_WhenSameBodyCreatesTheTarget() {
		// Arrange
		const string create = """{ "operation": "merge", "path": ["dataSources"], "values": { "UsrNewDS": { "config": {} } } }""";
		const string patch = """{ "operation": "merge", "path": ["dataSources", "UsrNewDS", "config"], "values": { "entitySchemaName": "Account" } }""";

		// Act
		IReadOnlyList<string> createdFirst = Detector.Detect(Body(modelConfigDiff: $"[{create},{patch}]"), TemplateParent).Warnings;
		IReadOnlyList<string> patchedFirst = Detector.Detect(Body(modelConfigDiff: $"[{patch},{create}]"), TemplateParent).Warnings;

		// Assert
		createdFirst.Should().BeEmpty(because: "the earlier merge in the same body already created the target");
		patchedFirst.Should().ContainSingle(because: "the nested merge runs before its target exists and is skipped");
	}

	[Test]
	[Description("A parent schema stored in the full-config form (SCHEMA_MODEL_CONFIG) contributes its config the way the page bundle merges it, so a merge into its data source resolves.")]
	public void Detect_ShouldResolveAgainstFullConfigParent_WhenParentUsesFullForm() {
		// Arrange
		string fullConfigParent = "define(\"Base\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { "
			+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
			+ "viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/, "
			+ "modelConfig: /**SCHEMA_MODEL_CONFIG*/{ \"dataSources\": { \"PDS\": { \"config\": {} } } }/**SCHEMA_MODEL_CONFIG*/ }; });";
		IReadOnlyList<PageDesignerHierarchySchema> parents = [new PageDesignerHierarchySchema { UId = "base", Body = fullConfigParent }];
		string body = Body(modelConfigDiff: """[{ "operation": "merge", "path": ["dataSources", "PDS", "config"], "values": { "entitySchemaName": "Account" } }]""");

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, () => parents).Warnings;

		// Assert
		warnings.Should().BeEmpty(because: "the full-config parent defines dataSources.PDS.config");
	}

	[Test]
	[Description("The nearest parent is applied last: with parents passed nearest first, a key added by the nearer parent on top of the root template is visible to the body.")]
	public void Detect_ShouldApplyParentsFromRootToNearest_WhenSeveralParentsExist() {
		// Arrange
		IReadOnlyList<PageDesignerHierarchySchema> parents = [
			new PageDesignerHierarchySchema {
				UId = "parent", Body = Body(modelConfigDiff: """[{ "operation": "merge", "path": ["dataSources", "AttachmentListDS"], "values": { "extra": {} } }]""")
			},
			TemplateParent()[0]
		];
		string body = Body(modelConfigDiff: """[{ "operation": "merge", "path": ["dataSources", "AttachmentListDS", "extra"], "values": { "x": 1 } }]""");

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, () => parents).Warnings;

		// Assert
		warnings.Should().BeEmpty(because: "the template defines AttachmentListDS first and the nearer parent then adds 'extra' to it");
	}

	[Test]
	[Description("The runtime starts every chain from an empty 'attributes' object and a 'PageParameters' data source, so with no parent declaring them a merge into 'attributes' or 'dataSources' still applies, while 'dependencies' does not.")]
	public void Detect_ShouldResolveAgainstRuntimeSeed_WhenNoParentDeclaresTheContainers() {
		// Arrange
		string body = Body(
			viewModelConfigDiff: """[{ "operation": "merge", "path": ["attributes"], "values": { "UsrProbe": { "value": 1 } } }]""",
			modelConfigDiff: """
				[
					{ "operation": "merge", "path": ["dataSources"], "values": { "UsrNewDS": {} } },
					{ "operation": "merge", "path": ["dataSources", "PageParameters", "config"], "values": { "x": 1 } },
					{ "operation": "merge", "path": ["dependencies"], "values": { "UsrNewDS": [] } }
				]
				""");

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, () => []).Warnings;

		// Assert
		warnings.Should().ContainSingle(because: "only 'dependencies' is absent from the runtime's starting config");
		warnings[0].Should().StartWith("modelConfigDiff merge at path [\"dependencies\"]",
			because: "the seed holds 'attributes' and 'dataSources.PageParameters', not 'dependencies'");
	}

	[Test]
	[Description("A BlankPageTemplate-like chain whose schemas carry only viewConfigDiff: the merges the warning recommends - into 'attributes' and 'dataSources' - are not reported.")]
	public void Detect_ShouldNotReportRecommendedMerges_WhenChainDeclaresNoConfig() {
		// Arrange
		IReadOnlyList<PageDesignerHierarchySchema> blankChain = [
			new PageDesignerHierarchySchema { UId = "blank", Name = "BlankPageTemplate", Body = Body() },
			new PageDesignerHierarchySchema { UId = "base", Name = "BasePageTemplate", Body = Body() }
		];
		string body = Body(
			viewModelConfigDiff: """[{ "operation": "merge", "path": ["attributes"], "values": { "UsrList": { "isCollection": true, "modelConfig": { "path": "UsrNewDS" } } } }]""",
			modelConfigDiff: """[{ "operation": "merge", "path": ["dataSources"], "values": { "UsrNewDS": { "type": "crt.EntityDataSource" } } }]""");

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, () => blankChain).Warnings;

		// Assert
		warnings.Should().BeEmpty(because: "the runtime seeds both containers, so both merges apply on a blank page");
	}

	[Test]
	[Description("A merge whose path ends on a single value makes the runtime throw (it sets the merged keys on a primitive), so it is an error, and the other merges in the same diff are still checked.")]
	public void Detect_ShouldReportMergeIntoSingleValueAsError_AndKeepCheckingOthers() {
		// Arrange
		string body = Body(viewModelConfigDiff: """
			[
				{ "operation": "merge", "path": ["attributes", "AttachmentList", "isCollection"], "values": { "x": 1 } },
				{ "operation": "merge", "path": ["attributes", "UsrMissing"], "values": { "x": 1 } }
			]
			""");

		// Act
		PageConfigMergeReport report = Detector.Detect(body, TemplateParent);

		// Assert
		report.Errors.Should().ContainSingle(because: "only the merge into the single value breaks the page");
		report.Errors[0].Should().StartWith("viewModelConfigDiff merge at path [\"attributes\",\"AttachmentList\",\"isCollection\"]",
			because: "the error must name the merge that throws").And.Contain("breaks the page");
		report.Warnings.Should().ContainSingle(w => w.Contains("[\"attributes\",\"UsrMissing\"]") && w.Contains("does not exist"),
			because: "the unrelated missing path must still be reported after the throwing merge");
	}

	[Test]
	[Description("A merge whose path ends on an array is reported by the runtime as applied, but the keys it sets on the array are lost, so it is a warning, not an error.")]
	public void Detect_ShouldReportMergeIntoArrayAsWarning() {
		// Arrange
		string body = Body(viewModelConfigDiff: """
			[
				{ "operation": "merge", "path": [], "values": { "UsrList": [1, 2] } },
				{ "operation": "merge", "path": ["UsrList"], "values": { "x": 1 } }
			]
			""");

		// Act
		PageConfigMergeReport report = Detector.Detect(body, TemplateParent);

		// Assert
		report.Errors.Should().BeEmpty(because: "the runtime does not throw on an array target");
		report.Warnings.Should().ContainSingle(because: "the merge into the array changes nothing");
		report.Warnings[0].Should().StartWith("viewModelConfigDiff merge at path [\"UsrList\"]")
			.And.Contain("is an array", because: "the warning must say why the merge has no effect");
	}

	[Test]
	[Description("A merge whose first path segment matches an element _id but whose rest does not resolve makes the runtime throw; the applier reports that cause, so the error names the unresolved path instead of a single value.")]
	public void Detect_ShouldReportUnresolvedIdFallbackAsError_WithItsOwnReason() {
		// Arrange
		string body = Body(viewModelConfigDiff: """
			[
				{ "operation": "merge", "path": ["attributes"], "values": { "UsrRoot": { "_id": "UsrRootId", "inner": {} } } },
				{ "operation": "merge", "path": ["UsrRootId", "missing"], "values": { "x": 1 } }
			]
			""");

		// Act
		PageConfigMergeReport report = Detector.Detect(body, TemplateParent);

		// Assert
		report.Errors.Should().ContainSingle(because: "the runtime throws on the merge into the missing remainder");
		report.Errors[0].Should().Contain("matches an element by its _id", because: "the error must state the real cause")
			.And.NotContain("single value", because: "the value at that path does not exist, so it is not a single value");
	}

	[Test]
	[Description("When an operation that is not a merge makes the differ throw, the merges of that section are still reported, one warning says the section has a throwing operation, and the other section is still checked.")]
	public void Detect_ShouldKeepMergeFindings_WhenANonMergeOperationThrows() {
		// Arrange
		string body = Body(
			viewModelConfigDiff: """
				[
					{ "operation": "merge", "path": ["attributes", "UsrMissing"], "values": { "x": 1 } },
					{ "operation": "insert", "name": "UsrX", "path": ["attributes", "AttachmentList", "modelConfig", "path"], "values": {} }
				]
				""",
			modelConfigDiff: """[{ "operation": "merge", "path": ["dataSources", "UsrNewDS"], "values": { "x": 1 } }]""");

		// Act
		PageConfigMergeReport report = Detector.Detect(body, TemplateParent);

		// Assert
		report.Errors.Should().BeEmpty(because: "no merge throws");
		report.Warnings.Should().Contain(w => w.Contains("[\"attributes\",\"UsrMissing\"]"),
			because: "the merge in the section with the throwing operation is still checked");
		report.Warnings.Should().Contain(w => w.StartsWith("viewModelConfigDiff: an operation that is not a merge"),
			because: "the caller must learn that the section has an operation the differ throws on");
		report.Warnings.Should().Contain(w => w.Contains("[\"dataSources\",\"UsrNewDS\"]"),
			because: "the other section is checked on its own");
	}

	[TestCase("""{ "operation": "merge", "path": ["attributes", "AttachmentList"] }""")]
	[TestCase("""{ "operation": "merge", "path": ["attributes", "AttachmentList"], "values": null }""")]
	[Description("A merge with missing or null values on a target that resolves makes the runtime throw (Object.keys of undefined or null), so it is an error.")]
	public void Detect_ShouldReportMissingValuesAsError_WhenTargetResolves(string merge) {
		// Arrange
		string body = Body(viewModelConfigDiff: $"[{merge}]");

		// Act
		PageConfigMergeReport report = Detector.Detect(body, TemplateParent);

		// Assert
		report.Errors.Should().ContainSingle(because: "the runtime throws on this merge");
		report.Errors[0].Should().Contain("no \"values\" object", because: "the error must say what is wrong");
		report.Warnings.Should().BeEmpty(because: "the merge is reported once, as an error");
	}

	[Test]
	[Description("A merge with null values whose target does not resolve is skipped by the runtime before it reads the values, so it is a warning about the path, not an error.")]
	public void Detect_ShouldReportMissingValuesAsWarning_WhenTargetDoesNotResolve() {
		// Arrange
		string body = Body(viewModelConfigDiff: """[{ "operation": "merge", "path": ["attributes", "UsrMissing"], "values": null }]""");

		// Act
		PageConfigMergeReport report = Detector.Detect(body, TemplateParent);

		// Assert
		report.Errors.Should().BeEmpty(because: "the runtime never reaches the values of a merge it skips");
		report.Warnings.Should().ContainSingle(w => w.Contains("does not exist"), because: "the path does not resolve");
	}

	[Test]
	[Description("A merge whose values is not an object is reported as not applied as written, and the rest of the diff is still checked.")]
	public void Detect_ShouldReportMergeWithNonObjectValues_AndKeepCheckingOthers() {
		// Arrange
		string body = Body(modelConfigDiff: """
			[
				{ "operation": "merge", "path": [], "values": ["dataSources"] },
				{ "operation": "merge", "path": ["dataSources", "UsrMissing"], "values": { "x": 1 } }
			]
			""");

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, TemplateParent).Warnings;

		// Assert
		warnings.Should().HaveCount(2, because: "the malformed merge and the unresolved merge are both reported");
		warnings.Should().Contain(w => w.StartsWith("modelConfigDiff merge at path []") && w.Contains("\"values\" is not an object"),
			because: "a merge's values must be an object");
	}

	[Test]
	[Description("A merge with neither a path nor a name that matches an element has nothing to merge into and is reported with the root-merge remedy.")]
	public void Detect_ShouldReportMerge_WhenItHasNoPathAndNoMatchingName() {
		// Arrange
		string body = Body(viewModelConfigDiff: """[{ "operation": "merge", "values": { "PDS_UsrName": { "modelConfig": { "path": "PDS.UsrName" } } } }]""");

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, TemplateParent).Warnings;

		// Assert
		warnings.Should().ContainSingle(because: "the differ cannot resolve a target for this merge");
		warnings[0].Should().Contain("has no \"path\"", because: "the warning must say why the merge has no target")
			.And.Contain("path []", because: "the remedy is a root merge");
	}

	[Test]
	[Description("The same skipped merge authored twice - typical after an append re-sends a fragment - is named once, not twice.")]
	public void Detect_ShouldNameDuplicateSkippedMergeOnce_WhenBodyRepeatsIt() {
		// Arrange
		const string merge = """{ "operation": "merge", "path": ["dataSources", "UsrNewDS"], "values": { "x": 1 } }""";

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(Body(modelConfigDiff: $"[{merge},{merge}]"), TemplateParent).Warnings;

		// Assert
		warnings.Should().ContainSingle(because: "a repeated finding adds no information and buries the others");
	}

	[Test]
	[Description("Findings beyond MaxReportedFindings collapse into one summary line that gives the remaining count.")]
	public void Detect_ShouldCapFindings_WhenTooManyMergesDoNotResolve() {
		// Arrange
		int total = PageUnresolvedMergeDetector.MaxReportedFindings + 3;
		string operations = "[" + string.Join(",", Enumerable.Range(0, total).Select(i =>
			$$"""{ "operation": "merge", "path": ["dataSources", "Missing{{i}}"], "values": { "x": 1 } }""")) + "]";

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(Body(modelConfigDiff: operations), TemplateParent).Warnings;

		// Assert
		warnings.Should().HaveCount(PageUnresolvedMergeDetector.MaxReportedFindings + 1,
			because: "the listed findings stop at the ceiling and one summary line follows");
		warnings[^1].Should().StartWith("3 more", because: "the summary line gives the count that was not listed");
	}

	[TestCase("""[{ "operation": "merge", "path": [], "values": { "attributes": {} } }]""", 0)]
	[TestCase("""[{ "operation": "insert", "path": ["attributes"], "values": {} }]""", 0)]
	[TestCase("""[{ "operation": "merge", "path": ["attributes"], "values": {} }]""", 1)]
	[TestCase("""[{ "operation": "merge", "values": {} }]""", 1)]
	[TestCase("""[{ "operation": "merge", "path": [] }]""", 1)]
	[Description("The parent schemas are read only when the body has a merge that can miss its target, so a body with root merges alone costs no hierarchy read.")]
	public void Detect_ShouldReadParentsOnlyWhenAMergeCanMissItsTarget(string viewModelConfigDiff, int expectedReads) {
		// Arrange
		string body = Body(viewModelConfigDiff: viewModelConfigDiff);
		int reads = 0;

		// Act
		Detector.Detect(body, () => {
			reads++;
			return TemplateParent();
		});

		// Assert
		reads.Should().Be(expectedReads, because: "a root merge always resolves and only merges are checked");
	}
}
