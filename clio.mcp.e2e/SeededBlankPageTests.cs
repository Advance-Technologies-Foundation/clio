using System.Text.Json;
using Clio.Mcp.E2E.Support.Creatio;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// Unit tests for the decisions <see cref="SeededBlankPage"/> makes without a stand: whether a body read from
/// the shared page <c>ClioMcp_BlankPageToSave</c> is its blank body, and how the stored <c>SysSchema</c> rows
/// of the page are reported. They need no MCP server and no Creatio, so they are categorized <c>Unit</c>
/// rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SeededBlankPageTests {

	private const string EmptyViewConfigDiff = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/";

	[Test]
	[Description("The body the reset writes is itself recognized as blank, so a reset page is not reset again by the next test.")]
	public void IsBlank_ShouldAcceptTheBodyTheResetWrites() {
		// Arrange
		string body = SeededBlankPage.BuildBlankBody();

		// Act
		bool blank = SeededBlankPage.IsBlank(body, out string? reason);

		// Assert
		blank.Should().BeTrue(
			because: "a reset that wrote a body the check rejects would rewrite the page before every test. Reason: {0}",
			reason);
		reason.Should().BeNull(because: "a blank body has nothing to report");
	}

	[Test]
	[Description("Whitespace inside the empty sections and after the module does not make a blank body look written.")]
	public void IsBlank_ShouldIgnoreWhitespace_InEmptySectionsAndAfterTheModule() {
		// Arrange
		string body = SeededBlankPage.BuildBlankBody()
			.Replace(EmptyViewConfigDiff, "/**SCHEMA_VIEW_CONFIG_DIFF*/ [ ]\r\n/**SCHEMA_VIEW_CONFIG_DIFF*/")
			+ "\r\n\r\n";

		// Act
		bool blank = SeededBlankPage.IsBlank(body, out string? reason);

		// Assert
		blank.Should().BeTrue(
			because: "the stand may store the same blank body with other whitespace. Reason: {0}", reason);
	}

	[Test]
	[Description("A component left in viewConfigDiff by an earlier test makes the body not blank, and the reason names the section and its content.")]
	public void IsBlank_ShouldRejectABodyThatCarriesAComponent() {
		// Arrange
		string body = SeededBlankPage.BuildBlankBody().Replace(
			EmptyViewConfigDiff,
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrE2ESyncPersistedField\"}]/**SCHEMA_VIEW_CONFIG_DIFF*/");

		// Act
		bool blank = SeededBlankPage.IsBlank(body, out string? reason);

		// Assert
		blank.Should().BeFalse(
			because: "this is the body later tests read in TeamCity build 16330482 and could not build a probe from");
		reason.Should().Contain("SCHEMA_VIEW_CONFIG_DIFF",
			because: "the reason must name the section that is not empty");
		reason.Should().Contain("UsrE2ESyncPersistedField",
			because: "the reason must show what the section holds, which names the test that left it");
	}

	[Test]
	[Description("The comment the visibility test appends after the module makes the body not blank.")]
	public void IsBlank_ShouldRejectABodyWithContentAfterTheModule() {
		// Arrange
		string body = SeededBlankPage.BuildBlankBody() + "\n// clio-sync-visibility-0123456789abcdef\n";

		// Act
		bool blank = SeededBlankPage.IsBlank(body, out string? reason);

		// Assert
		blank.Should().BeFalse(
			because: "every section is still empty, so only the text after the module shows the marker was left behind");
		reason.Should().Contain("clio-sync-visibility",
			because: "the reason must show the leftover content");
	}

	[Test]
	[Description("A body without one of the marker pairs, and an empty body, are not blank: no probe can be built from them.")]
	public void IsBlank_ShouldRejectABodyWithoutAMarkerPair_AndAnEmptyBody() {
		// Arrange
		string withoutHandlers = SeededBlankPage.BuildBlankBody()
			.Replace("/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/", "[]");

		// Act
		bool missingMarkerIsBlank = SeededBlankPage.IsBlank(withoutHandlers, out string? missingMarkerReason);
		bool emptyIsBlank = SeededBlankPage.IsBlank("  ", out string? emptyReason);

		// Assert
		missingMarkerIsBlank.Should().BeFalse(because: "a body that lost a marker pair is not the blank page");
		missingMarkerReason.Should().Contain("SCHEMA_HANDLERS", because: "the reason must name the missing pair");
		emptyIsBlank.Should().BeFalse(because: "a read that returned nothing proves nothing about the page");
		emptyReason.Should().Contain("empty", because: "the reason must say the body was empty");
	}

	[Test]
	[Description("Two stored rows of the page in one package are reported as such, because a one-row name lookup can then resolve either of them.")]
	public void DescribeStoredLayers_ShouldFlagTwoRowsInOnePackage() {
		// Arrange
		using JsonDocument rows = JsonDocument.Parse("""
			[
				{"UId":"11111111-1111-1111-1111-111111111111","PackageName":"AutoTestClioMcp","PackageUId":"a","ExtendParent":false,"Checksum":"c1"},
				{"UId":"22222222-2222-2222-2222-222222222222","PackageName":"Custom","PackageUId":"b","ExtendParent":true,"Checksum":"c2"},
				{"UId":"33333333-3333-3333-3333-333333333333","PackageName":"Custom","PackageUId":"b","ExtendParent":true,"Checksum":"c3"}
			]
			""");

		// Act
		string description = SeededBlankPage.DescribeStoredLayers(rows.RootElement);

		// Assert
		description.Should().Contain("MORE THAN ONE ROW IN ONE PACKAGE (Custom x2)",
			because: "the duplicated package and the number of rows in it are the observation the investigation needs");
		description.Should().Contain("22222222-2222-2222-2222-222222222222").And
			.Contain("33333333-3333-3333-3333-333333333333",
				because: "each row is listed with its schema UId so a read and a write can be matched to a row by checksum");
	}

	[Test]
	[Description("One stored row per package - the page and its replacing schema - is reported without the duplicate warning.")]
	public void DescribeStoredLayers_ShouldNotFlagOneRowPerPackage() {
		// Arrange
		using JsonDocument rows = JsonDocument.Parse("""
			[
				{"UId":"11111111-1111-1111-1111-111111111111","PackageName":"AutoTestClioMcp","PackageUId":"a","ExtendParent":false,"Checksum":"c1"},
				{"UId":"22222222-2222-2222-2222-222222222222","PackageName":"Custom","PackageUId":"b","ExtendParent":true,"Checksum":"c2"}
			]
			""");

		// Act
		string description = SeededBlankPage.DescribeStoredLayers(rows.RootElement);

		// Assert
		description.Should().Contain("SysSchema rows: 2, at most one per package",
			because: "a page with one replacing schema is the normal state of the stand");
		description.Should().NotContain("MORE THAN ONE ROW",
			because: "the warning must appear only for a real duplicate");
	}

	[Test]
	[Description("A failure message that carries the page diagnostics keeps their JSON braces: FluentAssertions must not read them as format placeholders.")]
	public void FailureMessage_ShouldKeepJsonDiagnostics() {
		// Arrange
		const string diagnostics = "schema chain: [{\"schemaName\":\"ClioMcp_BlankPageToSave\",\"hasBody\":true}]";

		// Act
		Action act = () => false.Should().BeTrue(because: "the reset must be readable. {0}", diagnostics);

		// Assert
		act.Should().Throw<Exception>().Which.Message.Should().Contain(diagnostics,
			because: "the diagnostics are passed as a because argument exactly so their braces survive");
	}
}
