using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Unit tests for <see cref="McpResultDiagnostics"/> (GitHub issues #1384 and #1537), the parse-failure
/// describer shared by <see cref="EntitySchemaStructuredResultParser"/> and every sibling result parser in
/// this folder. They construct <see cref="CallToolResult"/> instances in-memory (no MCP server, no stand,
/// no network I/O), so they are categorized <c>Unit</c> rather than <c>McpE2E.Sandbox</c>.
/// </summary>
/// <remarks>
/// The payload assertions go through <see cref="McpResultDiagnostics.DescribeWithSink"/> with a recording
/// sink, so nothing here writes to disk. <see cref="TestResultsPayloadDumpSinkTests"/> covers the real
/// sink separately.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Category("McpE2E.NoEnvironment")]
[Property("Module", "McpServer")]
public sealed class McpResultDiagnosticsTests {
	[Test]
	[Description("Describes a result with neither StructuredContent nor Content as IsError plus '(none)' for both payload fields.")]
	public void Describe_ShouldReportNonePayloads_WhenResultIsEmpty() {
		// Arrange
		RecordingDumpSink sink = new();
		CallToolResult callResult = new() {
			IsError = false,
			Content = []
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "empty", sink);

		// Assert
		description.Should().Contain("IsError=False",
			because: "the call did not report an error");
		description.Should().Contain("StructuredContent=(none)",
			because: "no StructuredContent was present on the result");
		description.Should().Contain("Content=(none)",
			because: "an empty Content array carries no items to describe");
	}

	[Test]
	[Description("Reports how many content blocks arrived, which is what separates 'the tool answered nothing' from 'the tool answered something unreadable'.")]
	public void Describe_ShouldReportContentBlockCount_WhenResultCarriesBlocks() {
		// Arrange
		RecordingDumpSink sink = new();
		CallToolResult callResult = new() {
			IsError = true,
			Content = [
				new TextContentBlock { Text = "alpha-block-marker" },
				new TextContentBlock { Text = "beta-block-marker" }
			]
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "two-blocks", sink);

		// Assert
		description.Should().Contain("Content=2 block(s)",
			because: "the block count is the part of the payload's shape that is safe in a build log by construction and still tells the reader the tool answered something");
		description.Should().NotContain("alpha-block-marker",
			because: "the blocks' text belongs in the dump file, not in a message that reaches the CI log");
	}

	[Test]
	[Description("Writes the payload to the dump verbatim, with no redaction, bounding or reshaping of any kind.")]
	public void Describe_ShouldDumpThePayloadVerbatim_WhenResultCannotBeParsed() {
		// Arrange: a value the previous design would have replaced with [redacted] on its way out.
		const string serverText = "Auth rejected for /Users/alex/.clio/appsettings.json password=hunter2";
		RecordingDumpSink sink = new();
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = serverText }]
		};

		// Act
		McpResultDiagnostics.DescribeWithSink(callResult, null, "verbatim", sink);

		// Assert
		sink.Writes.Should().ContainSingle(
			because: "one parse failure produces exactly one dump");
		sink.Writes[0].Payload.Should().Contain(serverText,
			because: "the dump is the full-fidelity record of what arrived; redacting or bounding it is precisely what destroyed the diagnostic under CI load (#1537)");
		sink.Writes[0].Payload.Should().NotContain("[redacted",
			because: "no redaction rule may run on the payload's way to the file");
	}

	[Test]
	[Description("Names the dump's path in the message, so the reader can find the artifact without another CI run.")]
	public void Describe_ShouldNameTheDumpPath_WhenTheWriteSucceeds() {
		// Arrange
		RecordingDumpSink sink = new() { PathToReturn = "/tmp/TestResults/mcp-payloads/dump.json" };
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = "anything" }]
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "named", sink);

		// Assert
		description.Should().Contain("Payload=\"/tmp/TestResults/mcp-payloads/dump.json\"",
			because: "naming the file is the whole mechanism by which the payload stays reachable while staying out of the log, and quoting it is what lets a caller append its own text after the description without making the path unrecoverable");
	}

	[Test]
	[Description("Keeps a multi-megabyte payload out of the message entirely while the dump still holds every byte of it.")]
	public void Describe_ShouldKeepTheMessageSmall_WhenPayloadIsMultiMegabyte() {
		// Arrange: the payload that made the previous design collapse into a bare [redacted] placeholder
		// once the redactor's one-second budget expired on a loaded agent.
		const string leadingErrorText = "Access denied for user.";
		string hugePayload = leadingErrorText + new string('x', 3_000_000);
		RecordingDumpSink sink = new();
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = hugePayload }]
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "huge", sink);

		// Assert
		description.Length.Should().BeLessThan(500,
			because: "the message carries metadata and a path, so its length no longer scales with the payload's at all - which is also what proves no redaction rule ran over those three megabytes, since a rule that ran would have had to produce text");
		sink.Writes[0].Payload.Should().Contain(hugePayload,
			because: "the dump holds the payload WHOLE, beginning included; the 64 000-character bound that used to keep only its start is gone");
	}

	[Test]
	[Description("Falls back to a bounded excerpt naming the reason when the dump cannot be written, rather than leaving the reader with no payload at all.")]
	public void Describe_ShouldFallBackToAnExcerpt_WhenTheDumpCannotBeWritten() {
		// Arrange
		RecordingDumpSink sink = new() { FailureReasonToReturn = "UnauthorizedAccessException: Access to the path is denied." };
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = "the server reported an authentication failure" }]
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "unwritable", sink);

		// Assert
		description.Should().Contain("dump failed: UnauthorizedAccessException",
			because: "a dump that did not happen must say so; a path-shaped message for a file that does not exist sends the reader hunting a missing artifact");
		description.Should().Contain("the server reported an authentication failure",
			because: "a failed write must DEGRADE the diagnostic, not erase it - returning nothing would reinstate the blindness #1384 removed");
	}

	[Test]
	[Description("Bounds the write-failure excerpt, so an unwritable dump cannot flood the log with the payload the file was meant to hold.")]
	public void Describe_ShouldBoundTheExcerpt_WhenTheDumpFailsOnAHugePayload() {
		// Arrange
		RecordingDumpSink sink = new() { FailureReasonToReturn = "IOException: No space left on device." };
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = new string('y', 3_000_000) }]
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "huge-unwritable", sink);

		// Assert
		description.Length.Should().BeLessThan(McpResultDiagnostics.LogFragmentLimit + 500,
			because: "the fallback excerpt is the one payload-shaped text still allowed into the log, so it stays inside the fragment budget the redaction pass is fast at");
		description.Should().Contain("characters total, truncated to fit",
			because: "the cut must be explicit rather than a silent trim");
	}

	[Test]
	[Description("Names the dump file after the caller's own prefix, so the artifact belonging to a failure is identifiable without opening it.")]
	public void DescribePrefixed_ShouldNameTheDumpAfterTheCallerPrefix() {
		// Arrange: the real DescribePrefixed, through the real sink, because the property under test is
		// that the prefix reaches the FILE NAME - which a call passing the label explicitly would not
		// prove, and which would survive DescribePrefixed quietly labelling every dump the same.
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = "unparsable" }]
		};

		// Act
		string message = McpResultDiagnostics.DescribePrefixed(
			"Could not parse list-apps MCP result: ", callResult);

		// Assert: cleanup in a finally, not after the assertions - FluentAssertions throws on the first
		// failure, and a delete placed below them is skipped on exactly the runs whose artifact matters.
		try {
			string? path = PayloadDumpReader.ExtractPath(message);
			path.Should().NotBeNull(because: "a successful write must name its file in the message");
			Path.GetFileName(path!).Should().Contain("could-not-parse-list-apps-mcp-result",
				because: "the caller's sentence is the only thing on this path that says which tool failed, so it is what makes one dump distinguishable from another in the published artifact");
		}
		finally {
			PayloadDumpReader.DeleteIfPresent(message);
		}
	}

	[Test]
	[Description("Includes the last JsonException's redacted message and it is only appended when the caller supplies one.")]
	public void Describe_ShouldIncludeLastJsonError_WhenCallerSuppliesOne() {
		// Arrange
		RecordingDumpSink sink = new();
		CallToolResult callResult = new() { IsError = false, Content = [] };
		JsonException lastJsonException;
		try {
			JsonSerializer.Deserialize<int>("not-json");
			throw new InvalidOperationException("Expected a JsonException to be thrown by the arrange step.");
		}
		catch (JsonException exception) {
			lastJsonException = exception;
		}

		// Act
		string descriptionWithException =
			McpResultDiagnostics.DescribeWithSink(callResult, lastJsonException, "with", sink);
		string descriptionWithoutException =
			McpResultDiagnostics.DescribeWithSink(callResult, null, "without", sink);

		// Assert
		descriptionWithException.Should().Contain("LastJsonError=\"",
			because: "the exception discarded by the old catch block is the single most useful fact about a parse failure (#1384)");
		descriptionWithoutException.Should().NotContain("LastJsonError=",
			because: "a caller that tracked no exception must not get an empty field suggesting one existed");
	}

	[Test]
	[Description("Redacts the JsonException message, which unlike the payload does reach the build log.")]
	public void Describe_ShouldRedactTheLastJsonError_WhenItCarriesAnAbsolutePath() {
		// Arrange
		RecordingDumpSink sink = new();
		CallToolResult callResult = new() { IsError = false, Content = [] };
		JsonException lastJsonException = new("Failed reading /Users/alex/secrets/credentials.json");

		// Act
		string description =
			McpResultDiagnostics.DescribeWithSink(callResult, lastJsonException, "redacted-error", sink);

		// Assert
		description.Should().NotContain("/Users/alex/secrets/credentials.json",
			because: "the payload is exempt from redaction because it goes to a file; this string goes to the build log, so it is not");
		description.Should().Contain("[redacted-path]",
			because: "the log fragment is redacted with the production rules, at a size whose cost is about a millisecond against their one-second budget");
	}

	[Test]
	[Description("Returns a diagnostics-unavailable note instead of throwing when describing the payload itself fails, so the original parse failure is never masked.")]
	public void Describe_ShouldReportUnavailable_WhenSerializingThePayloadThrows() {
		// Arrange
		RecordingDumpSink sink = new();
		CallToolResult callResult = new() {
			IsError = true,
			Content = new ThrowingContentList()
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "throwing", sink);

		// Assert
		description.Should().StartWith("(diagnostics unavailable:",
			because: "this method composes the explanation of another failure, so it must never replace that failure with one of its own");
		description.Should().MatchRegex(@"\(diagnostics unavailable: \w+Exception: ",
			because: "naming the type of the failure is what makes an unavailable diagnostic actionable rather than merely silent");
	}

	[Test]
	[Description("Returns the no-result note without touching the sink when there was no result at all.")]
	public void Describe_ShouldReportNoResult_WhenCallResultIsNull() {
		// Arrange
		RecordingDumpSink sink = new();

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(null, null, "absent", sink);

		// Assert
		description.Should().Be("(no result)",
			because: "there is nothing to dump when no result ever arrived");
		sink.Writes.Should().BeEmpty(
			because: "writing an empty file for a call that produced no result would leave a misleading artifact behind");
	}

	[Test]
	[Description("Drops a quoted value the excerpt bound cut through, so half a secret cannot reach the build log where a whole one would have been redacted.")]
	public void Describe_ShouldDropAValueTheExcerptBoundCutThrough_WhenTheDumpFails() {
		// Arrange: a password positioned so the 1 000-character excerpt bound lands INSIDE its value.
		// The production rule states as an accepted limit that a value sliced before its closing quote
		// is not matched, so without the drop this reaches the log verbatim.
		RecordingDumpSink sink = new() { FailureReasonToReturn = "IOException: No space left on device." };
		string padding = new('p', McpResultDiagnostics.LogFragmentLimit);
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = $"{padding}\"password\":\"s3cr3tValue" }]
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "cut-secret", sink);

		// Assert
		description.Should().NotContain("s3cr3t",
			because: "a secret the bound cut in half matches no redaction rule, so the cut value must be discarded rather than shipped");
	}

	[Test]
	[Description("Reports that StructuredContent was present, which separates a shape mismatch in the structured channel from a result that carried nothing.")]
	public void Describe_ShouldReportStructuredContentPresent_WhenResultCarriesIt() {
		// Arrange
		RecordingDumpSink sink = new();
		CallToolResult callResult = new() {
			IsError = false,
			Content = [],
			StructuredContent = JsonSerializer.SerializeToElement(new { code = 7 })
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "structured", sink);

		// Assert
		description.Should().Contain("StructuredContent=present",
			because: "a reader has to be able to tell which channel carried the unreadable answer before opening the dump");
		sink.Writes[0].Payload.Should().Contain("\"code\":7",
			because: "the structured channel must reach the dump too, not only the content blocks");
	}

	[Test]
	[Description("Dumps a content block that carries no text string (an image block), which the old renderer named explicitly and which now rests on the SDK's own serialization.")]
	public void Describe_ShouldDumpABlockWithNoTextString_WhenContentCarriesAnImage() {
		// Arrange
		RecordingDumpSink sink = new();
		CallToolResult callResult = new() {
			IsError = true,
			Content = [
				new ImageContentBlock {
					Data = new ReadOnlyMemory<byte>("hello"u8.ToArray()),
					MimeType = "image/png"
				}
			]
		};

		// Act
		string description = McpResultDiagnostics.DescribeWithSink(callResult, null, "image", sink);

		// Assert
		description.Should().Contain("Content=1 block(s)",
			because: "a block with no text is still a block the tool answered with, and must be counted rather than silently skipped");
		sink.Writes[0].Payload.Should().Contain("image/png",
			because: "an image or embedded-resource block is exactly the answer the old '(no text)' rendering made undiagnosable, so its own fields have to survive into the dump");
	}

	[Test]
	[Description("Truncates text longer than the documented cap and reports the total original length, instead of flooding the CI log unbounded.")]
	public void Truncate_ShouldCapAndReportTotalLength_WhenTextExceedsTheLimit() {
		// Arrange
		const int limit = 80;
		string text = new('z', 500);

		// Act
		string truncated = McpResultDiagnostics.Truncate(text, limit);

		// Assert
		truncated.Length.Should().Be(limit,
			because: "the note is paid for out of the budget rather than added on top of it");
		truncated.Should().EndWith($" … {text.Length} characters total, truncated to fit {limit}",
			because: "the note must report both the original length and the budget, so the reader knows how much was cut");
	}

	[Test]
	[Description("Leaves text at or under the documented cap unchanged, the boundary case included.")]
	public void Truncate_ShouldReturnTextUnchanged_WhenTextIsAtOrUnderTheLimit() {
		// Arrange
		const int limit = 80;
		const string shorterThanLimit = "short enough";
		string exactlyAtLimit = new('z', limit);

		// Act
		string shorterResult = McpResultDiagnostics.Truncate(shorterThanLimit, limit);
		string exactResult = McpResultDiagnostics.Truncate(exactlyAtLimit, limit);

		// Assert
		shorterResult.Should().Be(shorterThanLimit,
			because: "text inside the budget must not gain a truncation note it did not earn");
		exactResult.Should().Be(exactlyAtLimit,
			because: "the comparison is <=, so text landing exactly on the budget is untouched - an off-by-one here would append a note that makes the result LONGER than the cap it reports");
	}

	[Test]
	[Description("Does not leave a lone high surrogate when the cap falls inside a surrogate pair.")]
	public void Truncate_ShouldNotSplitASurrogatePair_WhenTheCapFallsInsideOne() {
		// Arrange: one leading char shifts the emoji run onto odd offsets, so the cut provably lands
		// BETWEEN the two halves of one pair rather than happening to miss it.
		const int limit = 60;
		string emojiRun = "a" + string.Concat(Enumerable.Repeat("\U0001F600", 100));
		string note = $" … {emojiRun.Length} characters total, truncated to fit {limit}";

		// Act
		string truncated = McpResultDiagnostics.Truncate(emojiRun, limit);

		// Assert
		string kept = truncated[..^note.Length];
		kept.Length.Should().Be(limit - note.Length - 1,
			because: "the cut fell inside a surrogate pair, so exactly one orphaned half must have been dropped - if it were not, this test would pass without testing anything");
		char.IsHighSurrogate(kept[^1]).Should().BeFalse(
			because: "a lone high surrogate is invalid UTF-16 and makes whatever serializes the message next throw instead of reporting the failure");
	}

	[Test]
	[Description("Keeps the documented cap even when the sanitizer lengthens the kept text rather than only shortening it.")]
	public void Truncate_ShouldStillCapTheResult_WhenTheSanitizerGrowsTheKeptText() {
		// Arrange: a sanitizer that appends more than it removes, which is what the only real one does -
		// DropValueCutByTheBound swaps an unterminated value for a 42-character marker.
		const int limit = 120;
		string text = new('z', 500);

		// Act
		string truncated = McpResultDiagnostics.Truncate(text, limit, kept => kept + new string('!', 200));

		// Assert
		truncated.Length.Should().Be(limit,
			because: "the limit is documented as the maximum length of the RESULT, so a sanitizer that grows the kept text must be re-cut rather than allowed to overflow the bound that keeps payload-shaped text inside the redaction pass's cheap range");
		truncated.Should().EndWith($" … {text.Length} characters total, truncated to fit {limit}",
			because: "the note must survive the second cut; trimming it away would leave a silent truncation");
	}

	/// <summary>
	/// A sink that records what would have been written instead of writing it, so the payload assertions
	/// never touch the filesystem.
	/// </summary>
	private sealed class RecordingDumpSink : IMcpPayloadDumpSink {
		/// <summary>The path reported back on a successful write.</summary>
		public string PathToReturn { get; init; } = "/recorded/dump.json";

		/// <summary>When set, the write reports this failure instead of succeeding.</summary>
		public string? FailureReasonToReturn { get; init; }

		/// <summary>Every write this sink received, in order.</summary>
		public List<(string Label, string Payload)> Writes { get; } = [];

		public McpPayloadDumpResult Write(string label, string rawPayload) {
			Writes.Add((label, rawPayload));
			return FailureReasonToReturn is null
				? new McpPayloadDumpResult(PathToReturn, null)
				: new McpPayloadDumpResult(null, FailureReasonToReturn);
		}
	}

	/// <summary>
	/// A content collection whose enumeration throws, standing in for any payload whose serialization
	/// fails on this path (a lazily materialized list, a cyclic graph, a throwing property getter).
	/// </summary>
	private sealed class ThrowingContentList : IList<ContentBlock> {
		public ContentBlock this[int index] {
			get => throw new NotSupportedException("Materializing the content blocks is not supported.");
			set => throw new NotSupportedException("Materializing the content blocks is not supported.");
		}

		public int Count => 1;

		public bool IsReadOnly => true;

		public IEnumerator<ContentBlock> GetEnumerator() =>
			throw new NotSupportedException("Materializing the content blocks is not supported.");

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

		public void Add(ContentBlock item) => throw new NotSupportedException();

		public void Clear() => throw new NotSupportedException();

		public bool Contains(ContentBlock item) => throw new NotSupportedException();

		public void CopyTo(ContentBlock[] array, int arrayIndex) => throw new NotSupportedException();

		public int IndexOf(ContentBlock item) => throw new NotSupportedException();

		public void Insert(int index, ContentBlock item) => throw new NotSupportedException();

		public bool Remove(ContentBlock item) => throw new NotSupportedException();

		public void RemoveAt(int index) => throw new NotSupportedException();
	}

	[Test]
	[Description("Keeps a JSON array as a meaningful candidate when the expected type is array-shaped, so the parsers whose target genuinely is an array still get a LastJsonError.")]
	public void RecordDeserializeAttempt_ShouldKeepTheArray_WhenTheExpectedTypeIsArrayShaped() {
		// Arrange
		McpParseDiagnostics diagnostics = new();
		JsonElement array = JsonDocument.Parse("[{\"Name\":\"dev\"}]").RootElement;
		JsonException failure = new("deserialization failed");

		// Act
		bool isMeaningful = diagnostics.RecordDeserializeAttempt(array, typeof(ShowWebAppListEnvironmentEnvelope[]));
		diagnostics.RecordJsonException(failure, isMeaningful);

		// Assert
		isMeaningful.Should().BeTrue(
			because: "for ShowWebAppListEnvelope.TryDeserialize and Extract<T> with a collection T, an array IS the expected shape, not the MCP content-item wrapper falling through");
		diagnostics.LastJsonException.Should().BeSameAs(failure,
			because: "suppressing it there reproduced the swallowed-exception state issue #1384 exists to remove - a malformed environment entry failed with no LastJsonError=");
		diagnostics.SawValidJson.Should().BeTrue(
			because: "the array was a plausible candidate for the expected type, so the failure shape must read as a shape mismatch rather than as no JSON");
	}

	[Test]
	[Description("Still discards the doomed content-item wrapper exception when the expected type is object-shaped, so the array-shaped fix does not reintroduce blaming the wrapper.")]
	public void RecordDeserializeAttempt_ShouldDiscardTheArray_WhenTheExpectedTypeIsObjectShaped() {
		// Arrange
		McpParseDiagnostics diagnostics = new();
		JsonElement array = JsonDocument.Parse("[{\"type\":\"text\",\"text\":\"{}\"}]").RootElement;

		// Act
		bool isMeaningful = diagnostics.RecordDeserializeAttempt(array, typeof(ShowWebAppListEnvironmentEnvelope));
		diagnostics.RecordJsonException(new JsonException("doomed"), isMeaningful);

		// Assert
		isMeaningful.Should().BeFalse(
			because: "an array reaching an object-shaped parser is the already-unpacked content-item wrapper, known not to match");
		diagnostics.LastJsonException.Should().BeNull(
			because: "the wrapper's always-doomed exception must not be blamed for a mismatch the real payload caused");
		diagnostics.SawAnyJson.Should().BeTrue(
			because: "suppressing the blame must not make the parser claim there was no JSON beside a dump of that very array");
	}

	[Test]
	[Description("A string is enumerable but deserializes from a JSON string, so it must not count as array-shaped.")]
	public void RecordDeserializeAttempt_ShouldNotTreatStringAsArrayShaped() {
		// Arrange
		McpParseDiagnostics diagnostics = new();
		JsonElement array = JsonDocument.Parse("[\"a\"]").RootElement;

		// Act
		bool isMeaningful = diagnostics.RecordDeserializeAttempt(array, typeof(string));

		// Assert
		isMeaningful.Should().BeFalse(
			because: "string implements IEnumerable but is deserialized from a JSON string, so an array reaching it is still the wrapper");
	}
}
