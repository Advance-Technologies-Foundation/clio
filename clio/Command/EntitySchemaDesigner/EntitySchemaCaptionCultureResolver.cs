using System;
using System.Globalization;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Resolves the effective culture used for entity-schema caption/description writes.
/// </summary>
internal interface IEntitySchemaCaptionCultureResolver
{
	/// <summary>
	/// Resolves the effective caption culture: an explicit override wins; otherwise the connected user's
	/// profile culture; otherwise the <c>en-US</c> fallback. Never reads the host
	/// <see cref="CultureInfo.CurrentCulture"/>. Profile-resolution failure is non-fatal — it degrades to
	/// <c>en-US</c> so scripted/CI column writes keep working.
	/// </summary>
	/// <param name="environmentOptions">Options identifying the target environment.</param>
	/// <param name="captionCultureOverride">Optional explicit culture override (e.g. <c>en-US</c>, <c>uk-UA</c>).</param>
	/// <returns>The resolved culture name.</returns>
	string ResolveEffectiveCulture(EnvironmentOptions environmentOptions, string? captionCultureOverride);
}

/// <summary>
/// Default <see cref="IEntitySchemaCaptionCultureResolver"/> implementation. Extracted from
/// <see cref="RemoteEntitySchemaColumnManager"/> so the manager stays within the constructor-parameter
/// budget and the culture-resolution policy lives in one cohesive place.
/// </summary>
internal sealed class EntitySchemaCaptionCultureResolver : IEntitySchemaCaptionCultureResolver
{
	private readonly ICurrentUserCultureResolverFactory _cultureResolverFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;

	public EntitySchemaCaptionCultureResolver(
		ICurrentUserCultureResolverFactory cultureResolverFactory,
		ISettingsRepository settingsRepository,
		ILogger logger) {
		_cultureResolverFactory = cultureResolverFactory;
		_settingsRepository = settingsRepository;
		_logger = logger;
	}

	/// <inheritdoc />
	public string ResolveEffectiveCulture(EnvironmentOptions environmentOptions, string? captionCultureOverride) {
		if (!string.IsNullOrWhiteSpace(captionCultureOverride)) {
			string overrideCulture = captionCultureOverride.Trim();
			try {
				return CultureInfo.GetCultureInfo(overrideCulture).Name;
			} catch (CultureNotFoundException) {
				throw new EntitySchemaDesignerException(
					$"--caption-culture '{overrideCulture}' is not a valid culture name (e.g. en-US, uk-UA).");
			}
		}

		try {
			EnvironmentSettings settings = _settingsRepository.GetEnvironment(environmentOptions);
			CultureResolution resolution = _cultureResolverFactory.Create(settings)
				.ResolveAsync().GetAwaiter().GetResult();
			return resolution.Success ? resolution.Culture : EntitySchemaDesignerSupport.DefaultCultureName;
		} catch (Exception ex) {
			_logger.WriteWarning(
				$"Could not resolve the user profile culture; using '{EntitySchemaDesignerSupport.DefaultCultureName}'. {ex.Message}");
			return EntitySchemaDesignerSupport.DefaultCultureName;
		}
	}
}
