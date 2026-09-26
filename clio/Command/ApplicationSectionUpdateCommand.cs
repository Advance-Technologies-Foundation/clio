using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.Localization;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for updating metadata of an existing installed application section.
/// </summary>
[Verb("update-app-section", HelpText = "Update metadata of a section inside an existing installed application")]
public sealed class UpdateAppSectionOptions : EnvironmentOptions {
	[Option("application-code", Required = true, HelpText = "Installed application code")]
	public string ApplicationCode { get; set; } = string.Empty;

	[Option("section-code", Required = true, HelpText = "Section code inside the installed application")]
	public string SectionCode { get; set; } = string.Empty;

	[Option("caption", Required = false, HelpText = "Updated section caption")]
	public string? Caption { get; set; }

	[Option("description", Required = false, HelpText = "Updated section description")]
	public string? Description { get; set; }

	[Option("icon-id", Required = false, HelpText = "Updated section icon GUID")]
	public string? IconId { get; set; }

	[Option("icon-background", Required = false, HelpText = "Updated section icon background in #RRGGBB format")]
	public string? IconBackground { get; set; }

	[Option("caption-culture", Required = false, HelpText = "Culture the --caption is written in (e.g. es-ES, de-DE). Precedence: this value > the connected user's profile culture > en-US. Other cultures of the section title are kept. The culture must exist in the Languages section. Requires --caption.")]
	public string? CaptionCulture { get; set; }
}

/// <summary>
/// Updates metadata of an existing installed application section.
/// </summary>
public interface IApplicationSectionUpdateService {
	/// <summary>
	/// Updates metadata of an existing installed application section in the specified environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Section update request payload.</param>
	/// <returns>Structured application and section metadata before and after the update.</returns>
	/// <remarks>
	/// The platform deletes every non-default culture of the section title and description on each
	/// <c>ApplicationSection</c> update. The implementation snapshots those values before the update and writes
	/// them back afterwards, writes a caption in a non-profile culture through the localization table only, and
	/// re-saves the application package's <c>SysModule_&lt;SectionCode&gt;</c> binding after such a write.
	/// A supplied <see cref="ApplicationSectionUpdateRequest.CaptionCulture"/> is looked up in the environment's
	/// <c>SysCulture</c> rows before any write, because the platform drops a value in an unknown culture and still
	/// reports success.
	/// </remarks>
	ApplicationSectionUpdateResult UpdateSection(
		string environmentName,
		ApplicationSectionUpdateRequest request);

	/// <summary>
	/// Updates metadata of an existing installed application section against an already-resolved
	/// environment. Behaves identically to <see cref="UpdateSection(string, ApplicationSectionUpdateRequest)"/>
	/// except it never consults <see cref="Clio.UserEnvironment.ISettingsRepository"/> — the caller
	/// supplies the settings directly (e.g. an MCP passthrough tenant resolved from request headers) —
	/// and every nested call it makes (the profile-culture resolution and the application-info read)
	/// uses the settings-based overloads of <see cref="Clio.Command.EntitySchemaDesigner.ICaptionCultureResolver"/>
	/// and <see cref="IApplicationInfoService"/>, never the name-based ones (ENG-93347, ADR OQ-01 c1 rule).
	/// </summary>
	/// <param name="environmentSettings">The already-resolved environment settings; must not be <c>null</c>.</param>
	/// <param name="request">Section update request payload.</param>
	/// <returns>Structured application and section metadata before and after the update.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="environmentSettings"/> is <c>null</c>.</exception>
	ApplicationSectionUpdateResult UpdateSection(
		EnvironmentSettings environmentSettings,
		ApplicationSectionUpdateRequest request);
}

