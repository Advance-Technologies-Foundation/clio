using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="ClassicEnumVocabularySourceParser"/> — the brace-matched, never-executed extraction of
/// <c>Terrasoft.ViewItemType</c>/<c>ContentType</c>/<c>DataValueType</c> out of a target stand's own
/// <c>sysenums.js</c>, which feeds the classic-to-freedom-migration engine's enum-drift guard (ENG-95412).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ClassicEnumVocabularySourceParserTests {

	private ClassicEnumVocabularySourceParser _parser;

	[SetUp]
	public void Setup() => _parser = new ClassicEnumVocabularySourceParser();

	private const string ViewItemTypeBlock =
		"Terrasoft.ViewItemType = {\n" +
		"\t/** Grid element. */\n" +
		"\tGRID_LAYOUT: 0,\n" +
		"\t/** Drop-down list separator. */\n" +
		"\tMENU_SEPARATOR: 10,\n" +
		"\t/** External widget*/\n" +
		"\tEXTERNAL_WIDGET: 33\n" +
		"};\n" +
		"Terrasoft.core.enums.ViewItemType = Terrasoft.ViewItemType;\n";

	private const string ContentTypeBlock =
		"Terrasoft.ContentType = {\n" +
		"\t/** Long string. */\n" +
		"\tLONG_TEXT: 0,\n" +
		"\t/** Searchable text. */\n" +
		"\tSEARCHABLE_TEXT: 6\n" +
		"};\n";

	private const string DataValueTypeBlock =
		"Terrasoft.DataValueType = {\n" +
		"\t/** Global unique identifier. */\n" +
		"\tGUID: 0,\n" +
		"\t/** String. */\n" +
		"\tTEXT: 1,\n" +
		"\t/** Currency. */\n" +
		"\tMONEY0: 6\n" +
		"};\n";

	[Test]
	[Description("Parse extracts all three enum tables verbatim (member name -> numeric value) from a well-formed sysenums.js excerpt.")]
	public void Parse_ShouldExtractAllThreeTables_WhenSourceIsWellFormed() {
		// Arrange
		string source = ViewItemTypeBlock + ContentTypeBlock + DataValueTypeBlock;

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums.Keys.Should().BeEquivalentTo(["ViewItemType", "ContentType", "DataValueType"],
			because: "all three DRIFT_TABLES enums are present and well-formed in the source");
		result.Enums["ViewItemType"].Should().BeEquivalentTo(
			new Dictionary<string, long> { ["GRID_LAYOUT"] = 0, ["MENU_SEPARATOR"] = 10, ["EXTERNAL_WIDGET"] = 33 },
			because: "member names and numeric values must be echoed verbatim as core spells them");
		result.Enums["DataValueType"].Should().ContainKey("MONEY0")
			.WhoseValue.Should().Be(6, because: "member names are emitted verbatim, including a digit suffix like MONEY0");
		result.Warnings.Should().BeEmpty(because: "a complete, parseable source produces no degradation to report");
	}

	[Test]
	[Description("Parse omits the whole enumVocabulary result (empty dictionary, no crash) and warns when the content is empty, mirroring a stand that served nothing.")]
	public void Parse_ShouldReturnEmptyWithWarning_WhenContentIsMissing() {
		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(string.Empty);

		// Assert
		result.Enums.Should().BeEmpty(because: "there is no source text to extract any enum table from");
		result.Warnings.Should().ContainSingle(because: "a missing/empty file is a single, explainable degradation, not a crash");
	}

	[Test]
	[Description("Parse omits only the truncated enum (unterminated object literal) and still extracts the other two complete tables, warning about the truncated one.")]
	public void Parse_ShouldOmitOnlyTruncatedEnum_WhenOneBlockIsUnterminated() {
		// Arrange — ViewItemType's object literal never closes (simulates a stand response cut off mid-file).
		string truncatedViewItemType = "Terrasoft.ViewItemType = {\n\tGRID_LAYOUT: 0,\n\tTAB_PANEL: 1,\n";
		string source = truncatedViewItemType + ContentTypeBlock + DataValueTypeBlock;

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums.Should().NotContainKey("ViewItemType",
			because: "an unterminated object literal cannot be safely bounded and must degrade to an omission, never a partial/garbage object");
		result.Enums.Keys.Should().BeEquivalentTo(["ContentType", "DataValueType"],
			because: "the two well-formed blocks are unaffected by the truncation of a sibling block");
		result.Warnings.Should().ContainSingle(w => w.Contains("ViewItemType"),
			because: "the omission must be explained to an MCP caller, whose log buffer is not available");
	}

	[Test]
	[Description("Parse omits an absent enum block entirely (no key, no empty object) and still returns the two present tables.")]
	public void Parse_ShouldOmitAbsentEnum_WhenBlockDoesNotExist() {
		// Arrange — DataValueType simply never appears in this excerpt.
		string source = ViewItemTypeBlock + ContentTypeBlock;

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums.Should().NotContainKey("DataValueType",
			because: "the consumer treats an absent table as 'skip this enum', never as an empty object with junk in it");
		result.Enums.Keys.Should().BeEquivalentTo(["ViewItemType", "ContentType"],
			because: "the two present tables are unaffected by a sibling table's absence");
		result.Warnings.Should().ContainSingle(w => w.Contains("DataValueType"),
			because: "the warning must name which enum was omitted, not just that something was");
	}

	[Test]
	[Description("Parse ignores non-numeric members and blocked prototype-shaped member names, never echoing them into the manifest.")]
	public void Parse_ShouldIgnoreNonNumericAndPrototypeShapedMembers() {
		// Arrange — a hostile/malformed block mixing valid numeric members with a string value and prototype-shaped keys.
		string source =
			"Terrasoft.ViewItemType = {\n" +
			"\tGRID_LAYOUT: 0,\n" +
			"\tBAD_STRING_MEMBER: \"not a number\",\n" +
			"\t__proto__: 999,\n" +
			"\tconstructor: 998,\n" +
			"\ttoString: 997,\n" +
			"\tTAB_PANEL: 1\n" +
			"};\n";

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums["ViewItemType"].Should().BeEquivalentTo(
			new Dictionary<string, long> { ["GRID_LAYOUT"] = 0, ["TAB_PANEL"] = 1 },
			because: "the engine's consumer reads with Object.hasOwn, but this parser must never emit a " +
				"prototype-shaped key or a non-numeric value regardless of the consumer's own guard");
	}

	[Test]
	[Description("Parse ignores comment text that happens to contain a colon and a digit, so a JSDoc line is never mistaken for a member.")]
	public void Parse_ShouldIgnoreColonsInsideComments() {
		// Arrange
		string source =
			"Terrasoft.ContentType = {\n" +
			"\t/** Default: 0 is the fallback for legacy pages. */\n" +
			"\tLONG_TEXT: 5\n" +
			"};\n";

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums["ContentType"].Should().BeEquivalentTo(
			new Dictionary<string, long> { ["LONG_TEXT"] = 5 },
			because: "the only real member is LONG_TEXT:5 — a comment's own colon/digit text must not be parsed as a second member");
	}

	[Test]
	[Description("Parse treats a duplicate member within one block using the last occurrence, mirroring plain JS object-literal semantics.")]
	public void Parse_ShouldKeepLastOccurrence_WhenMemberIsDuplicated() {
		// Arrange
		string source = "Terrasoft.DataValueType = {\n\tGUID: 0,\n\tGUID: 42\n};\n";

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums["DataValueType"]["GUID"].Should().Be(42,
			because: "a plain JS object literal resolves a duplicate key to its last-written value");
	}

	[Test]
	[Description("Parse does not fabricate a phantom member from a colon+digit inside a quoted string value, and does not lose a real member that follows one on the same line.")]
	public void Parse_ShouldIgnoreColonAndDigitInsideStringLiteralValue() {
		// Arrange — a hostile/malformed member whose STRING value itself looks like a second member ("LEGACY: 2").
		string source =
			"Terrasoft.ContentType = {\n" +
			"\tLONG_TEXT: 0,\n" +
			"\tDESC: \"see LEGACY: 2 instead\",\n" +
			"\tSEARCHABLE_TEXT: 6\n" +
			"};\n";

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums["ContentType"].Should().BeEquivalentTo(
			new Dictionary<string, long> { ["LONG_TEXT"] = 0, ["SEARCHABLE_TEXT"] = 6 },
			because: "a colon+digit sequence inside a quoted string value is not a member — the parser must not fabricate " +
				"a phantom 'LEGACY: 2' entry from it, nor let the string's own content masquerade as anything else");
	}

	[Test]
	[Description("Parse does not lose a real member when a preceding string value happens to contain '//' (e.g. a URL), which a naive line-comment strip would misread as starting a comment.")]
	public void Parse_ShouldNotTreatSlashSlashInsideStringLiteralAsLineComment() {
		// Arrange — "http://x" inside a string is NOT a line comment; a regex-only comment strip that ignores string
		// boundaries would delete everything from that "//" to end of line, including the real member after it.
		string source =
			"Terrasoft.DataValueType = {\n" +
			"\tURL_LIKE: 1,\n" +
			"\tLINK: \"see http://example\", REAL_MEMBER: 7\n" +
			"};\n";

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums["DataValueType"].Should().ContainKey("REAL_MEMBER")
			.WhoseValue.Should().Be(7,
				because: "a '//' occurring inside a string literal value must not be mistaken for the start of a line comment " +
					"that would otherwise delete the real member written after it on the same line");
	}

	[Test]
	[Description("Parse correctly bounds an object literal whose member value contains an UNBALANCED closing brace inside a string, without truncating the block early.")]
	public void Parse_ShouldNotDesyncBraceCount_WhenStringValueContainsUnbalancedBrace() {
		// Arrange — a brace-matcher that is NOT string-aware would hit the lone `}` inside this string value, read it
		// as closing the outer object literal, and truncate the block right there — silently losing SEARCHABLE_TEXT
		// and any well-formed trailing content.
		string source =
			"Terrasoft.ContentType = {\n" +
			"\tLONG_TEXT: 0,\n" +
			"\tLABEL: \"unexpected } inside string\",\n" +
			"\tSEARCHABLE_TEXT: 6\n" +
			"};\n";

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums["ContentType"].Should().BeEquivalentTo(
			new Dictionary<string, long> { ["LONG_TEXT"] = 0, ["SEARCHABLE_TEXT"] = 6 },
			because: "an unbalanced brace character inside a string value must not be read as closing the outer object literal");
	}

	[Test]
	[Description("Parse correctly bounds an object literal past a JSDoc comment containing its own UNBALANCED closing brace, without the comment's internal brace desynchronizing the depth count.")]
	public void Parse_ShouldNotDesyncBraceCount_WhenCommentContainsUnbalancedBrace() {
		// Arrange — a comment-skipper that stopped recognizing `/* */` (or never did) would hit the lone `}` inside
		// this comment, read it as closing the outer object literal, and truncate the block right there.
		string source =
			"Terrasoft.ViewItemType = {\n" +
			"\t/** unexpected } inside a comment, e.g. a stray JSDoc {@link tag} fragment */\n" +
			"\tGRID_LAYOUT: 0,\n" +
			"\tTAB_PANEL: 1\n" +
			"};\n";

		// Act
		ClassicEnumVocabularyParseResult result = _parser.Parse(source);

		// Assert
		result.Enums["ViewItemType"].Should().BeEquivalentTo(
			new Dictionary<string, long> { ["GRID_LAYOUT"] = 0, ["TAB_PANEL"] = 1 },
			because: "an unbalanced brace inside a skipped comment must not be read as closing the outer object literal");
	}
}
