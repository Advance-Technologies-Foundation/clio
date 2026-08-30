using System;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Pins which rule each rejected knowledge-source location is blamed on. The whole point of splitting one
/// composite condition into five throws was that a location with a stray query string used to be reported as
/// a scheme problem, sending the reader to fix the half that was already right — a mapping nothing else
/// guards.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeSourceRemoteLocationTests {

	[TestCase("git@ssh.internal.corp:team/kb.git", "not an absolute URI", TestName = "scp-style Git remote")]
	[TestCase("kb.internal/path", "not an absolute URI", TestName = "bare host and path")]
	[TestCase("ftp://example.invalid/kb.git", "must use HTTPS", TestName = "unsupported scheme")]
	[TestCase("http://example.invalid/kb.git", "must use HTTPS", TestName = "non-loopback HTTP")]
	[TestCase("https://user:secret@example.invalid/kb.git", "must not carry credentials", TestName = "credentials in URI")]
	[TestCase("https://example.invalid/kb.git?token=abc", "must not carry a query string", TestName = "query string")]
	[TestCase("https://example.invalid/kb.git#frag", "must not carry a fragment", TestName = "fragment")]
	[Description("Names the one broken rule for a rejected location instead of blaming a neighbouring one.")]
	public void ValidateAndClone_ShouldNameTheBrokenRule_WhenLocationIsRejected(string location, string expected) {
		// Arrange
		KnowledgeSourceConfiguration source = GitSource(location);

		// Act
		Action act = () => KnowledgeSourceConfigurationValidator.ValidateAndClone(source);

		// Assert
		act.Should().Throw<ArgumentException>(because: "an unusable location must be refused at configuration time")
			.WithMessage($"*{expected}*",
				because: "the reader has to be sent to the half of the location that is actually wrong");
	}

	[TestCase("https://example.invalid/kb.git", TestName = "HTTPS")]
	[TestCase("http://127.0.0.1:9/kb.git", TestName = "loopback HTTP")]
	[TestCase("http://localhost:8080/kb.git", TestName = "loopback by name")]
	[Description("Accepts HTTPS and loopback HTTP unchanged after the split into five separate rejections.")]
	public void ValidateAndClone_ShouldAccept_WhenLocationIsCredentialFreeHttpsOrLoopback(string location) {
		// Arrange
		KnowledgeSourceConfiguration source = GitSource(location);

		// Act
		Action act = () => KnowledgeSourceConfigurationValidator.ValidateAndClone(source);

		// Assert
		act.Should().NotThrow(
			because: "loopback HTTP is the only way to attach a local knowledge library, so narrowing the "
				+ "accept set would take that away");
	}

	[TestCase("git@ssh.internal.corp:team/kb.git", TestName = "scp-style Git remote")]
	[TestCase("https://kb.internal.corp/team/kb.git?token=abc", TestName = "query string on an internal host")]
	[Description("Keeps the rejected location and its host out of the message, which the redactor cannot scrub.")]
	public void ValidateAndClone_ShouldNotEchoTheLocation_WhenRejectingIt(string location) {
		// Arrange
		KnowledgeSourceConfiguration source = GitSource(location);

		// Act
		Action act = () => KnowledgeSourceConfigurationValidator.ValidateAndClone(source);

		// Assert
		string message = act.Should().Throw<ArgumentException>().Which.Message;
		message.Should().NotContain("internal.corp",
			because: "these messages cross the MCP boundary, and the redactor keys on 'scheme://', absolute "
				+ "paths and host:port - an scp-style remote and a bare internal hostname match none of them");
		message.Should().NotContain("token=abc",
			because: "a query string can carry the very credential the URI rule exists to keep out");
	}

	[Test]
	[Description("Survives the redactor intact, so the example the message gives is not itself scrubbed away.")]
	public void ValidateAndClone_RejectionMessage_ShouldSurviveRedaction() {
		// Arrange
		KnowledgeSourceConfiguration source = GitSource("git@ssh.internal.corp:team/kb.git");

		// Act
		string message = ((Action)(() => KnowledgeSourceConfigurationValidator.ValidateAndClone(source)))
			.Should().Throw<ArgumentException>().Which.Message;

		// Assert
		SensitiveErrorTextRedactor.Redact(message).Should().Contain("https://<host>/<path>",
			because: "this message reaches the caller through Redact, whose UriRegex would replace a literal "
				+ "'https://host/path' example with [redacted-uri] - deleting the one thing it exists to show");
	}

	private static KnowledgeSourceConfiguration GitSource(string location) => new() {
		LibraryId = "com.example.kb",
		Type = KnowledgeSourceType.Git,
		Location = location,
		Branch = "main",
		Enabled = true,
		Priority = 100,
		Participation = KnowledgeSourceParticipation.Authoritative
	};
}
