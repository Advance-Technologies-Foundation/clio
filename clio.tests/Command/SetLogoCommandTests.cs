namespace Clio.Tests.Command;

using System;
using System.IO;
using System.Linq;
using System.Text;
using Clio.Command.Branding;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

/// <summary>
/// Unit coverage for <see cref="SetLogoCommand"/>: the all-slots <c>--logo</c> shortcut and its per-slot
/// overrides (login, menu, configuration, dark toolbar), the splash suppression toggle, and the
/// apply-then-bind flow into the target package. The real <see cref="SysSettingsCommand"/> sits under the
/// command so the Binary file write path (existing-type check, file-security policy, Base64 encoding) is
/// exercised, over a substituted <see cref="ISysSettingsManager"/> and file system.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class SetLogoCommandTests : BaseCommandTests<SetLogoOptions> {

	/// <summary>The package the substituted delivery target reports back as the resolved delivery target.</summary>
	private const string TestPackageName = "UsrBrandingPkg";

	private const string LogoFile = "C:/brand/logo.svg";
	private const string DarkLogoFile = "C:/brand/logo-white.svg";
	private static readonly byte[] LogoBytes = Encoding.UTF8.GetBytes("logo-image-bytes");

	/// <summary>Distinct bytes for the dark slot, so an override can be told apart from the all-slots file.</summary>
	private static readonly byte[] DarkLogoBytes = Encoding.UTF8.GetBytes("dark-logo-image-bytes");

	private const string FaviconFile = "C:/brand/favicon.ico";
	private static readonly byte[] FaviconBytes = Encoding.UTF8.GetBytes("favicon-image-bytes");

	private ISysSettingsManager _sysSettingsManager;
	private IFileSystem _fileSystem;
	private IPackageDataBinder _packageDataBinder;
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
			.Returns(callInfo => (string.Empty, callInfo.Arg<string>() switch {
				SetLogoCommand.HideSplashLogoCode => "Boolean",
				SetLogoCommand.UseFaviconCode => "Boolean",
				var _ => "Binary"
			}));
		_sysSettingsManager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
			.Returns(true);
		_packageDataBinder.UsePackage(Arg.Any<string>()).Returns(TestPackageName);
		_packageDataBinder
			.BindSysSettingsValue(Arg.Any<string>())
			.Returns(PackageDataBindingOutcome.Success());
	}

	public override void TearDown() {
		_sysSettingsManager.ClearReceivedCalls();
		_fileSystem.ClearReceivedCalls();
		_packageDataBinder.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_fileSystem = Substitute.For<IFileSystem>();
		_packageDataBinder = Substitute.For<IPackageDataBinder>();
		containerBuilder.AddTransient<ISysSettingsManager>(_ => _sysSettingsManager);
		containerBuilder.AddTransient<IFileSystem>(_ => _fileSystem);
		containerBuilder.AddTransient<IPackageDataBinder>(_ => _packageDataBinder);
	}

	[Test, Category("Unit")]
	[Description("Fails without touching the environment when no logo slot is passed — there is nothing to apply.")]
	public void Execute_ShouldFail_WhenNoLogoSlotIsPassed() {
		// Arrange
		SetLogoOptions options = new();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a run with no image file has nothing to apply");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("at least one image") && message.Contains("favicon")));
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Fails naming the slot and the path when a passed logo file does not exist, before writing anything.")]
	public void Execute_ShouldFail_WhenALogoFileDoesNotExist() {
		// Arrange
		_fileSystem.ExistsFile("C:/brand/missing.svg").Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile, MenuLogo = "C:/brand/missing.svg" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a typo in one path must fail the run before any slot is written");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("C:/brand/missing.svg") && message.Contains("menu-logo")));
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Names every missing file in one error, so a run with more than one bad path does not have to be repeated to discover the rest.")]
	public void Execute_ShouldNameEveryMissingFile_WhenSeveralPathsDoNotExist() {
		// Arrange
		_fileSystem.ExistsFile("C:/brand/missing.svg").Returns(false);
		_fileSystem.ExistsFile(FaviconFile).Returns(false);
		SetLogoOptions options = new() { MenuLogo = "C:/brand/missing.svg", Favicon = FaviconFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeFalse(because: "a missing input is a caller mistake, not a partial outcome");
		result.Error.Should().Contain("C:/brand/missing.svg",
			because: "the caller has several file arguments and needs to know which path is wrong");
		result.Error.Should().Contain(FaviconFile,
			because: "stopping at the first missing file would force a second run just to learn the next one");
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Writes the login-page logo setting as a Binary payload encoded from the passed file.")]
	public void Execute_ShouldWriteTheLoginLogoSetting_FromTheFile() {
		// Arrange
		SetLogoOptions options = new() { LoginLogo = LogoFile };

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
			LoginLogo = LogoFile,
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
		SetLogoOptions options = new() { LoginLogo = LogoFile };

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
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a failed splash toggle degrades the result, it does not undo the applied logos");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(SetLogoCommand.HideSplashLogoCode)));
	}

	[Test, Category("Unit")]
	[Description("Carries the failed splash toggle on the result's warnings, not only in the log, so a non-CLI caller such as the MCP tool sees the caveat too.")]
	public void ApplyLogos_ShouldCarryTheSplashFailure_OnTheResultWarnings() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.HideSplashLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Warnings.Should().Contain(warning => warning.Contains(SetLogoCommand.HideSplashLogoCode),
			because: "the logger is only the CLI surface; a caveat that lives nowhere but the log is invisible to the MCP caller, which is exactly how a delivery gap goes unnoticed");
	}

	[Test, Category("Unit")]
	[Description("Raises no caveat when the splash toggle failed but the environment already reports it suppressed, because a failed write that changed nothing leaves the user nothing to fix.")]
	public void ApplyLogos_ShouldNotWarn_WhenTheSplashToggleFailsButItAlreadyReadsAsSuppressed() {
		// Arrange
		GivenSplashAlreadySuppressed();
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.HideSplashLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeTrue(
			because: "the stock splash logo is already suppressed, so the failed write cost the user nothing");
		result.Warnings.Should().BeEmpty(
			because: "warning about a setting that already holds the wanted value would send the user chasing a non-problem");
		result.Bound.Should().Contain(SetLogoCommand.HideSplashLogoCode,
			because: "the setting reads as suppressed, so the value the package snapshots is the one the install target needs");
	}

	[Test, Category("Unit")]
	[Description("Keeps the warnings raised before a binding failure on the failure result, so a caveat is not swallowed by the later error.")]
	public void ApplyLogos_ShouldKeepEarlierWarnings_WhenTheBindingFails() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.HideSplashLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		_packageDataBinder.UsePackage(Arg.Any<string>())
			.Throws(new InvalidOperationException("SaveSchema rejected the binding"));
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Warnings.Should().Contain(warning => warning.Contains(SetLogoCommand.HideSplashLogoCode),
			because: "the splash toggle failed before the binding did, and the run already changed the environment — reporting only the binding error would hide the other thing the user has to fix");
	}

	[Test, Category("Unit")]
	[Description("Fails naming the slot when applying every requested logo setting fails, and never reaches the binding.")]
	public void Execute_ShouldFailNamingTheSlot_WhenApplyingEverySettingFails() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { MenuLogo = "C:/brand/menu.svg" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a slot the environment refused was not applied");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("menu-logo")));
		_packageDataBinder.DidNotReceiveWithAnyArgs().UsePackage(default);
	}

	[Test, Category("Unit")]
	[Description("Still delivers the slots that applied when another slot was refused, so the package never drifts away from what the environment now carries.")]
	public void Execute_ShouldStillDeliverTheAppliedSlots_WhenAnotherSlotIsRefused() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile, MenuLogo = "C:/brand/menu.svg" };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.Received(1).BindSysSettingsValue(SetLogoCommand.LoginLogoCode);
		_packageDataBinder.Received(1).BindSysSettingsValue(SetLogoCommand.HideSplashLogoCode);
		_packageDataBinder.DidNotReceive().BindSysSettingsValue(SetLogoCommand.MenuLogoCode);
	}

	[Test, Category("Unit")]
	[Description("Reports a refused slot as a failure even when other slots applied, because the caller asked for more than the run produced.")]
	public void Execute_ShouldFail_WhenOnlySomeSlotsApplied() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile, MenuLogo = "C:/brand/menu.svg" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "reporting success would hide the slot the user asked for and did not get");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("1 image(s) failed") && message.Contains("menu-logo")));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Applied:") && message.Contains("login-logo")));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains(TestPackageName)));
	}

	[Test, Category("Unit")]
	[Description("Leaves the package unset on the binding call when the caller names none, so the environment's CurrentPackageId decides where the data lands.")]
	public void Execute_ShouldLeaveThePackageUnset_WhenNoPackageIsNamed() {
		// Arrange
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.Received(1).UsePackage(
			Arg.Is<string>(package => string.IsNullOrWhiteSpace(package)));
	}

	[Test, Category("Unit")]
	[Description("Applies the all-slots --logo file to every logo slot in one run, so branding the whole product does not take four calls.")]
	public void Execute_ShouldApplyTheAllSlotsLogo_ToEverySlot() {
		// Arrange
		SetLogoOptions options = new() { Logo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_sysSettingsManager.Received(1).UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), "Binary");
	}

	[Test, Category("Unit")]
	[Description("Lets a slot option override the all-slots --logo file for its own slot, so a light variant can go on the dark top panel in the same run.")]
	public void Execute_ShouldLetASlotOptionOverrideTheAllSlotsLogo() {
		// Arrange
		SetLogoOptions options = new() { Logo = LogoFile, DarkLogo = DarkLogoFile };
		_fileSystem.OpenReadStream(DarkLogoFile).Returns(_ => new MemoryStream(DarkLogoBytes));

		// Act
		_command.Execute(options);

		// Assert
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetLogoCommand.DarkLogoCode,
			Arg.Is<object>(value => value.ToString() == Convert.ToBase64String(DarkLogoBytes)),
			"Binary");
	}

	[Test, Category("Unit")]
	[Description("Keeps the all-slots --logo file on the slots no slot option overrode, so an override narrows the fan-out instead of replacing it.")]
	public void Execute_ShouldKeepTheAllSlotsLogo_OnSlotsWithoutAnOverride() {
		// Arrange
		SetLogoOptions options = new() { Logo = LogoFile, DarkLogo = DarkLogoFile };
		_fileSystem.OpenReadStream(DarkLogoFile).Returns(_ => new MemoryStream(DarkLogoBytes));

		// Act
		_command.Execute(options);

		// Assert
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetLogoCommand.LoginLogoCode,
			Arg.Is<object>(value => value.ToString() == Convert.ToBase64String(LogoBytes)),
			"Binary");
	}

	[Test, Category("Unit")]
	[Description("Passes exactly the applied setting codes to the binding, so a slot this run never wrote cannot be shipped as newly branded.")]
	public void Execute_ShouldBindOnlyTheAppliedSettingCodes() {
		// Arrange
		SetLogoOptions options = new() { LoginLogo = LogoFile, DarkLogo = DarkLogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.Received(1).BindSysSettingsValue(SetLogoCommand.LoginLogoCode);
		_packageDataBinder.Received(1).BindSysSettingsValue(SetLogoCommand.DarkLogoCode);
		_packageDataBinder.Received(1).BindSysSettingsValue(SetLogoCommand.HideSplashLogoCode);
		_packageDataBinder.DidNotReceive().BindSysSettingsValue(SetLogoCommand.MenuLogoCode);
	}

	[Test, Category("Unit")]
	[Description("Excludes the splash toggle from the applied codes when its write failed, so the binding cannot ship a splash state this run never wrote.")]
	public void Execute_ShouldNotBindTheSplashToggle_WhenItsWriteFailed() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.HideSplashLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.DidNotReceive().BindSysSettingsValue(SetLogoCommand.HideSplashLogoCode);
	}

	[Test, Category("Unit")]
	[Description("Binds the logos into the caller-named package instead of the default.")]
	public void Execute_ShouldBindLogosIntoNamedPackage_WhenPackageIsPassed() {
		// Arrange
		_packageDataBinder.UsePackage("UsrMyApp").Returns("UsrMyApp");
		SetLogoOptions options = new() { LoginLogo = LogoFile, PackageName = "UsrMyApp" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a user-named package must be honored");
		_packageDataBinder.Received(1).UsePackage("UsrMyApp");
	}

	[Test, Category("Unit")]
	[Description("Reports the bound package in the run output so the user learns where the logo data landed.")]
	public void Execute_ShouldNameTheBoundPackage_InTheRunOutput() {
		// Arrange
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains($"bound into package '{TestPackageName}'")));
	}

	[Test, Category("Unit")]
	[Description("Fails naming the package and asking for a re-run when the logos applied but the binding failed, so a delivery failure is never silent.")]
	public void Execute_ShouldFailNamingThePackage_WhenBindingFails() {
		// Arrange
		_packageDataBinder.UsePackage(Arg.Any<string>())
			.Throws(new InvalidOperationException("package is locked"));
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the user asked for logos that ship with the package, and the package part failed");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("package is locked")));
	}

	[Test, Category("Unit")]
	[Description("Still reports the slots that were applied before a failure, so the caller sees the partial state instead of assuming nothing changed.")]
	public void Execute_ShouldReportAppliedSlots_WhenALaterStepFails() {
		// Arrange
		_packageDataBinder.UsePackage(Arg.Any<string>())
			.Throws(new InvalidOperationException("package is locked"));
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Applied:") && message.Contains("logo")));
	}

	[Test, Category("Unit")]
	[Description("Relays the binding reconcile's warnings in the run output at warning level, because they are the only place a delivery gap is reported and info level would give a gap the same weight as a success line.")]
	public void Execute_ShouldRelayTheBindingWarnings_AtWarningLevel() {
		// Arrange
		_packageDataBinder
			.BindSysSettingsValue(SetLogoCommand.LoginLogoCode)
			.Returns(PackageDataBindingOutcome.Refused(
				[$"{SetLogoCommand.LoginLogoCode}: no All-Users value on this environment"]));
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(SetLogoCommand.LoginLogoCode)));
	}

	[Test, Category("Unit")]
	[Description("Enforces the environment's file-security policy on a logo upload: a blocked extension fails the run before any write.")]
	public void Execute_ShouldFail_WhenTheFileSecurityPolicyBlocksTheExtension() {
		// Arrange
		_sysSettingsManager.GetFileSecurityPolicy().Returns(new FileSecurityPolicy(
			FileSecurityMode.AllowList, new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { "png" },
			AllowUnknownType: false));
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "an svg upload under a png-only allow-list mirrors what the environment's own upload service would refuse");
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Reports what the package already carries when a later delivery throws, so the caller is not told the whole binding failed while part of it landed.")]
	public void Execute_ShouldReportTheAlreadyBoundSettings_WhenALaterDeliveryFails() {
		// Arrange
		_packageDataBinder.BindSysSettingsValue(SetLogoCommand.HideSplashLogoCode)
			.Throws(new InvalidOperationException("SaveSchema rejected the binding"));
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "part of the delivery the user asked for did not land");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("SaveSchema rejected the binding")));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains($"Branding data bound into package '{TestPackageName}'")
			&& message.Contains(SetLogoCommand.LoginLogoCode)));
	}

	[Test, Category("Unit")]
	[Description("Names the slots the delivery actually landed when another slot was refused, instead of claiming every applied slot was bound.")]
	public void Execute_ShouldNameTheBoundSettings_WhenOnlySomeSlotsApplied() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile, MenuLogo = "C:/brand/menu.svg" };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains($"Branding data bound into package '{TestPackageName}'")
			&& message.Contains(SetLogoCommand.LoginLogoCode)));
	}

	[Test, Category("Unit")]
	[Description("Says nothing was bound when every applied slot's delivery was refused, so the failure never claims package changes the warnings contradict.")]
	public void Execute_ShouldSayNothingWasBound_WhenEveryDeliveryIsRefused() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		_packageDataBinder.BindSysSettingsValue(Arg.Any<string>())
			.Returns(PackageDataBindingOutcome.Refused(["no All-Users value on this environment"]));
		SetLogoOptions options = new() { LoginLogo = LogoFile, MenuLogo = "C:/brand/menu.svg" };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains($"No branding data could be bound into package '{TestPackageName}'")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message =>
			message.Contains("Branding data bound into package")));
	}

	[Test, Category("Unit")]
	[Description("Says nothing was bound on a successful run whose every delivery was refused, so the package line never claims a delivery the warnings beside it contradict.")]
	public void Execute_ShouldSayNothingWasBound_WhenEveryDeliveryIsRefusedOnASuccessfulRun() {
		// Arrange
		_packageDataBinder.BindSysSettingsValue(Arg.Any<string>())
			.Returns(PackageDataBindingOutcome.Refused(["no All-Users value on this environment"]));
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "every slot applied — a delivery gap is a warning channel, not an apply failure");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains($"No branding data could be bound into package '{TestPackageName}'")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message =>
			message.Contains("Branding data bound into package")));
	}

	[Test, Category("Unit")]
	[Description("Names the settings the delivery landed on a fully successful run, so the package line reports what the package actually carries.")]
	public void Execute_ShouldNameTheBoundSettings_WhenEveryDeliverySucceeds() {
		// Arrange
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains($"Branding data bound into package '{TestPackageName}'")
			&& message.Contains(SetLogoCommand.LoginLogoCode)
			&& message.Contains(SetLogoCommand.HideSplashLogoCode)));
	}

	[Test, Category("Unit")]
	[Description("Carries the resolved package on a partial failure, so a structured caller learns where the applied slots landed without parsing the message.")]
	public void ApplyLogos_ShouldCarryThePackage_WhenOnlySomeSlotsApplied() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile, MenuLogo = "C:/brand/menu.svg" };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeFalse(because: "a slot the caller asked for was refused");
		result.Package.Should().Be(TestPackageName,
			because: "the applied slots were bound into it, so the field its own contract describes must name it");
	}

	[Test, Category("Unit")]
	[Description("Carries the bound settings on a partial failure, so the field its own contract describes is not empty while the package already carries them.")]
	public void ApplyLogos_ShouldCarryTheBoundSettings_WhenOnlySomeSlotsApplied() {
		// Arrange
		_sysSettingsManager.UpdateSysSetting(SetLogoCommand.MenuLogoCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile, MenuLogo = "C:/brand/menu.svg" };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeFalse(because: "a slot the caller asked for was refused");
		result.Bound.Should().Contain(SetLogoCommand.LoginLogoCode,
			because: "an empty Bound must mean the package carries nothing from this run, so a failure that did bind must say so");
	}

	[Test, Category("Unit")]
	[Description("Never starts delivering a slot the user never branded — an unshipped, unapplied slot stays out of the package.")]
	public void Execute_ShouldNotDeliverASlot_ThatWasNeverAppliedOrShipped() {
		// Arrange
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_packageDataBinder.DidNotReceive().BindSysSettingsValue(SetLogoCommand.ConfigurationLogoCode);
	}

	[Test, Category("Unit")]
	[Description("Writes the favicon image and binds both the image and the gate it depends on.")]
	public void Execute_ShouldApplyAndBindTheFaviconWithItsGate() {
		// Arrange
		GivenFaviconFile();
		SetLogoOptions options = new() { LoginLogo = LogoFile, Favicon = FaviconFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeTrue(because: "the logo and the favicon both applied");
		result.Applied.Should().Contain(SetLogoCommand.FaviconLabel,
			because: "the caller reads Applied to learn the favicon landed, and the sign-out notice keys off it");
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetLogoCommand.FaviconCode,
			Arg.Is<object>(value => value.ToString() == Convert.ToBase64String(FaviconBytes)),
			"Binary");
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetLogoCommand.UseFaviconCode,
			Arg.Is<object>(value => value.ToString() == "true"),
			Arg.Any<string>());
		_packageDataBinder.Received(1).BindSysSettingsValue(SetLogoCommand.FaviconCode);
		_packageDataBinder.Received(1).BindSysSettingsValue(SetLogoCommand.UseFaviconCode);
	}

	[Test, Category("Unit")]
	[Description("Writes the gate even when the environment already reports it on, so the run never depends on reading a value back before deciding.")]
	public void Execute_ShouldWriteTheGate_EvenWhenTheEnvironmentReportsItOn() {
		// Arrange
		GivenFaviconFile();
		GivenGateAlreadyOn();
		SetLogoOptions options = new() { Favicon = FaviconFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeTrue(because: "the favicon alone is a complete request");
		_sysSettingsManager.Received(1).UpdateSysSetting(
			SetLogoCommand.UseFaviconCode,
			Arg.Is<object>(value => value.ToString() == "true"),
			Arg.Any<string>());
		_packageDataBinder.Received(1).BindSysSettingsValue(SetLogoCommand.UseFaviconCode);
	}

	[Test, Category("Unit")]
	[Description("Fails the run when the favicon gate could not be turned on and the environment does not already report it on, because the icon is inert without the gate.")]
	public void Execute_ShouldFail_WhenTheFaviconGateCannotBeTurnedOn() {
		// Arrange
		GivenFaviconFile();
		_sysSettingsManager
			.UpdateSysSetting(SetLogoCommand.UseFaviconCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { Favicon = FaviconFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeFalse(
			because: "success would tell the caller the tab icon is done while the platform keeps the stock one");
		result.Error.Should().Contain(SetLogoCommand.UseFaviconCode,
			because: "the failure has to name the setting that leaves the written icon inert");
		result.Bound.Should().Contain(SetLogoCommand.FaviconCode,
			because: "the image itself did land on the environment, so the package should carry it");
		result.Bound.Should().NotContain(SetLogoCommand.UseFaviconCode,
			because: "the binding snapshots the value at save time, so binding an off gate would switch the favicon off on the install target");
		_packageDataBinder.DidNotReceive().RemoveSysSettingsValue(SetLogoCommand.UseFaviconCode);
	}

	[Test, Category("Unit")]
	[Description("Succeeds and still binds the gate when its write failed but the environment already reports it on, so a failed write that changed nothing is not reported as a broken favicon.")]
	public void Execute_ShouldSucceed_WhenTheGateWriteFailsButItAlreadyReadsAsOn() {
		// Arrange
		GivenFaviconFile();
		GivenGateAlreadyOn();
		_sysSettingsManager
			.UpdateSysSetting(SetLogoCommand.UseFaviconCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
		SetLogoOptions options = new() { Favicon = FaviconFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeTrue(
			because: "the gate is already on, so the favicon will display and the failed write changed nothing");
		result.Warnings.Should().BeEmpty(
			because: "nothing on this environment is left in a state the caller has to act on");
		result.Bound.Should().Contain(SetLogoCommand.UseFaviconCode,
			because: "the gate reads as on, so the value the package snapshots is the one the install target needs");
	}

	[Test, Category("Unit")]
	[Description("Fails the run when the environment refuses the favicon, yet keeps the logo that already applied both applied and bound.")]
	public void Execute_ShouldFailButKeepAppliedLogos_WhenTheFaviconIsRefused() {
		// Arrange
		GivenFaviconFile();
		GivenFaviconWriteFails();
		SetLogoOptions options = new() { LoginLogo = LogoFile, Favicon = FaviconFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeFalse(
			because: "the caller asked for a favicon and did not get it, exactly as for a refused logo slot");
		result.Error.Should().Contain(SetLogoCommand.FaviconLabel,
			because: "the failure has to name which image the environment refused");
		result.Applied.Should().NotContain(SetLogoCommand.FaviconLabel,
			because: "a refused favicon never reached the environment, so the sign-out notice must stay silent");
		result.Bound.Should().Contain(SetLogoCommand.LoginLogoCode,
			because: "a refused favicon must not discard a logo that already landed");
		_packageDataBinder.DidNotReceive().BindSysSettingsValue(SetLogoCommand.FaviconCode);
	}

	[Test, Category("Unit")]
	[Description("Fails a favicon-only run whose icon was refused, since nothing at all reached the environment.")]
	public void Execute_ShouldFail_WhenTheFaviconWasTheOnlyRequestAndWasRefused() {
		// Arrange
		GivenFaviconFile();
		GivenFaviconWriteFails();
		SetLogoOptions options = new() { Favicon = FaviconFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeFalse(because: "the run produced nothing the caller asked for");
		result.Error.Should().Contain(SetLogoCommand.FaviconCode,
			because: "the failure has to name the setting that could not be written");
		_packageDataBinder.DidNotReceiveWithAnyArgs().UsePackage(default);
	}

	[Test, Category("Unit")]
	[Description("Fails naming the favicon when its file does not exist, before writing anything.")]
	public void Execute_ShouldFail_WhenTheFaviconFileIsMissing() {
		// Arrange
		_fileSystem.ExistsFile(FaviconFile).Returns(false);
		SetLogoOptions options = new() { LoginLogo = LogoFile, Favicon = FaviconFile };

		// Act
		SetLogoResult result = _command.ApplyLogos(options);

		// Assert
		result.Success.Should().BeFalse(because: "a missing input is a caller mistake, not a partial outcome");
		result.Error.Should().Contain("favicon",
			because: "the caller has several file arguments and needs to know which path is wrong");
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

	[Test, Category("Unit")]
	[Description("Never touches the favicon settings on a logos-only run, so a favicon bound earlier is left alone.")]
	public void Execute_ShouldLeaveTheFaviconAlone_WhenNoFaviconIsPassed() {
		// Arrange
		SetLogoOptions options = new() { LoginLogo = LogoFile };

		// Act
		_command.Execute(options);

		// Assert
		_sysSettingsManager.DidNotReceive().UpdateSysSetting(
			SetLogoCommand.FaviconCode, Arg.Any<object>(), Arg.Any<string>());
		_packageDataBinder.DidNotReceive().BindSysSettingsValue(SetLogoCommand.FaviconCode);
		_packageDataBinder.DidNotReceive().BindSysSettingsValue(SetLogoCommand.UseFaviconCode);
		_packageDataBinder.DidNotReceive().RemoveSysSettingsValue(SetLogoCommand.UseFaviconCode);
	}

	[Test, Category("Unit")]
	[Description("Tells the user a page refresh is enough when a logo applied, because a logo does appear on the next load.")]
	public void Execute_ShouldPromiseAPageRefresh_WhenALogoApplied() {
		// Arrange
		GivenFaviconFile();
		SetLogoOptions options = new() { LoginLogo = LogoFile, Favicon = FaviconFile };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Applied:") && message.Contains("after a page refresh")));
	}

	[Test, Category("Unit")]
	[Description("Leaves the page-refresh promise out of a favicon-only run, because a favicon never appears on a refresh and the run would otherwise contradict the sign-out notice printed next to it.")]
	public void Execute_ShouldNotPromiseAPageRefresh_WhenOnlyTheFaviconApplied() {
		// Arrange
		GivenFaviconFile();
		SetLogoOptions options = new() { Favicon = FaviconFile };

		// Act
		_command.Execute(options);

		// Assert
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Applied:") && message.Contains(SetLogoCommand.FaviconLabel)));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message =>
			message.Contains("after a page refresh")));
	}

	private void GivenFaviconFile() {
		_fileSystem.ExistsFile(FaviconFile).Returns(true);
		_fileSystem.OpenReadStream(FaviconFile).Returns(_ => new MemoryStream(FaviconBytes));
	}

	private void GivenFaviconWriteFails() {
		_sysSettingsManager
			.UpdateSysSetting(SetLogoCommand.FaviconCode, Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);
	}

	private void GivenGateAlreadyOn() {
		_sysSettingsManager.GetAllUsersDefaultWithType(SetLogoCommand.UseFaviconCode)
			.Returns(("true", "Boolean"));
	}

	private void GivenSplashAlreadySuppressed() {
		_sysSettingsManager.GetAllUsersDefaultWithType(SetLogoCommand.HideSplashLogoCode)
			.Returns(("true", "Boolean"));
	}
}
