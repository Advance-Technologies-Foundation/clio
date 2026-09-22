using System.Collections.Generic;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// What clio adds to a layout refusal on its way to an agent.
/// </summary>
/// <remarks>
/// The sentence is the whole of the feature at this layer. The server decides WHETHER to hold an edit back;
/// what the caller then does about it is decided entirely by these words, and there is no other surface where
/// an agent learns that a new version is an option at all.
/// </remarks>
[TestFixture]
[Property("Module", "ProcessModel")]
public class LayoutChangeRelayTests {
	private static LayoutChangeRelay Relay(params string[] elements) =>
		new() { Reason = "elementsChangeOrder", Summary = "Branches swap places.", Elements = [.. elements] };

	[Test]
	[Category("Unit")]
	[Description("The new-version route is named, and named BEFORE the confirmation flag. A relay that offered "
		+ "only the flag would be an instruction an agent can carry out alone - re-send and confirm - which is "
		+ "the consent this gate exists to collect, lost through a different door than the one that was shut.")]
	public void RelaySentence_ShouldOfferTheNewVersionRoute_BeforeTheConfirmationFlag() {
		// Act
		string sentence = Relay("HandleT1", "HandleT2").RelaySentence();

		// Assert
		sentence.Should().Contain("modify-business-process-as-new-version",
			because: "the answer that costs the user nothing has to be on the surface that reaches the agent");
		sentence.IndexOf("modify-business-process-as-new-version", System.StringComparison.Ordinal)
			.Should().BeLessThan(sentence.IndexOf("confirm-layout-change", System.StringComparison.Ordinal),
				because: "order is what decides which one an agent reaches for; the destructive answer is "
					+ "second because it is destructive, not because it is unusual");
		sentence.Should().Contain("ASK THE USER",
			because: "both answers are the user's, and an agent given two options and no instruction picks one");
	}

	[Test]
	[Category("Unit")]
	[Description("The elements the report names travel with it. Without them the caller holds the server's "
		+ "sentence and no way to tell the user WHICH parts of their diagram move, which is the first thing "
		+ "anybody asked to approve a layout change wants to know.")]
	public void ElementsClause_ShouldNameTheElements_WhenTheServerSentThem() {
		// Act & Assert
		Relay("HandleT1", "HandleT2").ElementsClause()
			.Should().Be(" Affected elements: HandleT1, HandleT2.");
	}

	[Test]
	[Category("Unit")]
	[Description("An empty list yields no clause at all, because \"Affected elements: .\" reads as a truncated "
		+ "message and sends a reader looking for what was cut off.")]
	public void ElementsClause_ShouldBeEmpty_WhenTheServerNamedNone() {
		// Act & Assert
		new LayoutChangeRelay { Elements = null }.ElementsClause().Should().BeEmpty();
		new LayoutChangeRelay { Elements = [] }.ElementsClause().Should().BeEmpty();
		new LayoutChangeRelay { Elements = new List<string> { " " } }.ElementsClause()
			.Should().Be(" Affected elements: .",
				because: "a blank NAME is a server that sent something, unlike a server that sent nothing - "
					+ "hiding it would hide a wire defect rather than a formatting one");
	}
}
