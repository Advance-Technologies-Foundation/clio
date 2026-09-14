using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Covers the one rule <see cref="CommandExecutionResult.WithCompileNotRequiredNote"/> exists to enforce: the
/// deterministic "compile-creatio not required" note must never be the sentence that contradicts a warning
/// standing in its own response.
/// </summary>
/// <remarks>
/// The note used to be appended unconditionally on every exit code 0 by both version tools. A version cloned
/// from a non-interpretable source is saved with <c>IsInterpretable</c> false — the flag travels through the
/// metadata round-trip and the server never recomputes it — and CrtProcessBuilder then warns that the version
/// cannot execute until the configuration is compiled. One response therefore carried both that warning and
/// its exact opposite, and an agent that believed the note skipped the compile and activated a version that
/// throws <c>NotImplementedException</c> out of <c>CreateProcess</c> on first run.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public class CompileNotRequiredNoteGateTests {

	private const string ServerWarning =
		"The version was saved but cannot execute until the configuration is compiled.";

	private static CommandExecutionResult ResultWith(params LogMessage[] output) =>
		new(0, output);

	[Test]
	[Category("Unit")]
	[Description("Appends the note on a clean success, which is the ordinary case and the behaviour the gate must not regress: a saved version is interpreted and runs as-is once activated, so an agent must not be pushed into a needless compile-creatio (ENG-95706).")]
	public void WithCompileNotRequiredNote_ShouldAppendTheNote_WhenNothingWarnsAboutCompiling() {
		// Arrange
		CommandExecutionResult result = ResultWith(new InfoMessage("Version 3 'UsrProcCustom3' created."));

		// Act
		CommandExecutionResult noted = result.WithCompileNotRequiredNote();

		// Assert
		noted.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "the note is the one channel an agent cannot skip, and on the ordinary path it is true");
	}

	[Test]
	[Description("SUPPRESSES the note when the response already says a compile IS required. This is the regression test for the contradiction: the note asserted the opposite of the server's own warning inside a single response, and only one of the two can be acted on.")]
	[Category("Unit")]
	public void WithCompileNotRequiredNote_ShouldSuppressTheNote_WhenTheResponseWarnsACompileIsRequired() {
		// Arrange
		CommandExecutionResult result = ResultWith(
			new InfoMessage("Version 3 'UsrProcCustom3' created."),
			new WarningMessage(ServerWarning));

		// Act
		CommandExecutionResult noted = result.WithCompileNotRequiredNote();

		// Assert
		noted.Note.Should().BeNull(
			because: "a response carrying the not-interpretable warning must not also claim a compile is "
				+ "unnecessary - the agent would skip it and activate a version that cannot execute");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves a note the command already set and appends to it rather than replacing it, so gating the note did not quietly become a way to drop other deterministic hints.")]
	public void WithCompileNotRequiredNote_ShouldAppendToAnExistingNote_RatherThanReplaceIt() {
		// Arrange
		CommandExecutionResult result = ResultWith(new InfoMessage("done")) with { Note = "read the family back" };

		// Act
		CommandExecutionResult noted = result.WithCompileNotRequiredNote();

		// Assert
		noted.Note.Should().Be("read the family back " + CommandExecutionResult.CompileNotRequiredNote,
			because: "both hints are deterministic and neither supersedes the other");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps an existing note untouched when a compile IS required: the compile warning suppresses only the contradicting note, and discarding the command's own hint alongside it would lose information the caller needs.")]
	public void WithCompileNotRequiredNote_ShouldKeepAnExistingNote_WhenACompileIsRequired() {
		// Arrange
		CommandExecutionResult result = ResultWith(new WarningMessage(ServerWarning))
			with { Note = "read the family back" };

		// Act
		CommandExecutionResult noted = result.WithCompileNotRequiredNote();

		// Assert
		noted.Note.Should().Be("read the family back",
			because: "only the contradicting sentence is withheld, not every hint on the response");
	}

	[Test]
	[Category("Unit")]
	[Description("Tolerates an absent output collection. A result built without a log is not a state this gate may throw on - the note decision has to be answerable from whatever the command actually produced.")]
	public void WithCompileNotRequiredNote_ShouldAppendTheNote_WhenThereIsNoOutputAtAll() {
		// Arrange
		CommandExecutionResult result = new(0, null);

		// Act
		CommandExecutionResult noted = result.WithCompileNotRequiredNote();

		// Assert
		noted.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "nothing warned about compiling, and an empty log is not evidence that something did");
	}

	[Test]
	[Category("Unit")]
	[Description("Matches the warning clause case-insensitively and as a SUBSTRING, so a reworded server warning that keeps the clause still suppresses the note. The marker mirrors ProcessVersionSaveHandler.NotInterpretableWarning by hand, and the failure that matters is asserting the opposite of what the server said.")]
	public void WithCompileNotRequiredNote_ShouldSuppressTheNote_WhenTheWarningIsRewordedAroundTheSameClause() {
		// Arrange
		CommandExecutionResult result = ResultWith(new WarningMessage(
			"WARNING: this version cannot run UNTIL THE CONFIGURATION IS COMPILED on that environment."));

		// Act
		CommandExecutionResult noted = result.WithCompileNotRequiredNote();

		// Assert
		noted.Note.Should().BeNull(
			because: "the gate keys on the clause rather than the whole sentence, so a rewording on the server "
				+ "side cannot silently re-enable the contradiction");
	}
}
