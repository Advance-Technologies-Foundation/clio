using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;
using Clio.Common;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Legacy branch of <c>get-mobile-page-conversion-guide</c> (ENG-95730): a classic Mobile-wizard LIST settings
/// schema (<c>Mobile&lt;Entity&gt;GridPageSettings&lt;Workplace&gt;</c>) is read across every package layer, classified,
/// and analysed into the same guide contract the Freedom UI web branch returns. Advisory-only: nothing is written.
/// </summary>
public sealed partial class MobilePageConversionGuideTool {

	/// <summary>
	/// Builds the guide for a legacy Mobile-wizard list settings source. Reads the effective (package-merged)
	/// settings through <see cref="ILegacyMobileSettingsReader"/>, confirms the body really is a GridPage settings
	/// array, refuses a custom viewConfig, and otherwise returns the analysis with the mechanism recorded.
	/// </summary>
	private MobilePageConversionGuideResponse BuildLegacyGridPageGuide(
		MobilePageConversionGuideArgs args,
		PageGetOptions getOptions,
		string sourceType,
		string version,
		string resolvedFrom,
		PlatformVersionResolution versionResolution) {
		const string mechanism = LegacyMobileListAnalysisService.MechanismLegacySettingsConverter;

		LegacyMobileSettingsReadResult read;
		try {
			ILegacyMobileSettingsReader reader = _commandResolver.Resolve<ILegacyMobileSettingsReader>(getOptions);
			lock (McpToolExecutionLock.GetLock(McpToolExecutionLock.SharedFallbackKey)) {
				try {
					read = reader.Read(args.SchemaName);
				} finally {
					_logger.ClearMessages();
				}
			}
		} catch (Exception ex) {
			return FailLegacy(args, sourceType, mechanism, $"Failed to read legacy mobile settings '{args.SchemaName}': {ex.Message}");
		}
		if (read is null || !read.Success) {
			return FailLegacy(args, sourceType, mechanism,
				$"Could not read legacy mobile settings '{args.SchemaName}': {read?.Error ?? "unknown error"}");
		}

		// Body confirmation: the name said "GridPageSettings", the merged body must agree. Anything else is NOT
		// converted — the generic not-supported verdict applies, with the label the platform actually gave.
		// Scalars are read as JValue so a hand-edited object in their place fails cleanly, never with a cast error.
		string settingsType = ScalarString(read.EffectiveSettings, "settingsType");
		if (!string.Equals(settingsType, LegacyGridPageSettingsParser.GridPageSettingsType, StringComparison.OrdinalIgnoreCase)) {
			return FailLegacy(args, "unknown", mechanism,
				$"Schema '{args.SchemaName}' is named like a legacy Mobile-wizard list settings schema, but its merged body declares settingsType '{settingsType ?? "(none)"}' instead of '{LegacyGridPageSettingsParser.GridPageSettingsType}'. Nothing was converted.");
		}

		LegacySettingsClassification classification;
		try {
			classification = LegacyMobileSettingsClassifier.Classify(read.EffectiveSettings);
		} catch (Exception ex) {
			return FailLegacy(args, sourceType, mechanism, $"Failed to classify legacy mobile settings '{args.SchemaName}': {ex.Message}");
		}
		if (classification.Kind == LegacySettingsKind.CustomViewConfig) {
			return FailLegacy(args, sourceType, mechanism,
				$"Source schema '{args.SchemaName}' carries a custom viewConfig in its legacy mobile settings. It cannot be converted automatically — even the classic Mobile application wizard cannot open such a page. Rebuild the mobile list page by hand from {LegacyMobileListAnalysisService.RecommendedTemplate} (create-page) or remove the custom viewConfig from the settings schema first.");
		}

		string entitySchemaName = ScalarString(read.EffectiveSettings, "entitySchemaName")?.Trim();
		if (string.IsNullOrWhiteSpace(entitySchemaName)) {
			return FailLegacy(args, sourceType, mechanism,
				$"Legacy mobile settings '{args.SchemaName}' do not declare 'entitySchemaName'; the mobile list page cannot be bound to an object. Nothing was converted.");
		}
		string targetName = string.IsNullOrWhiteSpace(args.TargetSchemaName)
			? LegacyMobileListAnalysisService.DeriveTargetSchemaName(entitySchemaName, ReadSchemaNamePrefix(getOptions))
			: args.TargetSchemaName.Trim();

		// Read-only probe: the legacy settings schema is not itself a SysModule page, so the section is found by
		// the entity the wizard page was bound to. Best-effort — never blocks the guide.
		SectionRegistrationInfo sectionRegistration = MobileSectionRegistrationProbe.ProbeByEntity(
			_commandResolver, args.EnvironmentName, args.Uri, args.Login, args.Password, entitySchemaName);

		MobilePageConversionGuide guide;
		try {
			guide = LegacyMobileListAnalysisService.Analyze(read, classification, args.SchemaName, targetName, sectionRegistration);
		} catch (Exception ex) {
			return FailLegacy(args, sourceType, mechanism, $"Failed to analyze legacy mobile settings '{args.SchemaName}': {ex.Message}");
		}

		return new MobilePageConversionGuideResponse {
			Success = true,
			SourceSchemaName = args.SchemaName,
			SourceType = sourceType,
			ConversionMechanism = mechanism,
			Guide = guide,
			ResolvedTargetVersion = version,
			ResolvedFrom = resolvedFrom,
			VersionWarning = ComponentInfoResolution.GetVersionWarning(resolvedFrom),
			RequiresVersionConfirmation = ComponentInfoResolution.RequiresVersionConfirmation(resolvedFrom),
			ResolvedFromReason = ComponentInfoResolution.GetFallbackReason(resolvedFrom, versionResolution.Reason)
		};
	}

