using System.Collections.Generic;
using Clio.Common;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the MCP-only <c>check-theming-access</c> operation.
/// </summary>
[RequiresCreatioVersion(ThemeServiceRequirement.MinVersion)]
public class CheckThemingAccessOptions : EnvironmentOptions
{
}

/// <summary>
/// Reports whether the caller may manage custom themes on the target environment by composing two native
/// Creatio checks: the <c>CanManageThemes</c> system operation (<see cref="ICreatioRightsClient"/>) and
/// the <c>CanCustomizeBranding</c> license (<see cref="ICreatioLicenseClient"/>).
/// </summary>
public class CheckThemingAccessCommand : Command<CheckThemingAccessOptions>
{
	private const string CanManageThemesOperation = "CanManageThemes";
	private const string CanCustomizeBrandingLicense = "CanCustomizeBranding";

	private readonly ICreatioRightsClient _rightsClient;
	private readonly ICreatioLicenseClient _licenseClient;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="CheckThemingAccessCommand"/> class.
	/// </summary>
	public CheckThemingAccessCommand(ICreatioRightsClient rightsClient, ICreatioLicenseClient licenseClient,
		ILogger logger) {
		_rightsClient = rightsClient;
		_licenseClient = licenseClient;
		_logger = logger;
	}

	/// <summary>
	/// Probes the <c>CanManageThemes</c> operation right and the <c>CanCustomizeBranding</c> license on
	/// the target environment and returns both verdicts.
	/// </summary>
	public virtual ThemingAccess GetThemingAccess() {
		CreatioRequestOptions requestOptions = new();
		bool canManageThemes = _rightsClient.GetCanExecuteOperation(CanManageThemesOperation, requestOptions);
		IReadOnlyDictionary<string, bool> licenseStatuses = _licenseClient.GetLicenseOperationStatuses(
			new[] { CanCustomizeBrandingLicense }, requestOptions);
		bool canCustomizeBranding = licenseStatuses.TryGetValue(CanCustomizeBrandingLicense, out bool granted)
			&& granted;
		return new ThemingAccess(canManageThemes, canCustomizeBranding);
	}

	/// <inheritdoc />
	public override int Execute(CheckThemingAccessOptions options) {
		ThemingAccess access = GetThemingAccess();
		_logger.WriteInfo(
			$"CanManageThemes: {access.CanManageThemes}; CanCustomizeBranding: {access.CanCustomizeBranding}");
		return 0;
	}
}

/// <summary>
/// The theming access verdicts returned by the <c>check-theming-access</c> operation.
/// </summary>
/// <param name="CanManageThemes">Whether the caller holds the <c>CanManageThemes</c> system operation right.</param>
/// <param name="CanCustomizeBranding">Whether the caller holds the <c>CanCustomizeBranding</c> license.</param>
public sealed record ThemingAccess(bool CanManageThemes, bool CanCustomizeBranding);
