using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Branding;

/// <summary>
/// Options for the <c>set-logo</c> command. <see cref="Logo"/> brands every slot at once from one file;
/// each slot option brands that slot alone and overrides <see cref="Logo"/> for it. At least one of them is
/// required. Every applied logo is also bound into <see cref="PackageName"/> as package data so it ships
/// with an install.
/// </summary>
[Verb("set-logo",
	HelpText = "Apply the product logos from local image files and bind them into a package as data bindings")]
public class SetLogoOptions : RemoteCommandOptions {

	/// <summary>
	/// Local image file applied to every logo slot at once. A slot option (<see cref="LoginLogo"/>,
	/// <see cref="MenuLogo"/>, <see cref="ConfigurationLogo"/>, <see cref="DarkLogo"/>) wins for its own slot.
	/// </summary>
	[Option("logo", Required = false,
		HelpText = "Local image file applied to every logo slot at once. A slot option (--login-logo, " +
			"--menu-logo, --configuration-logo, --dark-logo) overrides it for that slot.")]
	public string Logo { get; set; }

	/// <summary>Local image file for the login-page logo.</summary>
	[Option("login-logo", Required = false,
		HelpText = "Local image file for the logo on the login page (LogoImage)")]
	public string LoginLogo { get; set; }

	/// <summary>Local image file for the main-menu logo.</summary>
	[Option("menu-logo", Required = false,
		HelpText = "Local image file for the main menu logo (MenuLogoImage)")]
	public string MenuLogo { get; set; }

	/// <summary>Local image file for the configuration-page logo.</summary>
	[Option("configuration-logo", Required = false,
		HelpText = "Local image file for the configuration page logo (ConfigurationPageLogoImage)")]
	public string ConfigurationLogo { get; set; }

	/// <summary>Local image file for the dark-surface logo (the Freedom UI top panel).</summary>
	[Option("dark-logo", Required = false,
		HelpText = "Local image file for the logo on the dark Freedom UI top panel (CrtAppToolbarLogo). " +
			"Pass the light variant of the logo here — a logo drawn for a white background is hard to read " +
			"on the dark panel.")]
	public string DarkLogo { get; set; }

	/// <summary>Package that receives the logo data bindings.</summary>
	[Option("package", Required = false,
		HelpText = "Package that receives the logo data bindings. When omitted, the package from the " +
			"environment's CurrentPackageId system setting is used.")]
	public string PackageName { get; set; }
}

/// <summary>
/// Outcome of applying and binding the product logos.
/// </summary>
public sealed record SetLogoResult {

	/// <summary>Whether every requested logo was applied and bound.</summary>
	public bool Success { get; private init; }

	/// <summary>Labels of the logo slots that were applied, in request order.</summary>
	public IReadOnlyList<string> Applied { get; private init; } = [];

	/// <summary>The package the logo data was bound into; null when the run failed before binding.</summary>
	public string Package { get; private init; }

	/// <summary>Delivery gaps reported by the binding reconcile (slots without a value, dropped bindings).</summary>
	public IReadOnlyList<string> Skipped { get; private init; } = [];

	/// <summary>A non-fatal problem the caller should surface (for example a failed splash-logo toggle).</summary>
	public string Warning { get; private init; }

	/// <summary>The failure message; null on success.</summary>
	public string Error { get; private init; }

	/// <summary>Creates a success result carrying what was applied, bound, and skipped.</summary>
	public static SetLogoResult Successful(IReadOnlyList<string> applied, string package,
		IReadOnlyList<string> skipped, string warning = null) =>
		new() { Success = true, Applied = applied, Package = package, Skipped = skipped, Warning = warning };

	/// <summary>
	/// Creates a failure result. <paramref name="applied"/> carries the slots that were already written
	/// before the failure, so the caller can see the partial state instead of assuming nothing changed.
	/// </summary>
	public static SetLogoResult Failure(string error, IReadOnlyList<string> applied = null) =>
		new() { Success = false, Error = error, Applied = applied ?? [] };
}

/// <summary>
/// Applies the product logos from local image files — one Binary sys-setting per slot — and binds the
/// applied values into a package as Creatio data bindings, so the logos ship with the package instead of
/// living only on this environment. Also sets <c>HideSplashScreenLogoImage</c> so the stock splash logo
/// does not flash during load. Re-running with a new file refreshes both the environment and the
/// packaged snapshot.
/// </summary>
public class SetLogoCommand : RemoteCommand<SetLogoOptions> {

	/// <summary>The login-page logo setting.</summary>
	internal const string LoginLogoCode = "LogoImage";

	/// <summary>The main-menu logo setting.</summary>
	internal const string MenuLogoCode = "MenuLogoImage";

	/// <summary>The configuration-page logo setting.</summary>
	internal const string ConfigurationLogoCode = "ConfigurationPageLogoImage";

	/// <summary>The dark-surface (Freedom UI top panel) logo setting.</summary>
	internal const string DarkLogoCode = "CrtAppToolbarLogo";

	/// <summary>The Boolean setting that suppresses the stock splash-screen logo.</summary>
	internal const string HideSplashLogoCode = "HideSplashScreenLogoImage";

	/// <summary>Error text shared by the CLI and MCP surfaces when no logo file is passed.</summary>
	internal const string NoLogoError =
		"Pass at least one logo file: logo (every slot at once), login-logo, menu-logo, configuration-logo, " +
		"or dark-logo.";

