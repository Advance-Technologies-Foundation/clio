using System;
using System.IO;
using System.Text.RegularExpressions;
using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Both places that declare which transport this MCP host serves. The worker path is stdio-only while
/// Stage 5 is deferred, and <see cref="McpHostTransport.Current"/> is what
/// <see cref="McpWorkerPathGate"/> reads to enforce it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a declaration test and not a behavioural one.</b> The two assignments are the first statements
/// of two process entry points; neither can be invoked from a unit test without starting a host, and
/// <see cref="McpHostTransport.Current"/> is a settable static, so a test that assigned it would prove
/// only its own assignment. Until this fixture existed, `grep` for the property across both test
/// projects returned NOTHING: deleting either line compiled, passed, and shipped.
/// </para>
/// <para>
/// <b>What each side is worth.</b> The stdio declaration also has a behavioural pin — remove it and the
/// gate reads <see cref="McpHostTransportKind.Unknown"/>, the wedge end-to-end scenario spawns no
/// workers and fails on its request counters. The HTTP declaration has NONE: removing it leaves
/// <c>Unknown</c> too, which is fail-closed and therefore silent, until someone "helpfully" restores a
/// transport there and opens the worker path on a host whose callers' credentials live in its own
/// <c>HttpContext</c>. That asymmetry is why this fixture asserts per file and names which declaration
/// vanished.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpHostTransportDeclarationTests {

	private static readonly string RepositoryRoot =
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	// Tolerant of whitespace, line breaks and namespace qualification, because the assertion is about the
	// declaration EXISTING, not about how it is spelled or where the using directives sit.
	private static Regex DeclarationOf(string transportKind) =>
		new(@"McpHostTransport\s*\.\s*Current\s*=\s*(?:[\w.]*\.)?McpHostTransportKind\s*\.\s*" + transportKind,
			RegexOptions.Singleline, TimeSpan.FromSeconds(5));

	[Test]
	[Description("The stdio entry point declares the stdio transport, without which the gate reads Unknown and the worker execution boundary is silently off for every clio mcp-server session.")]
	public void StdioHost_ShouldDeclareTheStdioTransport() {
		// Arrange
		string entryPoint = Path.Combine(RepositoryRoot, "clio", "Program.cs");
		File.Exists(entryPoint).Should().BeTrue(
			because: "the declaration can only be pinned in the file that carries it, and a moved entry point must fail loudly rather than silently stop being checked");

		// Act
		string source = File.ReadAllText(entryPoint);

		// Assert
		DeclarationOf("Stdio").IsMatch(source).Should().BeTrue(
			because: "clio mcp-server is the only host allowed to spawn workers, and it is allowed because it declares itself — delete this line and every cohort tool quietly runs in-process again");
	}

	[Test]
	[Description("The HTTP entry point declares the HTTP transport, so the stdio-only gate stays shut there — a call relayed from mcp-http would run under a different identity, since the caller's credentials live in that host's HttpContext and the channel to hand them down is exactly what Stage 5 deferred.")]
	public void HttpHost_ShouldDeclareTheHttpTransport() {
		// Arrange
		string entryPoint = Path.Combine(RepositoryRoot, "clio", "Command", "McpServer", "McpHttpServerCommand.cs");
		File.Exists(entryPoint).Should().BeTrue(
			because: "the declaration can only be pinned in the file that carries it, and a moved HTTP host must fail loudly rather than silently stop being checked");

		// Act
		string source = File.ReadAllText(entryPoint);

		// Assert
		DeclarationOf("Http").IsMatch(source).Should().BeTrue(
			because: "removing this declaration turns the stdio-only gate into 'gate open on http' the moment anything else declares Stdio in a shared process, and a privilege boundary crossed silently is worse than a failed call");
	}

	[Test]
	[Description("The property defaults to Unknown, the fail-closed value: an ordinary CLI process, a hand-built test container or a future host that declares nothing must read as 'not stdio' rather than inheriting the worker path on an unstated assumption.")]
	public void Transport_ShouldDefaultToTheFailClosedValue() {
		// Arrange & Act
		McpHostTransportKind zeroValue = default;

		// Assert
		zeroValue.Should().Be(McpHostTransportKind.Unknown,
			because: "the zero value is what every process that declared nothing carries, and if it ever became Stdio the gate would open by omission");
	}
}