/// <summary>
/// Default ApplicationSection DataService-backed implementation for existing-app section updates.
/// </summary>
public sealed class ApplicationSectionUpdateService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	IApplicationInfoService applicationInfoService,
	ICaptionCultureResolver captionCultureResolver,
	IApplicationSectionLocalizationClient sectionLocalizationClient,
	ISectionLocalizationPlanner localizationPlanner,
	ICreatioCultureCatalogFactory cultureCatalogFactory)
	: IApplicationSectionUpdateService {
	private const string ApplicationSectionSchemaName = "ApplicationSection";
	private const string ApplicationIdField = "ApplicationId";
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ApplicationSectionUpdateResult UpdateSection(
		string environmentName,
		ApplicationSectionUpdateRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		ValidateRequest(request);
		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));
		if (string.IsNullOrWhiteSpace(environmentSettings.Uri)) {
			throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));
		}

		// The name-based path keeps its pre-change nested calls byte-for-byte (name-based culture
		// resolution and name-based application-info read) so stdio / registered-environment behavior
		// is untouched (ENG-93347 AC-06).
		EnvironmentOptions cultureOptions = new() { Environment = environmentName };
		return UpdateSectionCore(
			environmentSettings,
			request,
			() => captionCultureResolver.Resolve(cultureOptions, null),
			() => applicationInfoService.GetApplicationInfo(environmentName, null, request.ApplicationCode));
	}

	/// <inheritdoc />
	public ApplicationSectionUpdateResult UpdateSection(
		EnvironmentSettings environmentSettings,
		ApplicationSectionUpdateRequest request) {
		ArgumentNullException.ThrowIfNull(environmentSettings);
		ArgumentNullException.ThrowIfNull(request);
		ValidateRequest(request);

		// ADR OQ-01 (c1) rule: a settings-based overload never calls a name-based overload or
		// ISettingsRepository — both nested calls (the profile-culture resolution and the
		// application-info read) go through the Story-2 settings-based overloads.
		return UpdateSectionCore(
			environmentSettings,
			request,
			() => captionCultureResolver.Resolve(environmentSettings, null),
			() => applicationInfoService.GetApplicationInfo(environmentSettings, null, request.ApplicationCode));
	}

	private ApplicationSectionUpdateResult UpdateSectionCore(
		EnvironmentSettings environmentSettings,
		ApplicationSectionUpdateRequest request,
		Func<string> resolveProfileCulture,
		Func<ApplicationInfoResult> loadApplicationInfo) {
		List<string> warnings = [];
		string profileCulture = resolveProfileCulture();
		// ENG-91044: the description always goes through the ApplicationSection update, which stores it under the
		// connected user's profile culture.
		CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(profileCulture, request.Description, "description");

		using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
		string targetCulture = ResolveTargetCulture(request, client, environmentSettings, profileCulture, warnings);
		bool captionThroughSection = request.Caption is not null && SameCulture(targetCulture, profileCulture);
		bool captionThroughLocalization = request.Caption is not null && !captionThroughSection;
		// ENG-91044: the caption is stored under the target culture (profile culture unless caption-culture says
		// otherwise), so its text must match that culture.
		CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(targetCulture, request.Caption, "caption");

		ApplicationInfoResult applicationInfo = loadApplicationInfo();
		string applicationId = applicationInfo.ApplicationId
			?? throw new InvalidOperationException("Application id was not returned by get-app-info.");
		ApplicationSectionRecord previousSection = GetSectionRecord(
			client,
			environmentSettings,
			applicationId,
			request.SectionCode);
		// ENG-90576 D11 (F11): the platform's ApplicationSection update deletes every non-default culture of
		// the section title and description. Snapshot them BEFORE any write so they can be written back.
		IReadOnlyList<SectionLocalizationRow> snapshot = sectionLocalizationClient.ReadLocalizations(
			client, environmentSettings, previousSection.Id);
		ResolvedApplicationSectionUpdateRequest resolvedRequest = ResolveRequest(request, captionThroughSection);
		bool sectionUpdateNeeded = resolvedRequest.ShouldUpdateCaption
			|| resolvedRequest.ShouldUpdateDescription
			|| resolvedRequest.ShouldUpdateIconId
			|| resolvedRequest.ShouldUpdateIconBackground;
		if (sectionUpdateNeeded) {
			ExecuteSectionUpdate(client, environmentSettings, previousSection, resolvedRequest);
		}

		// A non-default profile culture has its own localization row; prefer it over the SelectQuery value, which
		// falls back to the default culture when that row is missing.
		string currentProfileCaption = captionThroughSection
			? resolvedRequest.Caption!
			: SectionLocalizationPlanner.ReadCell(snapshot, SectionLocalizationPlanner.CaptionColumn, profileCulture)
				?? previousSection.Caption ?? string.Empty;
		SectionLocalizationPlan plan = localizationPlanner.BuildPlan(new SectionLocalizationPlanInput(
			snapshot,
			sectionUpdateNeeded,
			profileCulture,
			targetCulture,
			captionThroughLocalization ? request.Caption!.Trim() : null,
			captionThroughSection,
			currentProfileCaption,
			resolvedRequest.ShouldUpdateDescription));
		localizationPlanner.Apply(
			client, environmentSettings, previousSection.Id, applicationInfo.PackageUId, previousSection.Code, plan, warnings);

		ApplicationSectionRecord updatedSection = GetSectionRecord(
			client,
			environmentSettings,
			applicationId,
			request.SectionCode);
		IReadOnlyList<SectionLocalizationRow> storedLocalizations = plan.HasWrites || sectionUpdateNeeded
			? sectionLocalizationClient.ReadLocalizations(client, environmentSettings, previousSection.Id)
			: snapshot;
		localizationPlanner.Verify(plan, storedLocalizations);
		string? captionCultureValue = ResolveCaptionCultureValue(
			request,
			captionThroughSection,
			targetCulture,
			profileCulture,
			updatedSection,
			storedLocalizations,
			warnings);
		return new ApplicationSectionUpdateResult(
			applicationInfo.PackageUId,
			applicationInfo.PackageName,
			applicationId,
			applicationInfo.ApplicationName ?? string.Empty,
			applicationInfo.ApplicationCode ?? request.ApplicationCode,
			applicationInfo.ApplicationVersion,
			MapSection(previousSection),
			MapSection(updatedSection),
			request.Caption is not null ? targetCulture : null,
			captionCultureValue,
			plan.PreservedCultures,
			warnings);
	}

	private string ResolveTargetCulture(
		ApplicationSectionUpdateRequest request,
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string profileCulture,
		List<string> warnings) {
		if (string.IsNullOrWhiteSpace(request.CaptionCulture)) {
			return profileCulture;
		}

		// The environment's SysCulture rows are the only source of truth for an explicit culture, as in
		// localize-page: a .NET culture check would reject a row .NET does not know and would hide the Languages
		// message. The platform drops a value in a culture that is not a SysCulture row and still answers success.
		string requestedCulture = request.CaptionCulture.Trim();
		CultureLookupResult lookup = cultureCatalogFactory.Create(client, environmentSettings).Find(requestedCulture);
		if (!lookup.Found) {
			throw new InvalidOperationException(CultureMessages.FormatCultureAbsent(requestedCulture, lookup.Available));
		}

		if (!lookup.Culture.Active) {
			warnings.Add(CultureMessages.FormatCultureInactive(lookup.Culture.Name));
		}

		return lookup.Culture.Name;
	}

	private void ExecuteSectionUpdate(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		ApplicationSectionRecord previousSection,
		ResolvedApplicationSectionUpdateRequest resolvedRequest) {
		string requestBody = JsonSerializer.Serialize(BuildUpdateBody(previousSection, resolvedRequest), JsonOptions);
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update, environmentSettings),
			requestBody);
		UpdateQueryResponseDto response = JsonSerializer.Deserialize<UpdateQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("UpdateQuery returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "UpdateQuery failed.");
		}
	}

	private static string? ResolveCaptionCultureValue(
		ApplicationSectionUpdateRequest request,
		bool captionThroughSection,
		string targetCulture,
		string profileCulture,
		ApplicationSectionRecord updatedSection,
		IReadOnlyList<SectionLocalizationRow> storedLocalizations,
		List<string> warnings) {
		if (request.Caption is null) {
			return null;
		}

		if (captionThroughSection) {
			return updatedSection.Caption;
		}

		string? stored = SectionLocalizationPlanner.ReadCell(
			storedLocalizations, SectionLocalizationPlanner.CaptionColumn, targetCulture);
		if (stored is null && !SameCulture(targetCulture, profileCulture)
			&& SameCulture(targetCulture, EntitySchemaDesignerSupport.DefaultCultureName)) {
			// The default culture is stored in SysModule itself, not in the localization table, and the
			// SelectQuery readback returns the connected user's culture — so it cannot be read back here.
			warnings.Add(
				$"The caption in '{targetCulture}' is stored in the section record itself and could not be read back " +
				"under the current profile culture; verify it in Creatio.");
			return request.Caption.Trim();
		}

		return stored;
	}

	private static bool SameCulture(string left, string right) =>
		string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	private static void ValidateRequest(ApplicationSectionUpdateRequest request) {
		if (string.IsNullOrWhiteSpace(request.ApplicationCode)) {
			throw new ArgumentException("application-code is required.");
		}

		if (string.IsNullOrWhiteSpace(request.SectionCode)) {
			throw new ArgumentException("section-code is required.");
		}

		bool hasCaption = request.Caption is not null;
		bool hasDescription = request.Description is not null;
		bool hasIconId = request.IconId is not null;
		bool hasIconBackground = request.IconBackground is not null;
		if (!hasCaption && !hasDescription && !hasIconId && !hasIconBackground) {
			throw new ArgumentException("At least one mutable field is required: caption, description, icon-id, or icon-background.");
		}

		if (hasCaption && string.IsNullOrWhiteSpace(request.Caption)) {
			throw new ArgumentException("caption cannot be empty.");
		}

		if (!hasCaption && !string.IsNullOrWhiteSpace(request.CaptionCulture)) {
			throw new ArgumentException("caption-culture requires caption.");
		}

		if (hasDescription && string.IsNullOrWhiteSpace(request.Description)) {
			throw new ArgumentException("description cannot be empty.");
		}

		if (hasIconId && (!Guid.TryParse(request.IconId, out _))) {
			throw new ArgumentException("icon-id must be a valid GUID.");
		}

		if (hasIconBackground) {
			ApplicationSectionColorPalette.ValidateOrThrow(request.IconBackground!);
		}
	}

	private static ResolvedApplicationSectionUpdateRequest ResolveRequest(
		ApplicationSectionUpdateRequest request,
		bool captionThroughSection) {
		return new ResolvedApplicationSectionUpdateRequest(
			captionThroughSection ? request.Caption?.Trim() : null,
			request.Description?.Trim(),
			request.IconId?.Trim(),
			request.IconBackground?.Trim(),
			captionThroughSection,
			request.Description is not null,
			request.IconId is not null,
			request.IconBackground is not null);
	}

	private ApplicationSectionRecord GetSectionRecord(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string applicationId,
		string sectionCode) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select, environmentSettings),
			JsonSerializer.Serialize(BuildSectionSelectQuery(applicationId), JsonOptions));
		ApplicationSectionSelectQueryResponseDto response = JsonSerializer.Deserialize<ApplicationSectionSelectQueryResponseDto>(
				responseBody,
				JsonOptions)
			?? throw new InvalidOperationException("ApplicationSection select query returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "ApplicationSection select query failed.");
		}

		return response.Rows
				.FirstOrDefault(row => string.Equals(row.Code, sectionCode, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException(
				$"Section '{sectionCode}' was not found in application '{applicationId}'.");
	}

	private static object BuildSectionSelectQuery(string applicationId) =>
		SelectQueryHelper.BuildSelectQuery(
			ApplicationSectionSchemaName,
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
				new SelectQueryHelper.SelectQueryColumnDefinition(ApplicationIdField, ApplicationIdField),
				new SelectQueryHelper.SelectQueryColumnDefinition("Caption", "Caption"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Code", "Code"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Description", "Description"),
				new SelectQueryHelper.SelectQueryColumnDefinition("EntitySchemaName", "EntitySchemaName"),
				new SelectQueryHelper.SelectQueryColumnDefinition("PackageId", "PackageId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("SectionSchemaUId", "SectionSchemaUId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("LogoId", "LogoId"),
				new SelectQueryHelper.SelectQueryColumnDefinition("IconBackground", "IconBackground"),
				new SelectQueryHelper.SelectQueryColumnDefinition("ClientTypeId", "ClientTypeId")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					ApplicationIdField,
					applicationId,
					SelectQueryHelper.GuidDataValueType)
			]);

	private static object BuildUpdateBody(ApplicationSectionRecord previousSection, ResolvedApplicationSectionUpdateRequest request) {
		Dictionary<string, object> items = new(StringComparer.Ordinal) {
			["Id"] = SelectQueryHelper.BuildParameterExpression(SelectQueryHelper.GuidDataValueType, previousSection.Id),
			[ApplicationIdField] = SelectQueryHelper.BuildParameterExpression(SelectQueryHelper.GuidDataValueType, previousSection.ApplicationId),
			["LogoId"] = SelectQueryHelper.BuildParameterExpression(SelectQueryHelper.GuidDataValueType,
				request.ShouldUpdateIconId && request.IconId is not null ? request.IconId : previousSection.LogoId ?? string.Empty),
			["PackageId"] = SelectQueryHelper.BuildParameterExpression(SelectQueryHelper.GuidDataValueType, previousSection.PackageId ?? string.Empty),
			["IconBackground"] = SelectQueryHelper.BuildParameterExpression(SelectQueryHelper.TextDataValueType,
				request.ShouldUpdateIconBackground && request.IconBackground is not null ? request.IconBackground : previousSection.IconBackground ?? string.Empty)
		};
		if (request.ShouldUpdateCaption && request.Caption is not null) {
			items["Caption"] = SelectQueryHelper.BuildParameterExpression(SelectQueryHelper.TextDataValueType, request.Caption);
		}

		if (request.ShouldUpdateDescription && request.Description is not null) {
			items["Description"] = SelectQueryHelper.BuildParameterExpression(SelectQueryHelper.TextDataValueType, request.Description);
		}

		return new Dictionary<string, object> {
			["__type"] = "Terrasoft.Nui.ServiceModel.DataContract.UpdateQuery",
			["operationType"] = 2,
			["rootSchemaName"] = ApplicationSectionSchemaName,
			["isForceUpdate"] = false,
			["columnValues"] = new {
				items
			},
			["filters"] = SelectQueryHelper.BuildIdFilter(previousSection.Id, SelectQueryHelper.TextDataValueType)
		};
	}

	private static ApplicationSectionInfoResult MapSection(ApplicationSectionRecord record) =>
		new(
			record.Id,
			record.Code,
			record.Caption ?? string.Empty,
			record.Description,
			record.EntitySchemaName,
			record.PackageId,
			record.SectionSchemaUId,
			record.LogoId,
			record.IconBackground,
			record.ClientTypeId);

	private sealed record ResolvedApplicationSectionUpdateRequest(
		string? Caption,
		string? Description,
		string? IconId,
		string? IconBackground,
		bool ShouldUpdateCaption,
		bool ShouldUpdateDescription,
		bool ShouldUpdateIconId,
		bool ShouldUpdateIconBackground);

	private sealed class ApplicationSectionSelectQueryResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<ApplicationSectionRecord> Rows { get; set; } = [];
	}

	private sealed class UpdateQueryResponseDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }
	}

	private sealed class ErrorInfoDto {
		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}
}

