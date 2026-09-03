using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Issue #1320: the <c>update-page</c> MCP tool used to have no <c>checksum</c> argument at all, so a
/// caller that supplied the <c>editable.checksum</c> it had just read from <c>get-page</c> had that value
/// silently dropped, and the conflict check fell back to the on-disk <c>.clio-pages</c> baseline. That
/// baseline can be stale or anchored elsewhere, which produced an external-modification conflict for an
/// edit that had no external modification behind it.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageUpdateToolChecksumTests {

	private static PageUpdateArgs CreateArgs(string checksum) =>
		new(
			SchemaName: "Usr_FormPage",
			Body: "body",
			Resources: null,
			DryRun: false,
			EnvironmentName: "dev",
			Uri: null,
			Login: null,
			Password: null) {
			Checksum = checksum
		};

	[Test]
	[Description("A caller-supplied checksum reaches PageUpdateOptions.ExpectedChecksum, so the conflict check compares against the body the caller actually fetched instead of the on-disk baseline.")]
	public void BuildOptions_ShouldMapTheChecksumToExpectedChecksum_WhenTheCallerSuppliesOne() {
		// Arrange
		PageUpdateArgs args = CreateArgs("4f3374af");

		// Act
		PageUpdateOptions options = PageUpdateTool.BuildOptions(args);

		// Assert
		options.ExpectedChecksum.Should().Be("4f3374af",
			"because the caller-supplied get-page checksum is the authoritative conflict baseline for this save");
	}

	[Test]
	[Description("Omitting the checksum leaves ExpectedChecksum null, so the pre-existing .clio-pages baseline discovery still drives the conflict check for callers that do not pass one.")]
	public void BuildOptions_ShouldLeaveExpectedChecksumNull_WhenTheCallerOmitsTheChecksum() {
		// Arrange
		PageUpdateArgs args = CreateArgs(null);

		// Act
		PageUpdateOptions options = PageUpdateTool.BuildOptions(args);

		// Assert
		options.ExpectedChecksum.Should().BeNull(
			"because without a caller checksum the on-disk baseline must remain the source of the conflict check");
	}
}
