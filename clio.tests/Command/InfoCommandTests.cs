using Clio.Command;
using Clio.Common;
using Clio.Project.NuGet;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Covers the <c>info</c> command's bundled-package line.
/// </summary>
/// <remarks>
/// The command used to print a compile-time constant and needed no test. It now reads the version out of
/// the shipped archive through <see cref="IBundledPackageCatalog"/>, which gives it a dependency, a failure
/// branch, and a DI registration whose failure mode is a resolution error the first time a user types
/// <c>clio info</c>.
/// </remarks>
public class InfoCommandTests : BaseCommandTests<InfoCommandOptions> {

	#region Fields: Private

	private readonly IBundledPackageCatalog _bundledPackageCatalog = Substitute.For<IBundledPackageCatalog>();
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private InfoCommand _sut;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_bundledPackageCatalog);
		containerBuilder.AddSingleton(_logger);
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_sut = Container.GetRequiredService<InfoCommand>();
	}

	[TearDown]
	public void TearDown() {
		_bundledPackageCatalog.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("The process-builder line reports the version read out of the bundled archive, because that is the version an install would actually ship and the one the convergence rule compares.")]
	public void Execute_ShouldReportTheBundledVersion_WhenTheCatalogCanReadIt() {
		// Arrange
		_bundledPackageCatalog
			.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out Arg.Any<PackageVersion>(),
				out Arg.Any<string>())
			.Returns(call => {
				call[1] = PackageVersion.ParseVersion("4.5.6.7");
				call[2] = null;
				return true;
			});

		// Act
		int result = _sut.Execute(new InfoCommandOptions { All = true });

		// Assert
		result.Should().Be(0, because: "reporting versions cannot fail when every version is readable");
		_logger.Received().WriteInfo(Arg.Is<string>(line =>
			line.Contains("process-builder") && line.Contains("4.5.6.7")));
	}

	[Test]
	[Description("A distribution that cannot read its own archive says so on that line instead of printing a number nothing backs — the whole reason a constant was the wrong carrier is that it could not be wrong out loud.")]
	public void Execute_ShouldReportTheDiagnosis_WhenTheCatalogCannotReadTheArchive() {
		// Arrange
		const string diagnosis = "This clio installation does not carry the bundled archive.";
		_bundledPackageCatalog
			.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out Arg.Any<PackageVersion>(),
				out Arg.Any<string>())
			.Returns(call => {
				call[1] = null;
				call[2] = diagnosis;
				return false;
			});

		// Act
		int result = _sut.Execute(new InfoCommandOptions { All = true });

		// Assert
		result.Should().Be(0,
			because: "an unreadable archive is reported on its own line; it must not fail the whole command, "
				+ "which also reports the clio and runtime versions the user asked for");
		_logger.Received().WriteInfo(Arg.Is<string>(line =>
			line.Contains("process-builder") && line.Contains(diagnosis)));
	}

	[Test]
	[Description("The command resolves from the container, because its new dependency is registered by an explicit singleton PLUS an auto-scan exclusion — a pairing whose failure mode is a resolution error the first time anyone runs the verb.")]
	public void InfoCommand_ShouldResolveFromTheContainer() {
		// Arrange & Act
		InfoCommand resolved = Container.GetRequiredService<InfoCommand>();

		// Assert
		resolved.Should().NotBeNull(
			because: "clio info is reachable on every installation and takes no environment, so a broken "
				+ "registration would surface as a crash rather than as a refusal");
	}

	#endregion

}
