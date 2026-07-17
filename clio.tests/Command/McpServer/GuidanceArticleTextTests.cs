using System;
using Clio.Command.McpServer.Resources;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for the <see cref="GuidanceArticleText"/> anchored-splice helpers that back every
/// feature-aware guidance resource. These pin the throw-on-drift guards and the line-boundary
/// arithmetic directly, since the resource ON/OFF tests only exercise the happy path.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class GuidanceArticleTextTests {

	[Test]
	[Category("Unit")]
	[Description("NormalizeNewlines converts CRLF to LF so anchors and output are checkout-independent.")]
	public void NormalizeNewlines_Should_Convert_Crlf_To_Lf() {
		// Arrange
		string crlf = "alpha\r\nbeta\r\ngamma";

		// Act
		string normalized = GuidanceArticleText.NormalizeNewlines(crlf);

		// Assert
		normalized.Should().Be("alpha\nbeta\ngamma",
			because: "every CRLF pair must collapse to a single LF so multi-line anchors match on every platform");
	}

	[Test]
	[Category("Unit")]
	[Description("ReplaceUnique swaps exactly one occurrence of the anchor for the replacement and normalizes CRLF first.")]
	public void ReplaceUnique_Should_Splice_Exactly_Once_And_Normalize() {
		// Arrange — CRLF input to prove ReplaceUnique normalizes before matching and returns LF output.
		string text = "start\r\nMIDDLE\r\nend";

		// Act
		string result = GuidanceArticleText.ReplaceUnique(text, "MIDDLE", "REPLACED");

		// Assert
		result.Should().Be("start\nREPLACED\nend",
			because: "the single anchor occurrence must be replaced and the output normalized to LF");
	}

	[Test]
	[Category("Unit")]
	[Description("ReplaceUnique throws when the anchor is absent, so a stale anchor fails loudly instead of silently serving unchanged content.")]
	public void ReplaceUnique_Should_Throw_When_Anchor_Missing() {
		// Arrange
		string text = "start\nmiddle\nend";

		// Act
		Action act = () => GuidanceArticleText.ReplaceUnique(text, "NOT-PRESENT", "x");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*not found*",
				because: "a missing anchor is content drift that must fail every unit run, not silently no-op");
	}

	[Test]
	[Category("Unit")]
	[Description("ReplaceUnique throws when the anchor matches more than once, so an ambiguous splice fails loudly.")]
	public void ReplaceUnique_Should_Throw_When_Anchor_Not_Unique() {
		// Arrange
		string text = "head\nDUP\nDUP\ntail";

		// Act
		Action act = () => GuidanceArticleText.ReplaceUnique(text, "DUP", "x");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*not unique*",
				because: "a non-unique anchor could splice the wrong occurrence, so it must be rejected");
	}

	[Test]
	[Category("Unit")]
	[Description("RemoveUniqueLine removes the first line (which has no preceding newline) together with its line break.")]
	public void RemoveUniqueLine_Should_Remove_First_Line() {
		// Arrange
		string text = "FIRST\nsecond\nthird";

		// Act
		string result = GuidanceArticleText.RemoveUniqueLine(text, "FIRST");

		// Assert
		result.Should().Be("second\nthird",
			because: "removing the first line must drop it and its trailing newline, leaving the rest intact");
	}

	[Test]
	[Category("Unit")]
	[Description("RemoveUniqueLine removes a middle line together with its line break, joining the neighbours.")]
	public void RemoveUniqueLine_Should_Remove_Middle_Line() {
		// Arrange
		string text = "a\nMID\nc";

		// Act
		string result = GuidanceArticleText.RemoveUniqueLine(text, "MID");

		// Assert
		result.Should().Be("a\nc",
			because: "removing a middle line must splice the surrounding lines without leaving a blank line");
	}

	[Test]
	[Category("Unit")]
	[Description("RemoveUniqueLine removes the final line even when the string has no trailing newline (the exact edge a resource file ending without a trailing newline produces).")]
	public void RemoveUniqueLine_Should_Remove_Final_Line_Without_Trailing_Newline() {
		// Arrange — no trailing newline: the marker is on the last line and has no following '\n'.
		string text = "a\nb\nLAST";

		// Act
		string result = GuidanceArticleText.RemoveUniqueLine(text, "LAST");

		// Assert
		result.Should().Be("a\nb",
			because: "removing the final line must drop it and the preceding newline, not throw on the missing trailing newline");
	}

	[Test]
	[Category("Unit")]
	[Description("RemoveUniqueLine throws when the marker is absent, so a stale marker fails loudly.")]
	public void RemoveUniqueLine_Should_Throw_When_Marker_Missing() {
		// Arrange
		string text = "a\nb\nc";

		// Act
		Action act = () => GuidanceArticleText.RemoveUniqueLine(text, "NOT-PRESENT");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*not found*",
				because: "a missing line marker is content drift that must fail every unit run");
	}

	[Test]
	[Category("Unit")]
	[Description("RemoveUniqueLine throws when the marker occurs more than once, so an ambiguous line removal fails loudly.")]
	public void RemoveUniqueLine_Should_Throw_When_Marker_Not_Unique() {
		// Arrange
		string text = "a\nDUP\nDUP\nb";

		// Act
		Action act = () => GuidanceArticleText.RemoveUniqueLine(text, "DUP");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*not unique*",
				because: "a non-unique marker could remove the wrong line, so it must be rejected");
	}
}
