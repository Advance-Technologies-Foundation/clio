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
		IReadOnlyList<string> warnings = Detector.Detect(body, TemplateParent);

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
		IReadOnlyList<string> warnings = Detector.Detect(body, TemplateParent);

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
		IReadOnlyList<string> warnings = Detector.Detect(body, TemplateParent);

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
		IReadOnlyList<string> createdFirst = Detector.Detect(Body(modelConfigDiff: $"[{create},{patch}]"), TemplateParent);
		IReadOnlyList<string> patchedFirst = Detector.Detect(Body(modelConfigDiff: $"[{patch},{create}]"), TemplateParent);

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
		IReadOnlyList<string> warnings = Detector.Detect(body, () => parents);

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
		IReadOnlyList<string> warnings = Detector.Detect(body, () => parents);

		// Assert
		warnings.Should().BeEmpty(because: "the template defines AttachmentListDS first and the nearer parent then adds 'extra' to it");
	}

	[Test]
	[Description("With no parent schema at all, the config base is empty, so any merge with a non-empty path is reported.")]
	public void Detect_ShouldReportEveryPathMerge_WhenThereAreNoParents() {
		// Arrange
		string body = Body(modelConfigDiff: """[{ "operation": "merge", "path": ["dataSources"], "values": { "UsrNewDS": {} } }]""");

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, () => []);

		// Assert
		warnings.Should().ContainSingle(because: "the runtime base is empty, so 'dataSources' does not exist");
	}

	[Test]
	[Description("A merge with neither a path nor a name that matches an element has nothing to merge into and is reported with the root-merge remedy.")]
	public void Detect_ShouldReportMerge_WhenItHasNoPathAndNoMatchingName() {
		// Arrange
		string body = Body(viewModelConfigDiff: """[{ "operation": "merge", "values": { "PDS_UsrName": { "modelConfig": { "path": "PDS.UsrName" } } } }]""");

		// Act
		IReadOnlyList<string> warnings = Detector.Detect(body, TemplateParent);

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
		IReadOnlyList<string> warnings = Detector.Detect(Body(modelConfigDiff: $"[{merge},{merge}]"), TemplateParent);

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
		IReadOnlyList<string> warnings = Detector.Detect(Body(modelConfigDiff: operations), TemplateParent);

		// Assert
		warnings.Should().HaveCount(PageUnresolvedMergeDetector.MaxReportedFindings + 1,
			because: "the listed findings stop at the ceiling and one summary line follows");
		warnings[^1].Should().StartWith("3 more", because: "the summary line gives the count that was not listed");
	}

	[TestCase("""[{ "operation": "merge", "path": [], "values": { "attributes": {} } }]""", 0)]
	[TestCase("""[{ "operation": "insert", "path": ["attributes"], "values": {} }]""", 0)]
	[TestCase("""[{ "operation": "merge", "path": ["attributes"], "values": {} }]""", 1)]
	[TestCase("""[{ "operation": "merge", "values": {} }]""", 1)]
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
