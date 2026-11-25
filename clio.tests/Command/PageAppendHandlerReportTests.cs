using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// ENG-107305: the append merge reports every current handler it removes, because several handlers for one
/// request are legal and all of them run, so a removed second handler is lost code.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class PageAppendHandlerReportTests {

	private static string WebBody(string handlers) =>
		$$"""
		define("Test", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
			return {
				viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,
				viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
				modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
				handlers: /**SCHEMA_HANDLERS*/{{handlers}}/**SCHEMA_HANDLERS*/,
				converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
				validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
			};
		});
		""";

	private const string SaveA = """{ request: "crt.SaveRecordRequest", handler: async (request, next) => { a(); return next?.handle(request); } }""";
	private const string SaveB = """{ request: "crt.SaveRecordRequest", handler: async (request, next) => { b(); return next?.handle(request); } }""";
	private const string SaveNew = """{ request: "crt.SaveRecordRequest", handler: async (request, next) => { c(); return next?.handle(request); } }""";
	private const string Init = """{ request: "crt.HandleViewModelInitRequest", handler: async (request, next) => next?.handle(request) }""";

	[Test]
	[Description("Reports both current handlers when the fragment's request was declared twice in the current body.")]
	public void Merge_WhenCurrentBodyHasRequestTwice_ReportsBothRemovedHandlers() {
		// Arrange
		string current = WebBody($"[{SaveA},{SaveB},{Init}]");
		string incoming = WebBody($"[{SaveNew}]");

		// Act
		PageBodyMerger.Merge(current, incoming, out PageAppendProjection projection);

		// Assert
		projection.RemovedHandlers.Should().ContainSingle(because: "one request lost its current handlers")
			.Which.Should().BeEquivalentTo(new PageAppendRemovedHandler("crt.SaveRecordRequest", 2),
				because: "both current handlers of the request are replaced by the fragment's one");
		projection.RemovedHandlers[0].HadDuplicates.Should().BeTrue(
			because: "the current body chained two handlers for the request, so one of them is lost code");
	}

	[Test]
	[Description("Reports a single replaced handler when the current body declared the request once.")]
	public void Merge_WhenFragmentReplacesOneHandler_ReportsOne() {
		// Arrange
		string current = WebBody($"[{SaveA},{Init}]");
		string incoming = WebBody($"[{SaveNew}]");

		// Act
		PageBodyMerger.Merge(current, incoming, out PageAppendProjection projection);

		// Assert
		projection.RemovedHandlers.Should().ContainSingle(because: "the re-sent handler replaced the current one")
			.Which.RemovedCount.Should().Be(1, because: "the current body declared the request once");
		projection.RemovedHandlers[0].HadDuplicates.Should().BeFalse(
			because: "a single replaced handler is the expected re-save, not a loss");
	}

	[Test]
	[Description("Reports nothing when the fragment's requests do not overlap the current handlers.")]
	public void Merge_WhenNoRequestOverlaps_ReportsNothing() {
		// Arrange
		string current = WebBody($"[{Init}]");
		string incoming = WebBody($"[{SaveNew}]");

		// Act
		PageBodyMerger.Merge(current, incoming, out PageAppendProjection projection);

		// Assert
		projection.RemovedHandlers.Should().BeEmpty(because: "every current handler is kept");
	}

	[Test]
	[Description("Reports removed handlers on the verbatim path, when the fragment is not a readable JavaScript array.")]
	public void Merge_WhenFragmentIsNotReadableArray_StillReportsRemovedHandlers() {
		// Arrange — an unterminated fragment the AST reader cannot parse, so the merge falls back to the
		// request-text match and appends the fragment verbatim.
		string current = WebBody($"[{SaveA},{SaveB}]");
		string incoming = WebBody("""[{ request: "crt.SaveRecordRequest", handler: async (request, next) => { if (x { } }]""");

		// Act
		PageBodyMerger.Merge(current, incoming, out PageAppendProjection projection);

		// Assert
		projection.RemovedHandlers.Should().ContainSingle(because: "the verbatim path removes by request text too")
			.Which.RemovedCount.Should().Be(2, because: "both current handlers of the request were removed");
	}
}
