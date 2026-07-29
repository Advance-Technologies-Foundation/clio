namespace Clio.Tests.Command;

using System;
using System.IO;
using System.Linq;
using System.Text;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

/// <summary>
/// Unit coverage for <see cref="SetLogoCommand"/>: the four logo slots (login, menu, configuration,
/// dark toolbar), the splash suppression toggle, and the apply-then-bind flow into the target package
/// (default <c>Custom</c>). The real <see cref="SysSettingsCommand"/> sits under the command so the Binary
/// file write path (existing-type check, file-security policy, Base64 encoding) is exercised, over a
/// substituted <see cref="ISysSettingsManager"/> and file system.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class SetLogoCommandTests : BaseCommandTests<SetLogoOptions> {

	private const string LogoFile = "C:/brand/logo.svg";
	private const string DarkLogoFile = "C:/brand/logo-white.svg";
	private static readonly byte[] LogoBytes = Encoding.UTF8.GetBytes("logo-image-bytes");

	private ISysSettingsManager _sysSettingsManager;
	private IFileSystem _fileSystem;
	private IBrandingBindingService _brandingBindingService;
	private ILogger _logger;
	private SetLogoCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetLogoCommand>();
		_logger = Substitute.For<ILogger>();
		_command.Logger = _logger;
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.OpenReadStream(Arg.Any<string>()).Returns(_ => new MemoryStream(LogoBytes));
		_sysSettingsManager.GetFileSecurityPolicy().Returns(FileSecurityPolicy.DisabledPolicy);
		_sysSettingsManager.GetAllUsersDefaultWithType(Arg.Any<string>())
			.Returns(callInfo => (string.Empty,
				callInfo.Arg<string>() == SetLogoCommand.HideSplashLogoCode ? "Boolean" : "Binary"));
		_sysSettingsManager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
			.Returns(true);
		_brandingBindingService.BindLogos(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyCollection<string>>())
			.Returns(new BrandingScopeReport(BrandingScope.Logos, [], [], false));
	}

	public override void TearDown() {
		_sysSettingsManager.ClearReceivedCalls();
		_fileSystem.ClearReceivedCalls();
		_brandingBindingService.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_fileSystem = Substitute.For<IFileSystem>();
		_brandingBindingService = Substitute.For<IBrandingBindingService>();
		containerBuilder.AddTransient<ISysSettingsManager>(_ => _sysSettingsManager);
		containerBuilder.AddTransient<IFileSystem>(_ => _fileSystem);
		containerBuilder.AddTransient<IBrandingBindingService>(_ => _brandingBindingService);
	}

	[Test, Category("Unit")]
	[Description("Fails without touching the environment when no logo slot is passed — there is nothing to apply.")]
	public void Execute_ShouldFail_WhenNoLogoSlotIsPassed() {
		// Arrange
		SetLogoOptions options = new();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a run with no logo file has nothing to apply");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("at least one logo")));
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Fails naming the slot and the path when a passed logo file does not exist, before writing anything.")]
	public void Execute_ShouldFail_WhenALogoFileDoesNotExist() {
		// Arrange
		_fileSystem.ExistsFile("C:/brand/missing.svg").Returns(false);
		SetLogoOptions options = new() { Logo = LogoFile, MenuLogo = "C:/brand/missing.svg" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a typo in one path must fail the run before any slot is written");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("C:/brand/missing.svg") && message.Contains("menu-logo")));
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Writes the login-page logo setting as a Binary payload encoded from the passed file.")]
	public void Execute_ShouldWriteTheLoginLogoSetting_FromTheFile() {
		// Arrange
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "one valid slot with an existing file is a complete request");
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetLogoCommand.LoginLogoCode,
			Arg.Is<object>(value => value.ToString() == Convert.ToBase64String(LogoBytes)),
			"Binary");
	}

	[Test, Category("Unit")]
	[Description("Maps the dark-logo option to the CrtAppToolbarLogo setting — the logo shown on the dark Freedom UI top panel.")]
	public void Execute_ShouldMapDarkLogo_ToTheToolbarLogoSetting() {
		// Arrange
		SetLogoOptions options = new() { DarkLogo = DarkLogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetLogoCommand.DarkLogoCode, Arg.Any<object>(), "Binary");
	}

	[Test, Category("Unit")]
	[Description("Writes every passed slot when several logo files are supplied in one run.")]
	public void Execute_ShouldWriteEveryPassedSlot() {
		// Arrange
		SetLogoOptions options = new() {
			Logo = LogoFile,
			MenuLogo = "C:/brand/menu.svg",
			ConfigurationLogo = "C:/brand/configuration.svg",
			DarkLogo = DarkLogoFile
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "all four slots carry existing files");
		_sysSettingsManager.Received(1).UpdateSysSetting(SetLogoCommand.LoginLogoCode, Arg.Any<object>(), "Binary");
		_sysSettingsManager.Received(1).UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), "Binary");
		_sysSettingsManager.Received(1).UpdateSysSetting(SetLogoCommand.ConfigurationLogoCode, Arg.Any<object>(), "Binary");
		_sysSettingsManager.Received(1).UpdateSysSetting(SetLogoCommand.DarkLogoCode, Arg.Any<object>(), "Binary");
	}

	[Test, Category("Unit")]
	[Description("Suppresses the stock splash-screen logo after applying logos so it does not flash during load.")]
	public void Execute_ShouldSuppressTheSplashLogo_AfterApplying() {
		// Arrange
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetLogoCommand.HideSplashLogoCode,
			Arg.Is<object>(value => value.ToString() == "true"),
			Arg.Any<string>());
	}

	[Test, Category("Unit")]
	[Description("Still reports success with a warning when the splash toggle fails: the logos are already applied and cannot be cleanly rolled back.")]
	public void Execute_ShouldSucceedWithWarning_WhenSplashToggleFails() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.HideSplashLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a failed splash toggle degrades the result, it does not undo the applied logos");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(SetLogoCommand.HideSplashLogoCode)));
	}

	[Test, Category("Unit")]
	[Description("Fails naming the slot when applying one of the logo settings fails, and never reaches the binding.")]
	public void Execute_ShouldFailNamingTheSlot_WhenApplyingASettingFails() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { Logo = LogoFile, MenuLogo = "C:/brand/menu.svg" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a slot the environment refused was not applied");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("menu-logo")));
		_brandingBindingService.DidNotReceiveWithAnyArgs().BindLogos(default, default);
	}

	[Test, Category("Unit")]
	[Description("Binds the logos into the default Custom package when the caller names no package.")]
	public void Execute_ShouldBindLogosIntoCustomPackage_WhenNoPackageIsNamed() {
		// Arrange
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the apply and the default-package bind both succeeded");
		_brandingBindingService.Received(1).BindLogos("Custom", Arg.Any<System.Collections.Generic.IReadOnlyCollection<string>>());
	}

	[Test, Category("Unit")]
	[Description("Passes exactly the applied setting codes to the binding, so a slot this run never wrote cannot be shipped as newly branded.")]
	public void Execute_ShouldBindOnlyTheAppliedSettingCodes() {
		// Arrange
		SetLogoOptions options = new() { Logo = LogoFile, DarkLogo = DarkLogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_brandingBindingService.Received(1).BindLogos("Custom",
			Arg.Is<System.Collections.Generic.IReadOnlyCollection<string>>(codes =>
				codes.Contains(SetLogoCommand.LoginLogoCode)
				&& codes.Contains(SetLogoCommand.DarkLogoCode)
				&& codes.Contains(SetLogoCommand.HideSplashLogoCode)
				&& codes.Count == 3));
	}

	[Test, Category("Unit")]
	[Description("Excludes the splash toggle from the applied codes when its write failed, so the binding cannot ship a splash state this run never wrote.")]
	public void Execute_ShouldNotBindTheSplashToggle_WhenItsWriteFailed() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.HideSplashLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_brandingBindingService.Received(1).BindLogos("Custom",
			Arg.Is<System.Collections.Generic.IReadOnlyCollection<string>>(codes =>
				!codes.Contains(SetLogoCommand.HideSplashLogoCode)));
	}

	[Test, Category("Unit")]
	[Description("Binds the logos into the caller-named package instead of the default.")]
	public void Execute_ShouldBindLogosIntoNamedPackage_WhenPackageIsPassed() {
		// Arrange
		_brandingBindingService.BindLogos("UsrMyApp", Arg.Any<System.Collections.Generic.IReadOnlyCollection<string>>())
			.Returns(new BrandingScopeReport(BrandingScope.Logos, ["LogoImage"], [], false));
		SetLogoOptions options = new() { Logo = LogoFile, PackageName = "UsrMyApp" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a user-named package must be honored");
		_brandingBindingService.Received(1).BindLogos("UsrMyApp", Arg.Any<System.Collections.Generic.IReadOnlyCollection<string>>());
	}

	[Test, Category("Unit")]
	[Description("Reports the bound package in the run output so the user learns where the logo data landed.")]
	public void Execute_ShouldNameTheBoundPackage_InTheRunOutput() {
		// Arrange
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("bound into package 'Custom'")));
	}

	[Test, Category("Unit")]
	[Description("Fails naming the package and asking for a re-run when the logos applied but the binding failed, so a delivery failure is never silent.")]
	public void Execute_ShouldFailNamingThePackage_WhenBindingFails() {
		// Arrange
		_brandingBindingService.BindLogos(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyCollection<string>>())
			.Throws(new InvalidOperationException("package is locked"));
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the user asked for logos that ship with the package, and the package part failed");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("'Custom'") && message.Contains("package is locked") && message.Contains("Re-run")));
	}

	[Test, Category("Unit")]
	[Description("Still reports the slots that were applied before a failure, so the caller sees the partial state instead of assuming nothing changed.")]
	public void Execute_ShouldReportAppliedSlots_WhenALaterStepFails() {
		// Arrange
		_brandingBindingService.BindLogos(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyCollection<string>>())
			.Throws(new InvalidOperationException("package is locked"));
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Applied:") && message.Contains("logo")));
	}

	[Test, Category("Unit")]
	[Description("Relays the binding reconcile's skipped entries in the run output, because they are the only place a delivery gap is reported.")]
	public void Execute_ShouldRelayTheSkippedEntries_FromTheBindingReport() {
		// Arrange
		_brandingBindingService.BindLogos(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyCollection<string>>())
			.Returns(new BrandingScopeReport(BrandingScope.Logos, ["LogoImage"],
				["MenuLogoImage: no All-Users value on this environment"], false));
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Skipped:") && message.Contains("MenuLogoImage")));
	}

	[Test, Category("Unit")]
	[Description("Enforces the environment's file-security policy on a logo upload: a blocked extension fails the run before any write.")]
	public void Execute_ShouldFail_WhenTheFileSecurityPolicyBlocksTheExtension() {
		// Arrange
		_sysSettingsManager.GetFileSecurityPolicy().Returns(new FileSecurityPolicy(
			FileSecurityMode.AllowList, new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { "png" },
			AllowUnknownType: false));
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an svg upload under a png-only allow-list mirrors what the environment's own upload service would refuse");
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}
}
