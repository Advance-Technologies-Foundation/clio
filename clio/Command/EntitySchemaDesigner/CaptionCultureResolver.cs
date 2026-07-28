using System;
using System.Globalization;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Resolves the effective caption culture for a creation/mutation command in one call, applying the
/// canonical precedence: an explicit <c>--caption-culture</c> override wins; otherwise the connected
/// user's profile culture (read once, cached); otherwise the <c>en-US</c> fallback. The host
/// <c>CultureInfo.CurrentCulture</c> is never read. Profile-resolution failure is non-fatal (M-4):
/// it degrades to <c>en-US</c> so scripted/CI creation keeps working.
/// </summary>
public interface ICaptionCultureResolver {
	/// <summary>
	/// Resolves the effective caption culture for the given environment options and optional override.
	/// </summary>
	/// <param name="options">The command's environment options (used to resolve the target environment).</param>
	/// <param name="overrideCulture">An explicit <c>--caption-culture</c> override, if any.</param>
	/// <returns>A validated .NET culture name (e.g. <c>en-US</c>, <c>uk-UA</c>).</returns>
	string Resolve(EnvironmentOptions options, string overrideCulture);

	/// <summary>
	/// Resolves the effective caption culture for an already-resolved environment. Applies the same
	/// precedence as <see cref="Resolve(EnvironmentOptions, string)"/> but never consults
	/// <see cref="ISettingsRepository"/> — the caller supplies the settings directly (e.g. an MCP
	/// passthrough tenant resolved from request headers).
	/// </summary>
	/// <param name="settings">The already-resolved environment settings; must not be <c>null</c>.</param>
	/// <param name="overrideCulture">An explicit <c>--caption-culture</c> override, if any.</param>
	/// <returns>A validated .NET culture name (e.g. <c>en-US</c>, <c>uk-UA</c>).</returns>
	/// <exception cref="ArgumentNullException"><paramref name="settings"/> is <c>null</c>.</exception>
	string Resolve(EnvironmentSettings settings, string overrideCulture);
}

/// <inheritdoc />
public sealed class CaptionCultureResolver : ICaptionCultureResolver {
	private readonly ICurrentUserCultureResolverFactory _cultureResolverFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;

	/// <summary>Initializes the resolver with the culture-resolver factory, settings repository, and logger.</summary>
	public CaptionCultureResolver(
		ICurrentUserCultureResolverFactory cultureResolverFactory,
		ISettingsRepository settingsRepository,
		ILogger logger) {
		_cultureResolverFactory = cultureResolverFactory ?? throw new ArgumentNullException(nameof(cultureResolverFactory));
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <inheritdoc />
	public string Resolve(EnvironmentOptions options, string overrideCulture) {
		ArgumentNullException.ThrowIfNull(options);
		string overrideResult = NormalizeOverrideCulture(overrideCulture);
		if (overrideResult is not null) {
			return overrideResult;
		}

		try {
			EnvironmentSettings settings = _settingsRepository.GetEnvironment(options);
			return ResolveProfileCulture(settings);
		} catch (Exception ex) {
			return WarnAndFallBack(ex);
		}
	}

	/// <inheritdoc />
	public string Resolve(EnvironmentSettings settings, string overrideCulture) {
		ArgumentNullException.ThrowIfNull(settings);
		string overrideResult = NormalizeOverrideCulture(overrideCulture);
		if (overrideResult is not null) {
			return overrideResult;
		}

		try {
			return ResolveProfileCulture(settings);
		} catch (Exception ex) {
			return WarnAndFallBack(ex);
		}
	}

	private static string NormalizeOverrideCulture(string overrideCulture) {
		if (string.IsNullOrWhiteSpace(overrideCulture)) {
			return null;
		}

		string trimmed = overrideCulture.Trim();
		try {
			return CultureInfo.GetCultureInfo(trimmed).Name;
		} catch (CultureNotFoundException) {
			throw new EntitySchemaDesignerException(
				$"--caption-culture '{trimmed}' is not a valid culture name (e.g. en-US, uk-UA).");
		}
	}

	private string ResolveProfileCulture(EnvironmentSettings settings) {
		CultureResolution resolution = _cultureResolverFactory.Create(settings)
			.ResolveAsync().GetAwaiter().GetResult();
		return resolution.Success ? resolution.Culture : EntitySchemaDesignerSupport.DefaultCultureName;
	}

	private string WarnAndFallBack(Exception ex) {
		_logger.WriteWarning(
			$"Could not resolve the user profile culture; using '{EntitySchemaDesignerSupport.DefaultCultureName}'. {ex.Message}");
		return EntitySchemaDesignerSupport.DefaultCultureName;
	}
}