	/// <summary>
	/// Best-effort read of the environment's <c>SchemaNamePrefix</c> system setting for the default target name.
	/// A blank setting AND an unreadable setting both degrade to
	/// <see cref="LegacyMobileListAnalysisService.DefaultSchemaNamePrefix"/>, so two environments with the same
	/// settings never produce different default names depending on whether the read succeeded (an explicit
	/// <c>target-schema-name</c> always wins anyway).
	/// </summary>
	private string ReadSchemaNamePrefix(PageGetOptions getOptions) {
		try {
			ISysSettingsManager sysSettingsManager = _commandResolver.Resolve<ISysSettingsManager>(getOptions);
			string prefix = SysSettingCodes.ReadSchemaNamePrefix(sysSettingsManager);
			return string.IsNullOrWhiteSpace(prefix) ? LegacyMobileListAnalysisService.DefaultSchemaNamePrefix : prefix;
		} catch (Exception) {
			return LegacyMobileListAnalysisService.DefaultSchemaNamePrefix;
		} finally {
			_logger.ClearMessages();
		}
	}

	/// <summary>
	/// Resolves the version tier for the response contract WITHOUT loading any component registry (the legacy
	/// guide needs none) and without letting a version-probe failure escape: any failure degrades to the
	/// <c>latest-fallback</c> tier the caller already knows how to confirm.
	/// </summary>
	private async Task<(string Version, string ResolvedFrom, PlatformVersionResolution Resolution)> ResolveVersionTierBestEffortAsync(
		MobilePageConversionGuideArgs args, CancellationToken cancellationToken) {
		PlatformVersionResolution resolution;
		try {
			resolution = await ResolveVersionAsync(args, cancellationToken).ConfigureAwait(false);
		} catch (Exception) {
			resolution = ComponentInfoResolution.CreateNoActiveEnvironmentFallback();
		} finally {
			_logger.ClearMessages();
		}
		string resolvedFrom = ComponentInfoResolution.MapResolvedFrom(resolution.Source, resolution.ResolvedVersion, resolution.ResolvedVersion);
		return (resolution.ResolvedVersion, resolvedFrom, resolution);
	}

	/// <summary>Reads a top-level scalar as text; an object/array/null in its place yields null instead of a cast error.</summary>
	private static string ScalarString(Newtonsoft.Json.Linq.JObject settings, string key) =>
		settings?[key] is Newtonsoft.Json.Linq.JValue { Type: not (Newtonsoft.Json.Linq.JTokenType.Null or Newtonsoft.Json.Linq.JTokenType.Undefined) } value
			? value.ToString()
			: null;

	private static MobilePageConversionGuideResponse FailLegacy(
		MobilePageConversionGuideArgs args, string sourceType, string mechanism, string error) =>
		new() {
			Success = false,
			SourceSchemaName = args?.SchemaName,
			SourceType = sourceType,
			ConversionMechanism = mechanism,
			Error = error
		};
}
