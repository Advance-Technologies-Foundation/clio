using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("UnitTests")]
internal class UnlockPackageCommandTests : BaseCommandTests<UnlockPackageOptions>{
	#region Fields: Private

	private readonly IClioGateway _clioGateway = Substitute.For<IClioGateway>();
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private readonly IPackageLockManager _packageLockManager = Substitute.For<IPackageLockManager>();
	private readonly ISysSettingsManager _sysSettingsManager = Substitute.For<ISysSettingsManager>();

	#endregion

	#region Methods: Public

	[Test]
	public void Execute_ReturnsError_WhenMaintainerIsMissing_ForUnlockAll() {
		UnlockPackageCommand sut = new(_packageLockManager, _clioGateway, _sysSettingsManager, _logger);
		UnlockPackageOptions options = new() {
			Environment = "dev"
		};
		_clioGateway.IsCompatibleWith(Arg.Any<string>()).Returns(true);

		int actual = sut.Execute(options);

		actual.Should().Be(1);
		_sysSettingsManager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
		_packageLockManager.DidNotReceive().Unlock(Arg.Any<IEnumerable<string>>());
		_logger.Received(1).WriteError("Maintainer is required to unlock all packages. Use -m <MAINTAINER>.");
	}

	[Test]
	public void Execute_ReturnsError_WhenSchemaNamePrefixUpdateFails_ForUnlockAll() {
		UnlockPackageCommand sut = new(_packageLockManager, _clioGateway, _sysSettingsManager, _logger);
		UnlockPackageOptions options = new() {
			Environment = "dev_env_n8",
			Maintainer = "Creatio"
		};
		_clioGateway.IsCompatibleWith(Arg.Any<string>()).Returns(true);
		_sysSettingsManager.UpdateSysSetting("Maintainer", Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		_sysSettingsManager.UpdateSysSetting("SchemaNamePrefix", Arg.Any<object>(), Arg.Any<string>()).Returns(false);

		int actual = sut.Execute(options);

		actual.Should().Be(1);
		_packageLockManager.DidNotReceive().Unlock(Arg.Any<IEnumerable<string>>());
		_logger.Received(1).WriteError("Could not update SchemaNamePrefix sys setting.");
	}

	[Test]
	public void Execute_SetsMaintainerAndUnlocksAllPackages_WhenMaintainerProvided() {
		UnlockPackageCommand sut = new(_packageLockManager, _clioGateway, _sysSettingsManager, _logger);
		UnlockPackageOptions options = new() {
			Environment = "dev_env_n8",
			Maintainer = "Creatio"
		};
		_clioGateway.IsCompatibleWith(Arg.Any<string>()).Returns(true);
		_sysSettingsManager.UpdateSysSetting("Maintainer", Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		_sysSettingsManager.UpdateSysSetting("SchemaNamePrefix", Arg.Any<object>(), Arg.Any<string>()).Returns(true);

		int actual = sut.Execute(options);

		actual.Should().Be(0);
		Received.InOrder(() => {
			_sysSettingsManager.UpdateSysSetting("Maintainer",
				Arg.Is<object>(value => value != null && value.ToString() == options.Maintainer), Arg.Any<string>());
			_sysSettingsManager.UpdateSysSetting("SchemaNamePrefix",
				Arg.Is<object>(value => value != null && value.ToString() == string.Empty), Arg.Any<string>());
			_packageLockManager.Unlock(Arg.Any<IEnumerable<string>>());
		});
	}

	[Test]
	public void Execute_UnlocksNamedPackages_WithoutMaintainerUpdate() {
		UnlockPackageCommand sut = new(_packageLockManager, _clioGateway, _sysSettingsManager, _logger);
		UnlockPackageOptions options = new() {
			Environment = "dev",
			Name = "Pkg1,Pkg2"
		};
		_clioGateway.IsCompatibleWith(Arg.Any<string>()).Returns(true);

		int actual = sut.Execute(options);

		actual.Should().Be(0);
		_sysSettingsManager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
		_packageLockManager.Received(1).Unlock(Arg.Any<IEnumerable<string>>());
	}

	[SetUp]
	public void SetUp() {
		_clioGateway.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_packageLockManager.ClearReceivedCalls();
		_sysSettingsManager.ClearReceivedCalls();
	}

	#endregion
}
