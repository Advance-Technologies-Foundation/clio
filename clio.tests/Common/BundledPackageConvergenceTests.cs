using Clio.Common;
using Clio.Project.NuGet;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Covers the convergence rule — "an environment should carry the version of a bundled package that this
/// clio carries" — as a rule distinct from <c>[RequiresPackage]</c>.
/// </summary>
/// <remarks>
/// The distinction is the point of these tests. The attribute states what the CODE needs; convergence
/// states what environments should be brought to. They land on the same remedy, so the only way to tell
/// them apart in the field is the message — which is why the messages are asserted, not just the verdicts.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class BundledPackageConvergenceTests {

	#region Constants: Private

	private const string BundledPackage = BundledPackages.ProcessBuilderPackageName;
	private const string UnbundledPackage = "SomePackageClioDoesNotShip";

	#endregion

	#region Fields: Private

	private IBundledPackageCatalog _catalog;
	private ILogger _logger;
	private IBundledPackageConvergence _sut;

	#endregion

	#region Methods: Private

	private static PackageVersion Version(string version) => PackageVersion.ParseVersion(version);

	private void ArrangeBundledVersion(string version) {
		_catalog.IsBundled(BundledPackage).Returns(true);
		_catalog.TryGetVersion(BundledPackage, out Arg.Any<PackageVersion>(), out Arg.Any<string>())
			.Returns(call => {
				call[1] = Version(version);
				call[2] = null;
				return true;
			});
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_catalog = Substitute.For<IBundledPackageCatalog>();
		_logger = Substitute.For<ILogger>();
		_sut = new BundledPackageConvergence(_catalog, _logger);
	}

	[TearDown]
	public void TearDown() {
		_catalog.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("An environment behind the version clio ships must be refused, with a message naming BOTH versions so the reader can tell a convergence refusal from a requirement refusal.")]
	public void TryGetConvergenceRefusal_ShouldRefuse_WhenEnvironmentIsBehindTheBundledVersion() {
		// Arrange
		ArrangeBundledVersion("1.5.0.0");

		// Act
		bool refused = _sut.TryGetConvergenceRefusal(BundledPackage, Version("1.3.0.0"), out string message);

		// Assert
		refused.Should().BeTrue(
			because: "clio carries newer sources than the environment runs, and nothing else in the product "
				+ "would ever tell the user so");
		message.Should().Contain("1.5.0.0", because: "the reader needs to know what clio would install");
		message.Should().Contain("1.3.0.0", because: "and what the environment currently has");
	}

	[Test]
	[Description("An environment already at the bundled version proceeds: convergence is about being behind, and re-running a configuration build for nothing is the cost of getting this wrong.")]
	public void TryGetConvergenceRefusal_ShouldAllow_WhenEnvironmentMatchesTheBundledVersion() {
		// Arrange
		ArrangeBundledVersion("1.5.0.0");

		// Act
		bool refused = _sut.TryGetConvergenceRefusal(BundledPackage, Version("1.5.0.0"), out string message);

		// Assert
		refused.Should().BeFalse(because: "the environment already carries exactly what clio ships");
		message.Should().BeNull(because: "there is nothing to say when nothing needs doing");
	}

	[Test]
	[Description("An environment AHEAD of the bundled version proceeds rather than being downgraded — a developer running a locally built package must not be blocked by an older release of clio.")]
	public void TryGetConvergenceRefusal_ShouldAllow_WhenEnvironmentIsAheadOfTheBundledVersion() {
		// Arrange
		ArrangeBundledVersion("1.5.0.0");

		// Act
		bool refused = _sut.TryGetConvergenceRefusal(BundledPackage, Version("2.0.0.0"), out string message);

		// Assert
		refused.Should().BeFalse(
			because: "convergence pulls environments forward, never back: refusing here would demand the user "
				+ "install an OLDER package than they already run");
		message.Should().BeNull(because: "there is nothing to say when nothing needs doing");
	}

	[Test]
	[Description("A package clio does not ship is not subject to convergence, and the catalog must not even be asked for a version it could not have.")]
	public void TryGetConvergenceRefusal_ShouldAllow_WhenPackageIsNotBundled() {
		// Arrange
		_catalog.IsBundled(UnbundledPackage).Returns(false);

		// Act
		bool refused = _sut.TryGetConvergenceRefusal(UnbundledPackage, Version("1.0.0.0"), out string message);

		// Assert
		refused.Should().BeFalse(
			because: "clio ships nothing for this package, so there is no version to converge to");
		message.Should().BeNull(because: "there is nothing to say when the rule does not apply");
		_catalog.DidNotReceive().TryGetVersion(
			UnbundledPackage, out Arg.Any<PackageVersion>(), out Arg.Any<string>());
	}

	[Test]
	[Description("An absent package is the [RequiresPackage] gate's business, not this rule's — convergence must not produce a second, differently worded refusal for the same condition.")]
	public void TryGetConvergenceRefusal_ShouldAllow_WhenPackageIsNotInstalled() {
		// Arrange
		ArrangeBundledVersion("1.5.0.0");

		// Act
		bool refused = _sut.TryGetConvergenceRefusal(BundledPackage, installedVersion: null, out string message);

		// Assert
		refused.Should().BeFalse(
			because: "the requirement gate has already refused this case with a message about installing the "
				+ "package; a second refusal about being 'behind version 1.5.0.0' would be misleading");
		message.Should().BeNull(because: "there is nothing to say when the rule does not apply");
	}

	[Test]
	[Description("A distribution that cannot read its own archive warns and lets the command through: the requirement gate already established the code can work here, and blocking would turn clio's defect into the user's.")]
	public void TryGetConvergenceRefusal_ShouldWarnAndAllow_WhenTheBundledVersionCannotBeRead() {
		// Arrange
		const string diagnosis = "This clio installation does not carry the bundled archive.";
		_catalog.IsBundled(BundledPackage).Returns(true);
		_catalog.TryGetVersion(BundledPackage, out Arg.Any<PackageVersion>(), out Arg.Any<string>())
			.Returns(call => {
				call[1] = null;
				call[2] = diagnosis;
				return false;
			});

		// Act
		bool refused = _sut.TryGetConvergenceRefusal(BundledPackage, Version("1.0.0.0"), out string message);

		// Assert
		refused.Should().BeFalse(
			because: "the environment has the package and the command would succeed; a broken clio must not "
				+ "block work it is not required for");
		message.Should().BeNull(because: "the refusal channel is for the environment, not for clio's own state");
		_logger.Received(1).WriteWarning(diagnosis);
	}

	[Test]
	[Description("A pre-release suffix on the INSTALLED version sorts it above the same numeric release, so a developer's -rc build is not asked to move to the release. Documented rather than corrected: PackageVersion's ordering is repo-wide and the [RequiresPackage] gate uses it, so diverging here would make the two rules disagree about which of two versions is newer.")]
	public void TryGetConvergenceRefusal_ShouldAllow_WhenInstalledVersionCarriesAPreReleaseSuffix() {
		// Arrange
		ArrangeBundledVersion("1.0.0.0");

		// Act
		bool refused = _sut.TryGetConvergenceRefusal(BundledPackage, Version("1.0.0.0-rc"), out string _);

		// Assert
		refused.Should().BeFalse(
			because: "PackageVersion.CompareSuffix ranks an empty suffix BELOW a non-empty one, the inverse of "
				+ "SemVer. Unreachable through the supported path - the rebundle script parses -Version through "
				+ "System.Version, which rejects suffixes - so this pins the behaviour rather than blessing it");
	}

	[Test]
	[Description("An environment recording a shorter version than the archive carries is treated as behind, because that is exactly how System.Version compares it and how the [RequiresPackage] gate would.")]
	public void TryGetConvergenceRefusal_ShouldRefuse_WhenInstalledVersionHasFewerParts() {
		// Arrange
		ArrangeBundledVersion("1.0.0.0");

		// Act
		bool refused = _sut.TryGetConvergenceRefusal(BundledPackage, Version("1.0.0"), out string _);

		// Assert
		refused.Should().BeTrue(
			because: "a three-part version yields Revision = -1 and so sorts below the four-part version the "
				+ "archive carries; installing is the correct remedy and makes the recorded version match");
	}

	#endregion

}
