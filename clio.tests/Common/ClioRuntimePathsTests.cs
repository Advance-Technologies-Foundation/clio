using System.IO;
using System.Reflection;
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

		// Act / Assert
		AssertFallbackHome(ClioRuntimePaths.Home);
	}

	[Test]
	[Description("An unset CLIO_HOME falls back to the default Company/Product path.")]
	public void Home_Falls_Back_When_ClioHome_Not_Set() {
		// Arrange
		System.Environment.SetEnvironmentVariable("CLIO_HOME", null);

		// Act / Assert
		AssertFallbackHome(ClioRuntimePaths.Home);
	}

	// The fallback path is AppSettingsFolderPath = <userRoot>/<Company>/<Product>. We assert that
	// structure (absolute + ends with the entry assembly's Company/Product segments) rather than a
	// hardcoded "creatio/clio": under `dotnet test` the entry assembly is the test host, so the
	// segments resolve to "Microsoft Corporation/testhost", not the clio assembly's values. This
	// still guards against the real regression — the fallback silently resolving to an unrelated
	// location (e.g. temp) or an empty string.
	private static void AssertFallbackHome(string home) {
		home.Should().NotBeNullOrWhiteSpace(
			because: "a missing/blank CLIO_HOME must resolve to the default Company/Product path, not an empty string");
		Path.IsPathRooted(home).Should().BeTrue(
			because: "the fallback must be an absolute path under the user's home/local-app-data root");

		Assembly entryAssembly = Assembly.GetEntryAssembly();
		string? company = entryAssembly?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
		string? product = entryAssembly?.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
		if (!string.IsNullOrEmpty(company) && !string.IsNullOrEmpty(product)) {
			home.Should().EndWith(Path.Combine(company, product),
				because: "the fallback path is composed as <userRoot>/<Company>/<Product>");
		}
	}
}