/// <summary>
/// Updates metadata of an existing installed application section and prints the structured readback payload.
/// </summary>
public sealed class UpdateAppSectionCommand(
	IApplicationSectionUpdateService applicationSectionUpdateService,
	ILogger logger)
	: Command<UpdateAppSectionOptions> {
	/// <inheritdoc />
	public override int Execute(UpdateAppSectionOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			ApplicationSectionUpdateResult result = applicationSectionUpdateService.UpdateSection(
				options.Environment,
				new ApplicationSectionUpdateRequest(
					options.ApplicationCode,
					options.SectionCode,
					options.Caption,
					options.Description,
					options.IconId,
					options.IconBackground,
					options.CaptionCulture));
			foreach (string warning in result.Warnings ?? []) {
				logger.WriteWarning(warning);
			}

			logger.WriteInfo(JsonSerializer.Serialize(result));
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Request payload for existing-app section updates.
/// </summary>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="SectionCode">Section code inside the installed application.</param>
/// <param name="Caption">Updated section caption.</param>
/// <param name="Description">Updated section description.</param>
/// <param name="IconId">Updated icon identifier.</param>
/// <param name="IconBackground">Updated icon background color.</param>
/// <param name="CaptionCulture">Culture <paramref name="Caption"/> is written in; <see langword="null"/> means the
/// connected user's profile culture. Other cultures of the section title are kept.</param>
public sealed record ApplicationSectionUpdateRequest(
	string ApplicationCode,
	string SectionCode,
	string? Caption = null,
	string? Description = null,
	string? IconId = null,
	string? IconBackground = null,
	string? CaptionCulture = null);

/// <summary>
/// Structured result for existing-app section updates.
/// </summary>
/// <param name="PackageUId">Primary package identifier.</param>
/// <param name="PackageName">Primary package name.</param>
/// <param name="ApplicationId">Installed application identifier.</param>
/// <param name="ApplicationName">Installed application display name.</param>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="ApplicationVersion">Installed application version.</param>
/// <param name="PreviousSection">Section metadata before the update.</param>
/// <param name="Section">Section metadata after the update (caption in the connected user's profile culture).</param>
/// <param name="CaptionCulture">Culture the caption was written in; <see langword="null"/> when no caption was sent.</param>
/// <param name="CaptionCultureValue">The stored caption in <paramref name="CaptionCulture"/>.</param>
/// <param name="PreservedCultures">Non-default cultures whose other stored section values (title, description) were
/// kept or written back; can include <paramref name="CaptionCulture"/> when its description was kept.</param>
/// <param name="Warnings">Non-fatal findings: inactive culture, stale package data binding.</param>
public sealed record ApplicationSectionUpdateResult(
	string PackageUId,
	string PackageName,
	string ApplicationId,
	string ApplicationName,
	string ApplicationCode,
	string? ApplicationVersion,
	ApplicationSectionInfoResult PreviousSection,
	ApplicationSectionInfoResult Section,
	string? CaptionCulture = null,
	string? CaptionCultureValue = null,
	IReadOnlyList<string>? PreservedCultures = null,
	IReadOnlyList<string>? Warnings = null);
