using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Guards the fixture helper the knowledge trust-store fixtures depend on.
/// </summary>
/// <remarks>
/// An earlier revision walked the temporary root's ancestors and asked
/// <see cref="Directory.ResolveLinkTarget"/> about each one, including the path root. That throws on a
/// Windows drive root rather than answering "not a link", and because the walk ran in a static
/// initializer the throw surfaced as every trust-store test erroring in SetUp with the real cause
/// buried in a TypeInitializationException. These tests pin the two properties that prevent a repeat.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeTrustTestPathsTests {

	[Test]
	[Description("The resolved temporary root is an existing rooted directory on every platform, so reading it can never fail a fixture's setup.")]
	public void ResolvedTempRoot_ShouldBeAnExistingRootedDirectory_OnEveryPlatform() {
		// Arrange

		// Act
		string resolved = KnowledgeTrustTestPaths.ResolvedTempRoot;

		// Assert
		resolved.Should().NotBeNullOrWhiteSpace(
			because: "fixtures combine this root with their own directory name and cannot proceed without it");
		Path.IsPathRooted(resolved).Should().BeTrue(
			because: "the trust store accepts only an absolute path, which is the whole reason this helper exists");
		Directory.Exists(resolved).Should().BeTrue(
			because: "resolving links must land on a real directory rather than a rewritten path that does not exist");
	}

	[Test]
	[Description("Resolving the path root itself returns it unchanged instead of throwing, which is what broke every trust-store fixture on Windows.")]
	public void ResolvedTempRoot_ShouldTolerateThePathRoot_WhenTheWalkReachesIt() {
		// Arrange
		string root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;

		// Act
		// The helper walks from the temporary directory up to this root on every call, so a root it
		// cannot handle would already have thrown while the class was being initialized.
		string resolved = KnowledgeTrustTestPaths.ResolvedTempRoot;

		// Assert
		root.Should().NotBeNullOrWhiteSpace(
			because: "the temporary path is absolute, so it always has a root for the walk to terminate at");
		Path.GetDirectoryName(Path.GetFullPath(root)).Should().BeNull(
			because: "the walk terminates on the parentless root, which is what keeps it from asking whether a "
				+ "drive root is a link — the question that throws on Windows");
		resolved.Should().NotBeNullOrWhiteSpace(
			because: "reaching the root has to produce a usable path rather than an initialization failure");
	}
}
