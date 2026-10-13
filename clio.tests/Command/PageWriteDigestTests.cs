using System.Linq;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>The page write digest (ENG-103023) over web and mobile bodies.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageWriteDigestTests {

	private static string WebBody(string viewConfigDiff) =>
		"define(\"Usr_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { "
		+ $"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{viewConfigDiff}/**SCHEMA_VIEW_CONFIG_DIFF*/, "
		+ "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
		+ "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, "
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
		+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
		+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Description("Lists each viewConfigDiff operation as one line, says whether the body was a fragment, and reports a skipped validation as skipped.")]
	public void TryBuild_ShouldListOperations_AndNameBasisAndValidation() {
		// Arrange
		string body = WebBody("[{\"operation\":\"insert\",\"name\":\"Phone\",\"parentName\":\"Main\",\"values\":{\"type\":\"crt.PhoneInput\"}},"
			+ "{\"operation\":\"merge\",\"name\":\"Main\",\"values\":{\"visible\":true}},{\"operation\":\"remove\",\"name\":\"Old\"}]");

		// Act
		PageWriteDigest digest = PageWriteDigest.TryBuild(body, appendMode: true, validationRan: false);

		// Assert
		digest.Components.Should().Equal(["insert Phone (crt.PhoneInput) in Main", "merge Main", "remove Old"],
			because: "each operation is one compact line");
		digest.Basis.Should().Be("fragment", because: "an append write merges the body as a fragment");
		digest.Validation.Should().Be("skipped", because: "validate=false skipped the content chain");
		digest.ComponentsOmitted.Should().BeNull(because: "nothing was cut");
		digest.OtherSections.Should().BeNull(because: "the body carries nothing outside viewConfigDiff");
	}

	[Test]
	[Description("Counts the non-component sections, so a write that adds only a handler or a data source does not read as an empty write.")]
	public void TryBuild_ShouldCountNonComponentSections() {
		// Arrange
		string body = WebBody("[]")
			.Replace("/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
				"/**SCHEMA_HANDLERS*/[{ request: \"crt.SaveRecordRequest\", handler: async (request, next) => next?.handle(request) }]/**SCHEMA_HANDLERS*/")
			.Replace("/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/",
				"/**SCHEMA_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"path\":[],\"values\":{}}]/**SCHEMA_MODEL_CONFIG_DIFF*/");

		// Act
		PageWriteDigest digest = PageWriteDigest.TryBuild(body, appendMode: true, validationRan: true);

		// Assert
		digest.Components.Should().BeEmpty(because: "the body has no viewConfigDiff operation");
		digest.OtherSections.Should().Contain("handlers", 1, because: "the handler the write carried is counted");
		digest.OtherSections.Should().Contain("modelConfigDiff", 1, because: "the model change is counted");
	}

	[Test]
	[Description("Caps the list and counts the rest, so a large replace body cannot grow the reply without bound; reads a mobile JSON body too.")]
	public void TryBuild_ShouldCapTheListAndReadMobileBodies() {
		// Arrange
		string operations = string.Join(",", Enumerable.Range(0, 40)
			.Select(index => $"{{\"operation\":\"insert\",\"name\":\"F{index}\",\"values\":{{\"type\":\"crt.Input\"}}}}"));
		string mobile = $"{{\"viewConfigDiff\":[{operations}],\"viewModelConfigDiff\":[],\"modelConfigDiff\":[]}}";

		// Act
		PageWriteDigest digest = PageWriteDigest.TryBuild(mobile, appendMode: false, validationRan: true);
		PageWriteDigest unreadable = PageWriteDigest.TryBuild("", appendMode: false, validationRan: true);

		// Assert
		digest.Components.Should().HaveCount(PageWriteDigest.MaxComponents, because: "the list is capped");
		digest.ComponentsOmitted.Should().Be(40 - PageWriteDigest.MaxComponents, because: "the rest are counted");
		digest.Basis.Should().Be("body", because: "a replace write's body is the whole own layer");
		unreadable.Should().BeNull(because: "a digest never fails a write; an unparseable body yields none");
	}

	[Test]
	[Description("Returns no digest for an append whose merged viewConfigDiff was never written back, so the fragment's components are not reported as written; an applied merge still gets one.")]
	public void TryBuild_ShouldReturnNull_WhenTheAppendMergeWasNotApplied() {
		// Arrange
		string body = WebBody("[{\"operation\":\"insert\",\"name\":\"Phone\",\"parentName\":\"Main\",\"values\":{\"type\":\"crt.PhoneInput\"}}]");

		// Act
		PageWriteDigest discarded = PageWriteDigest.TryBuild(body, appendMode: true, validationRan: true,
			new PageAppendProjection { ViewConfigDiffApplied = false, IncomingOperationCount = 1 });
		PageWriteDigest applied = PageWriteDigest.TryBuild(body, appendMode: true, validationRan: true,
			new PageAppendProjection { ViewConfigDiffApplied = true });

		// Assert
		discarded.Should().BeNull(because: "the merged viewConfigDiff never reached the page, so nothing it lists was written");
		applied.Components.Should().Equal(["insert Phone (crt.PhoneInput) in Main"],
			because: "an applied merge wrote the fragment's components");
	}

	[Test]
	[Description("Review ENG-103023 #11: an append fragment with no component operations (handlers only) loses nothing when the viewConfigDiff markers are missing, so it keeps its digest; the post-save checksum is carried.")]
	public void TryBuild_ShouldKeepTheDigest_ForAComponentFreeFragment_AndCarryTheChecksum() {
		// Arrange
		string body = WebBody("[]");

		// Act
		PageWriteDigest digest = PageWriteDigest.TryBuild(body, appendMode: true, validationRan: true,
			new PageAppendProjection { ViewConfigDiffApplied = false, IncomingOperationCount = 0 }, "4f3374af");

		// Assert
		digest.Should().NotBeNull(because: "a fragment without component operations had nothing to lose");
		digest.Components.Should().BeEmpty(because: "the fragment wrote no components");
		digest.Checksum.Should().Be("4f3374af", because: "the post-save checksum is the next save's baseline");
	}

	[Test]
	[Description("Pins the literal component cap, so a change of the number is a deliberate one.")]
	public void MaxComponents_ShouldBe25() {
		// Assert
		PageWriteDigest.MaxComponents.Should().Be(25, because: "the page digest lists at most 25 components");
	}
}