	private readonly SysSettingsCommand _sysSettingsCommand;
	private readonly IBrandingBindingService _brandingBindingService;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetLogoCommand"/> class.
	/// </summary>
	public SetLogoCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		SysSettingsCommand sysSettingsCommand, IBrandingBindingService brandingBindingService,
		IFileSystem fileSystem)
		: base(applicationClient, settings) {
		_sysSettingsCommand = sysSettingsCommand;
		_brandingBindingService = brandingBindingService;
		_fileSystem = fileSystem;
	}

	/// <summary>One requested logo slot: its user-facing label, target sys-setting code, and source file.</summary>
	private sealed record LogoSlot(string Label, string Code, string File);

	/// <summary>
	/// Applies every logo file in <paramref name="options"/> to its sys-setting slot, suppresses the stock
	/// splash logo, and reconciles the logo data bindings in the target package (creating the binding when it
	/// does not exist yet, updating it when it does). At least one logo option is required.
	/// </summary>
	/// <param name="options">Command options carrying the per-slot files, target package, and connection settings.</param>
	/// <returns>The outcome, carrying the applied slots, the bound package, and any delivery gaps.</returns>
	public virtual SetLogoResult ApplyLogos(SetLogoOptions options) {
		IReadOnlyList<LogoSlot> slots = CollectRequestedSlots(options);
		if (slots.Count == 0) {
			return SetLogoResult.Failure(NoLogoError);
		}
		foreach (LogoSlot slot in slots) {
			if (!_fileSystem.ExistsFile(slot.File)) {
				return SetLogoResult.Failure($"File not found: '{slot.File}' (passed for {slot.Label}).");
			}
		}

		List<string> applied = [];
		List<string> appliedCodes = [];
		foreach (LogoSlot slot in slots) {
			SysSettingUpdateResult update = _sysSettingsCommand.TryUpdateSysSetting(
				new UpdateSysSettingArgs(options.Environment ?? string.Empty, slot.Code, ValueFilePath: slot.File));
			if (!update.Success) {
				return SetLogoResult.Failure(
					$"Applying {slot.Label} ({slot.Code}) from '{slot.File}' failed: {update.Error}", applied);
			}
			applied.Add(slot.Label);
			appliedCodes.Add(slot.Code);
		}

		string warning = null;
		SysSettingUpdateResult splash = _sysSettingsCommand.TryUpdateSysSetting(
			new UpdateSysSettingArgs(options.Environment ?? string.Empty, HideSplashLogoCode, Value: "true"));
		if (splash.Success) {
			appliedCodes.Add(HideSplashLogoCode);
		} else {
			warning = $"The logos were applied, but setting {HideSplashLogoCode} failed, so the stock splash " +
				$"logo may still flash during load: {splash.Error}";
		}

		try {
			BrandingScopeReport report = _brandingBindingService.BindLogos(options.PackageName, appliedCodes);
			return SetLogoResult.Successful(applied, report.Package, report.Skipped, warning);
		} catch (Exception exception) {
			return SetLogoResult.Failure(
				$"The logos were applied, but binding them into " +
				$"{BrandingBindingService.DescribeTargetPackage(options.PackageName)} failed: {exception.Message} " +
				"Re-run the command to retry the binding.", applied);
		}
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(SetLogoOptions options) {
		SetLogoResult result = ApplyLogos(options);
		if (result.Applied.Count > 0) {
			Logger.WriteInfo($"Applied: {string.Join(", ", result.Applied)}. Users see the new logos after a page refresh.");
		}
		if (!string.IsNullOrWhiteSpace(result.Warning)) {
			Logger.WriteWarning(result.Warning);
		}
		if (!result.Success) {
			CommandSuccess = false;
			Logger.WriteError(result.Error);
			return;
		}
		Logger.WriteInfo($"Logo data bound into package '{result.Package}'.");
		foreach (string skipped in result.Skipped) {
			Logger.WriteInfo($"Skipped: {skipped}.");
		}
	}

	/// <summary>
	/// Expands the options into the slots to write. <c>--logo</c> seeds every slot from one file so branding all
	/// four takes a single call; a slot option overrides it for its own slot, which is how a brand with a light
	/// variant for the dark top panel supplies both in the same run.
	/// </summary>
	private static IReadOnlyList<LogoSlot> CollectRequestedSlots(SetLogoOptions options) {
		(string Label, string Code, string File)[] candidates = [
			("login-logo", LoginLogoCode, SlotFile(options.LoginLogo, options.Logo)),
			("menu-logo", MenuLogoCode, SlotFile(options.MenuLogo, options.Logo)),
			("configuration-logo", ConfigurationLogoCode, SlotFile(options.ConfigurationLogo, options.Logo)),
			("dark-logo", DarkLogoCode, SlotFile(options.DarkLogo, options.Logo))
		];
		return candidates
			.Where(candidate => !string.IsNullOrWhiteSpace(candidate.File))
			.Select(candidate => new LogoSlot(candidate.Label, candidate.Code, candidate.File))
			.ToList();
	}

	/// <summary>Returns the slot-specific file when one was passed, otherwise the all-slots <c>--logo</c> file.</summary>
	private static string SlotFile(string slotFile, string allSlotsFile) =>
		string.IsNullOrWhiteSpace(slotFile) ? allSlotsFile : slotFile;
}
