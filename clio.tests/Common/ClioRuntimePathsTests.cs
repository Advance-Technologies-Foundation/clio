using System.IO;
using Clio;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
[NonParallelizable] // mutates the CLIO_HOME process environment variable
[Description("Unit tests for ClioRuntimePaths home/cache resolution and the CLIO_HOME override")]
public sealed class ClioRuntimePathsTests {

	private string? _originalClioHome;

	[SetUp]
	public void SetUp() => _originalClioHome = System.Environment.GetEnvironmentVariable("CLIO_HOME");

	[TearDown]
	public void TearDown() => System.Environment.SetEnvironmentVariable("CLIO_HOME", _originalClioHome);

	[Test]
	[Description("CLIO_HOME overrides the home root verbatim and CacheRoot nests directly under it.")]
	public void CacheRoot_Honors_ClioHome_Override() {
		// Arrange
		string home = Path.Combine(Path.GetTempPath(), "clio-home-override-test");
		System.Environment.SetEnvironmentVariable("CLIO_HOME", home);

		// Act / Assert
		ClioRuntimePaths.Home.Should().Be(home,
			because: "CLIO_HOME must override the resolved home root verbatim");
		ClioRuntimePaths.CacheRoot.Should().Be(Path.Combine(home, "cache"),
			because: "the cache root must nest directly under the home root");
	}

	[Test]
	[Description("A blank CLIO_HOME is ignored so the default Company/Product path is used.")]
	public void Home_Falls_Back_When_ClioHome_Blank() {
		// Arrange
		System.Environment.SetEnvironmentVariable("CLIO_HOME", "   ");

		// Act
		string home = ClioRuntimePaths.Home;

		// Assert
		home.Should().NotBeNullOrWhiteSpace(
			because: "a blank override must be ignored so the default Company/Product path is resolved instead");
		home.Trim().Should().NotBe("",
			because: "a whitespace-only CLIO_HOME must not leak through as the home root");
	}
}
