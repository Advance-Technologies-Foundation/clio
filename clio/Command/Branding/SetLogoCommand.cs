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

/// <summary>One requested logo slot: its user-facing label, target sys-setting code, and source file.</summary>
internal sealed record LogoSlot(string Label, string Code, string File);

/// <summary>
/// Outcome of applying and binding the product logos.
/// </summary>
public sealed record SetLogoResult {

	/// <summary>Whether every requested logo was applied and bound.</summary>
	public bool Success { get; private init; }

	/// <summary>Labels of the logo slots that were applied, in request order.</summary>
	public IReadOnlyList<string> Applied { get; private init; } = [];

	/// <summary>
	/// Codes of the settings the package delivery confirmed it bound, in delivery order. Empty means the
	/// package carries nothing from this run even when <see cref="Applied"/> is not — every binding can be
	/// refused while every apply succeeds, and <see cref="Warnings"/> then names the reason for each.
	/// </summary>
	public IReadOnlyList<string> Bound { get; private init; } = [];

	/// <summary>
	/// The package the logo data was bound into; null only when the run never got as far as resolving one.
	/// Populated on a partial failure too, because the slots that did apply were bound into it.
	/// </summary>
	public string Package { get; private init; }

	/// <summary>
	/// Every non-fatal problem the caller should surface, in the order it arose: an apply-side caveat such as a
	/// failed splash-logo toggle, then each delivery gap (a slot without a value, a slot refused by policy, a
	/// dropped binding). One channel rather than two, because a caveat and a gap are the same thing to the
	/// caller — the run succeeded but delivers less than it looks like it did.
	/// </summary>
	public IReadOnlyList<string> Warnings { get; private init; } = [];

	/// <summary>The failure message; null on success.</summary>
	public string Error { get; private init; }

	/// <summary>Creates a success result carrying what was applied, the target package, and any warnings.</summary>
	public static SetLogoResult Successful(IReadOnlyList<string> applied, string package,
		IReadOnlyList<string> warnings, IReadOnlyList<string> bound = null) {
		return new SetLogoResult {
			Success = true, Applied = applied, Package = package, Warnings = warnings ?? [],
			Bound = bound ?? []
		};
	}

	/// <summary>
	/// Creates a failure result. <paramref name="applied"/> carries the slots that were already written
	/// before the failure, so the caller can see the partial state instead of assuming nothing changed;
	/// <paramref name="warnings"/> carries the caveats raised before it, which the failure must not swallow;
	/// <paramref name="package"/> names the delivery target once one was resolved, so a caller reading the
	/// structured result learns where the applied slots landed without parsing the message.
	/// </summary>
	public static SetLogoResult Failure(string error, IReadOnlyList<string> applied = null,
		IReadOnlyList<string> warnings = null, string package = null, IReadOnlyList<string> bound = null) {
		return new SetLogoResult {
			Success = false, Error = error, Applied = applied ?? [], Warnings = warnings ?? [], Package = package,
			Bound = bound ?? []
		};
	}

	internal static SetLogoResult FromSlots(
		IReadOnlyList<LogoSlot> appliedSlots,
		IReadOnlyList<(LogoSlot Slot, string Error)> failedSlots,
		string package = null,
		IReadOnlyList<string> bound = null,
		IReadOnlyList<string> warnings = null,
		string error = null) {
		List<string> applied = appliedSlots.Select(slot => slot.Label).ToList();
		string slotFailures = failedSlots.Count == 0
			? null
			: DescribeFailedSlots(appliedSlots.Count, failedSlots);
		if (error is not null) {
			return Failure(
				slotFailures is null ? error : $"{slotFailures} {error}", applied, warnings, package, bound);
		}
		if (slotFailures is null) {
			return Successful(applied, package, warnings, bound);
		}
		return Failure($"{slotFailures}{DescribeDelivery(package, bound)}", applied, warnings, package, bound);
	}

	private static string DescribeFailedSlots(
		int appliedCount, IReadOnlyList<(LogoSlot Slot, string Error)> failedSlots) {
		string details = string.Join("; ", failedSlots.Select(failure =>
			$"{failure.Slot.Label} ({failure.Slot.Code}) from '{failure.Slot.File}': {failure.Error}"));
		return $"Applying {failedSlots.Count} of {appliedCount + failedSlots.Count} logo slot(s) failed: {details}.";
	}

