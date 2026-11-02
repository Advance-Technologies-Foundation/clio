using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessDesignServerFault"/> - the reading of the bare error string ProcessDesignService
/// answers instead of its envelope, shared by the create, modify and version-save paths.
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class ProcessDesignServerFaultTests {

	private const string UnreadableRequest =
		"The formatter threw an exception while trying to deserialize the message: There was an error while "
		+ "trying to deserialize parameter http://tempuri.org/:request.";

	[Test]
	[Description("A bare JSON string body is read as the server's text, trimmed; a body that is not a JSON string, a blank one and a blank string read as no text, so the caller is not shown an empty quote.")]
	public void ReadServerText_ShouldReadABareJsonString_AndNothingElse() {
		// Act
		string? text = ProcessDesignServerFault.ReadServerText("  \"  Object reference not set.  \"");
		string? envelope = ProcessDesignServerFault.ReadServerText("{\"result\":{}}");
		string? html = ProcessDesignServerFault.ReadServerText("<html>error</html>");
		string? blank = ProcessDesignServerFault.ReadServerText("\"   \"");
		string? empty = ProcessDesignServerFault.ReadServerText(null);
		string? broken = ProcessDesignServerFault.ReadServerText("\"unterminated");

		// Assert
		text.Should().Be("Object reference not set.", because: "the server's text is quoted without its padding");
		envelope.Should().BeNull(because: "an object body is not a WCF fault string");
		html.Should().BeNull(because: "an HTML page is not a WCF fault string");
		blank.Should().BeNull(because: "a blank string says nothing worth quoting");
		empty.Should().BeNull(because: "no body has no text");
		broken.Should().BeNull(because: "a body that starts like a string but does not parse is not quoted");
	}

	[Test]
	[Description("A server text longer than 400 characters is cut at 400 and marked with an ellipsis, and a text of exactly 400 is kept whole, so a long fault does not flood the message the caller reads.")]
	public void ReadServerText_ShouldCapTheTextAt400Characters() {
		// Arrange
		string atLimit = new('a', ProcessDesignServerFault.MaxServerTextLength);
		string overLimit = new string('b', ProcessDesignServerFault.MaxServerTextLength) + "TAIL";

		// Act
		string? kept = ProcessDesignServerFault.ReadServerText($"\"{atLimit}\"");
		string? cut = ProcessDesignServerFault.ReadServerText($"\"{overLimit}\"");

		// Assert
		ProcessDesignServerFault.MaxServerTextLength.Should().Be(400, because: "the cap is 400 characters");
		kept.Should().Be(atLimit, because: "a text at the cap is quoted whole");
		cut.Should().Be(new string('b', 400) + "...", because: "a longer text is cut at the cap and says it was cut");
		cut.Should().NotContain("TAIL", because: "nothing past the cap is quoted");
	}

	[Test]
	[Description("Only WCF's request-deserialization string classifies as a request the server could not read; any other text, and no text, do not, because a failure after the save may have written it.")]
	public void IsUnreadableRequest_ShouldMatchOnlyTheDeserializationFault() {
		// Act
		bool unreadable = ProcessDesignServerFault.IsUnreadableRequest(UnreadableRequest);
		bool other = ProcessDesignServerFault.IsUnreadableRequest("Object reference not set to an instance.");
		bool none = ProcessDesignServerFault.IsUnreadableRequest(null);

		// Assert
		unreadable.Should().BeTrue(because: "WCF answers this text before any operation runs");
		other.Should().BeFalse(because: "another fault may come after the save, so its outcome stays unknown");
		none.Should().BeFalse(because: "no text is no evidence of an unread request");
	}

	[Test]
	[Description("The quoting sentence appends to a message with a leading space, and is empty when there is no server text.")]
	public void ServerSaid_ShouldQuoteTheServer_OrSayNothing() {
		// Act
		string said = ProcessDesignServerFault.ServerSaid("Boom.");
		string nothing = ProcessDesignServerFault.ServerSaid(null);

		// Assert
		said.Should().Be(" The server said: Boom.", because: "the sentence appends to the command's own message");
		nothing.Should().BeEmpty(because: "no text means no quote");
	}

}