	private static string DescribeDelivery(string package, IReadOnlyList<string> bound) {
		if (package is null) {
			return string.Empty;
		}
		return bound is { Count: > 0 }
			? $" Settings bound into package '{package}': {string.Join(", ", bound)}."
			: $" No setting could be bound into package '{package}'; the warnings name the reason for each.";
	}
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
	private readonly IPackageDataBinder _packageDataBinder;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetLogoCommand"/> class.
	/// </summary>
	public SetLogoCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		SysSettingsCommand sysSettingsCommand, IPackageDataBinder packageDataBinder, IFileSystem fileSystem)
		: base(applicationClient, settings) {
		_sysSettingsCommand = sysSettingsCommand;
		_packageDataBinder = packageDataBinder;
		_fileSystem = fileSystem;
	}

	/// <summary>
	/// Applies every logo file in <paramref name="options"/> to its sys-setting slot, suppresses the stock
	/// splash logo, and delivers the applied values into the target package so they ship with it. At least
	/// one logo option is required.
	/// </summary>
	/// <remarks>
	/// A slot the environment refuses does not abandon the slots that already succeeded: as long as one slot
	/// applied, the run still suppresses the splash logo and delivers the applied slots into the package, so
	/// the environment and the package never drift apart. The result is a failure naming every refused slot,
	/// because the caller asked for more than the run produced — read <see cref="SetLogoResult.Applied"/> to
	/// see what did land before re-running with the refused slots fixed.
	/// </remarks>
	/// <param name="options">Command options carrying the per-slot files, target package, and connection settings.</param>
	/// <returns>The outcome, carrying the applied slots, the bound package, and any warnings.</returns>
	public virtual SetLogoResult ApplyLogos(SetLogoOptions options) {
		IReadOnlyList<LogoSlot> slots = ResolveSlots(options);
		if (slots.Count == 0) {
			return SetLogoResult.Failure(NoLogoError);
		}
		LogoSlot missingFile = slots.FirstOrDefault(slot => !_fileSystem.ExistsFile(slot.File));
		if (missingFile is not null) {
			return SetLogoResult.Failure($"File not found: '{missingFile.File}' (passed for {missingFile.Label}).");
		}
		List<LogoSlot> appliedSlots = [];
		List<(LogoSlot Slot, string Error)> failedSlots = [];
		foreach (LogoSlot slot in slots) {
			SysSettingUpdateResult update = _sysSettingsCommand.TryUpdateSysSetting(
				new UpdateSysSettingArgs(options.Environment ?? string.Empty, slot.Code, ValueFilePath: slot.File));
			if (update.Success) {
				appliedSlots.Add(slot);
			} else {
				failedSlots.Add((slot, update.Error));
			}
		}
		if (appliedSlots.Count == 0) {
			return SetLogoResult.FromSlots(appliedSlots, failedSlots);
		}

		List<string> warnings = [];
		List<string> appliedCodes = appliedSlots.Select(slot => slot.Code).ToList();
		SysSettingUpdateResult splash = _sysSettingsCommand.TryUpdateSysSetting(
			new UpdateSysSettingArgs(options.Environment ?? string.Empty, HideSplashLogoCode, Value: "true"));
		if (splash.Success) {
			appliedCodes.Add(HideSplashLogoCode);
		} else {
			warnings.Add($"The logos were applied, but setting {HideSplashLogoCode} failed, so the stock splash " +
				$"logo may still flash during load: {splash.Error}");
		}
		return BindLogos(appliedSlots, failedSlots, appliedCodes, options.PackageName, warnings);
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(SetLogoOptions options) {
		SetLogoResult result = ApplyLogos(options);
		if (result.Applied.Count > 0) {
			Logger.WriteInfo($"Applied: {string.Join(", ", result.Applied)}. Users see the new logos after a page refresh.");
		}
		if (!result.Success) {
			Logger.WriteWarnings(result.Warnings);
			CommandSuccess = false;
			Logger.WriteError(result.Error);
			return;
		}
		Logger.WriteInfo(result.Bound.Count > 0
			? $"Logo data bound into package '{result.Package}': {string.Join(", ", result.Bound)}."
			: $"No logo data could be bound into package '{result.Package}'; the warnings name the reason for each.");
		Logger.WriteWarnings(result.Warnings);
	}

	private SetLogoResult BindLogos(List<LogoSlot> appliedSlots, List<(LogoSlot Slot, string Error)> failedSlots,
		IReadOnlyList<string> appliedCodes, string packageName, List<string> warnings) {
		string package = null;
		List<string> bound = [];
		try {
			package = _packageDataBinder.UsePackage(packageName);
			foreach (string code in appliedCodes) {
				PackageDataBindingOutcome outcome = _packageDataBinder.BindSysSettingsValue(code);
				warnings.AddRange(outcome.Warnings);
				if (outcome.Bound) {
					bound.Add(code);
				}
			}
			return SetLogoResult.FromSlots(appliedSlots, failedSlots, package, bound, warnings);
		} catch (Exception exception) {
			string into = package is null ? string.Empty : $" into package '{package}'";
			string kept = bound.Count == 0
				? string.Empty
				: $" Already bound and left in place: {string.Join(", ", bound)}.";
			return SetLogoResult.FromSlots(appliedSlots, failedSlots, package, bound, warnings,
				error: $"The applied logos could not be bound{into}: {exception.Message}{kept}");
		}
	}

	private static IReadOnlyList<LogoSlot> ResolveSlots(SetLogoOptions options) {
		(string Label, string Code, string File)[] candidates = [
			("login-logo", LoginLogoCode, ResolveSlotFile(options.LoginLogo, options.Logo)),
			("menu-logo", MenuLogoCode, ResolveSlotFile(options.MenuLogo, options.Logo)),
			("configuration-logo", ConfigurationLogoCode, ResolveSlotFile(options.ConfigurationLogo, options.Logo)),
			("dark-logo", DarkLogoCode, ResolveSlotFile(options.DarkLogo, options.Logo))
		];
		return candidates
			.Where(candidate => !string.IsNullOrWhiteSpace(candidate.File))
			.Select(candidate => new LogoSlot(candidate.Label, candidate.Code, candidate.File))
			.ToList();
	}

	private static string ResolveSlotFile(string slotOverride, string allSlotsFile) {
		return string.IsNullOrWhiteSpace(slotOverride) ? allSlotsFile : slotOverride;
	}
}
