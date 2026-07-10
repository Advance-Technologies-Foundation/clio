using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;
	
/// <summary>
/// CLI options for creating a section inside an existing installed application.
/// </summary>
[Verb("create-app-section", HelpText = "Create a section inside an existing installed application")]
public sealed class CreateAppSectionOptions : EnvironmentOptions {
	[Option("application-code", Required = true, HelpText = "Installed application code")]
	public string ApplicationCode { get; set; } = string.Empty;

	[Option("caption", Required = true, HelpText = "Section caption")]
	public string Caption { get; set; } = string.Empty;

	[Option("description", Required = false, HelpText = "Section description")]
	public string? Description { get; set; }

	[Option("entity-schema-name", Required = false, HelpText = "Existing entity schema name")]
	public string? EntitySchemaName { get; set; }

	[Option("code", Required = false, HelpText = "Explicit section code (Latin identifier). When omitted, the code is generated from the caption; required when the caption has no Latin letters or digits (for example a non-Latin caption such as \"Контакти\").")]
	public string? Code { get; set; }

	[Option("icon-background", Required = false, HelpText = "Icon background color in #RRGGBB format, e.g. #1F5F8B. Defaults to a random color when omitted.")]
	public string? IconBackground { get; set; }

	[Option("with-mobile-pages", Required = false, Default = "true", HelpText = "Create mobile pages in addition to web pages (default: true)")]
	public string? WithMobilePagesValue { get; set; }

	[Option("caption-culture", Required = false, HelpText = "Override the culture used when displaying the created section caption (e.g. en-US, uk-UA). Precedence: this override > the connected user's profile culture > en-US. The stored section caption itself is localized server-side under the connected user's profile.")]
	public string? CaptionCulture { get; set; }

	public bool WithMobilePages {
		get => string.Equals(WithMobilePagesValue ?? "true", "true", StringComparison.OrdinalIgnoreCase);
		set => WithMobilePagesValue = value ? "true" : "false";
	}

	internal static void ValidateMobilePagesOption(string? value) {
		if (value is null) return;
		if (!string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Invalid value '{value}' for --with-mobile-pages. Allowed values: true, false.");
		}
	}
}

/// <summary>
/// Creates a section inside an existing installed application and returns structured readback data.
/// </summary>
public interface IApplicationSectionCreateService {
	/// <summary>
	/// Creates a section inside an existing installed application in the specified environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Section creation request payload.</param>
	/// <param name="insertTimeoutMsOverride">
	/// Optional override (in milliseconds) for the section-insert budget. When the call runs behind
	/// an MCP response deadline that continues the work in the background, the caller passes a
	/// generous budget so the insert is not aborted at the default 90 s and the section actually
	/// commits server-side (ENG-91316). When <see langword="null"/> the budget falls back to
	/// <c>CLIO_CREATE_SECTION_TIMEOUT_SECONDS</c> or the 90 s default.
	/// </param>
	/// <param name="readbackTimeoutMsOverride">
	/// Optional per-request override (in milliseconds) for the post-insert success readback. When the
	/// call runs behind an MCP response deadline that continues the work in the background, the caller
	/// passes a finite budget so a wedged readback (one Creatio accepts but never answers) cannot hold a
	/// thread-pool worker and HTTP connection for the life of the long-lived server process (ENG-91316).
	/// When <see langword="null"/> (the synchronous CLI path) the readback keeps its
	/// <see cref="Timeout.Infinite"/> default; non-positive values fall through to that default too.
	/// </param>
	/// <param name="reportStage">
	/// Optional callback invoked with a short human-readable marker at each internal stage boundary
	/// (load application info, create section, load created section). Additive and side-effect-free;
	/// when <see langword="null"/> no markers are emitted, so CLI callers are unaffected.
	/// </param>
	/// <returns>Structured data for the created section, entity, and pages.</returns>
	ApplicationSectionCreateResult CreateSection(string environmentName, ApplicationSectionCreateRequest request,
		int? insertTimeoutMsOverride = null, int? readbackTimeoutMsOverride = null,
		Action<string>? reportStage = null);
}

/// <summary>
/// Default ApplicationSection DataService-backed implementation for existing-app section creation.
/// </summary>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
	Justification = "Service composes its required collaborators (settings, client factory, URL builders, app info, sys-settings factory, logger, culture resolver) via constructor injection; splitting them would add an artificial parameter object without behavioral benefit.")]
public sealed class ApplicationSectionCreateService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	IServiceUrlBuilderFactory serviceUrlBuilderFactory,
	IApplicationInfoService applicationInfoService,
	Func<EnvironmentSettings, ISysSettingsManager> sysSettingsManagerFactory,
	ILogger logger,
	ICaptionCultureResolver captionCultureResolver)
	: IApplicationSectionCreateService {
	private const string ApplicationSectionSchemaName = "ApplicationSection";
	private const string ApplicationIdJsonField = "ApplicationId";
	private const string LogoIdField = "LogoId";
	private const string PackageIdField = "PackageId";
	private const string WebClientTypeId = "195785B4-F55A-4E72-ACE3-6480B54C8FA5";
	private const string SelectQueryRoute = "DataService/json/SyncReply/SelectQuery";
	private const int SectionTypeNormal = 0;
	private const int PollAttempts = 15;

	/// <summary>
	/// Environment variable that overrides the ApplicationSection insert budget, in whole seconds.
	/// Non-numeric or non-positive values fall back to the 90-second default (ENG-91540).
	/// </summary>
	internal const string InsertTimeoutEnvironmentVariable = "CLIO_CREATE_SECTION_TIMEOUT_SECONDS";

	// The budget must fire BEFORE the MCP client's hard request ceiling so clio returns its
	// structured creatio-timeout envelope (error-class / section-created / retry-guidance) instead
	// of letting the client abandon the call with an opaque "-32001 Request timed out" (ENG-91540).
	// The progress heartbeat (ENG-91274) does not rescue this: clients such as GitHub Copilot CLI
	// enforce a fixed ~180 s per-request ceiling that progress notifications do not reset (and some
	// clients never send a progressToken, so no beat is emitted at all). These budgets bound the insert
	// call (90 s) and the post-timeout recovery readback (30 s) — the dominant slow span on the
	// not-visible timeout path that is the actual repro — so clio answers well under the observed 180 s
	// ceiling there. They do NOT bound the end-to-end response: the preparation reads before the insert
	// and the 15-attempt poll loop have no cumulative deadline. The background/MCP path additionally
	// bounds each success-path readback HTTP call (readbackTimeoutMsOverride) so a wedged readback cannot
	// hold a thread + connection for the life of the long-lived server process (ENG-91316); the
	// synchronous CLI path keeps the patient Timeout.Infinite readback default.
	// CLIO_CREATE_SECTION_TIMEOUT_SECONDS still lets patient clients / large stands extend the insert budget.
	private const int DefaultInsertTimeoutMs = 90_000;
	private const int VerificationTimeoutMs = 30_000;

	private const string TransportRetryGuidance =
		"The request never reached Creatio, so no section was created and retrying is safe. "
		+ "Verify the environment URL and connectivity first (clio ping -e <env> / clio get-info -e <env>), "
		+ "then retry create-app-section.";

	private const string ServerErrorRetryGuidance =
		"Creatio rejected the operation, so retrying with the same arguments will most likely fail again. "
		+ "Inspect the error, fix the inputs or the server state, and use list-app-sections to inspect "
		+ "existing sections before retrying.";

	private const string TimeoutNotCreatedRetryGuidance =
		"Do not retry immediately: Creatio may still be processing the insert, and a retry can create a "
		+ "duplicate section or fail with an 'already bound' error. Wait a few minutes, then run "
		+ "list-app-sections; if the section appeared, the operation completed despite the timeout. "
		+ "Retry only if the section is still absent and the environment is healthy (clio healthcheck -e <env>). "
		+ "To extend the budget, set the CLIO_CREATE_SECTION_TIMEOUT_SECONDS environment variable.";

	private const string TimeoutUnknownRetryGuidance =
		"Do not retry blindly: the post-timeout verification readback also failed, so the section may or may "
		+ "not have been created. Check environment health (clio healthcheck -e <env>), wait a few minutes, "
		+ "then run list-app-sections to verify the section state before any retry.";

	private const string PreparationRetryGuidance =
		"No section insert was attempted, so no section was created and retrying is safe once the underlying "
		+ "issue is resolved. Verify the environment first (clio ping -e <env> / clio healthcheck -e <env>), "
		+ "then retry create-app-section.";

	private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);
	private static readonly Regex CodeWordRegex = new(
		@"[^\p{L}\p{Nd}]+",
		RegexOptions.None,
		TimeSpan.FromSeconds(5));
	private static readonly Regex SectionCodeRegex = new(
		@"^[A-Za-z][A-Za-z0-9_]*$",
		RegexOptions.None,
		TimeSpan.FromSeconds(5));
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ApplicationSectionCreateResult CreateSection(string environmentName, ApplicationSectionCreateRequest request,
		int? insertTimeoutMsOverride = null, int? readbackTimeoutMsOverride = null,
		Action<string>? reportStage = null) {
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

		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		// The stored section caption is localized server-side under the connected user's profile.
		// This effective culture only drives which value the readback surfaces (override > profile > en-US).
		EnvironmentOptions cultureOptions = new() { Environment = environmentName };
		string effectiveCultureName = captionCultureResolver.Resolve(cultureOptions, request.CaptionCulture);
		// ENG-91044: the stored section caption is localized server-side under the connected user's
		// PROFILE culture — the caption-culture override only selects which value the readback surfaces,
		// not the stored language. Validate the written text against the profile culture (override =
		// null), so a non-matching --caption-culture cannot smuggle the wrong language past the guard
		// (e.g. Cyrillic text stored under an 'en-US' profile).
		string profileCultureForCaption = captionCultureResolver.Resolve(cultureOptions, null);
		CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(profileCultureForCaption, request.Caption, "caption");
		CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(profileCultureForCaption, request.Description, "description");
		ISysSettingsManager sysSettingsManager = sysSettingsManagerFactory(environmentSettings);
		ApplicationInfoResult beforeInfo;
		ResolvedApplicationSectionCreateRequest resolvedRequest;
		string requestBody;
		try {
			string schemaNamePrefix = SysSettingCodes.ReadSchemaNamePrefix(sysSettingsManager);
			logger.WriteInfo($"Loading application info for '{request.ApplicationCode}'...");
			reportStage?.Invoke("loading application info");
			beforeInfo = applicationInfoService.GetApplicationInfo(
				environmentName,
				null,
				request.ApplicationCode);
			resolvedRequest = ResolveRequest(
				request,
				beforeInfo,
				client,
				environmentSettings,
				schemaNamePrefix);
			requestBody = JsonSerializer.Serialize(BuildInsertBody(resolvedRequest), JsonOptions);
			if (string.IsNullOrWhiteSpace(resolvedRequest.EntitySchemaName)) {
				CheckEntitySchemaDoesNotExist(client, environmentSettings, resolvedRequest.SectionCode, request.Caption);
			} else {
				CheckEntitySchemaExists(client, environmentSettings, resolvedRequest.EntitySchemaName, request.Caption);
			}
		} catch (Exception exception) {
			// Preparation reads happen before the destructive insert, so any classified failure here
			// is guaranteed side-effect-free and safe to retry.
			ApplicationSectionCreateFailureClass? preparationFailureClass = ClassifyInsertFailure(exception);
			if (preparationFailureClass is null) {
				throw;
			}

			throw BuildPreparationFailure(request, preparationFailureClass.Value, exception);
		}
		logger.BeginSpinner($"Creating section '{resolvedRequest.Caption}' ({resolvedRequest.SectionCode})...");
		int insertTimeoutMs = ResolveInsertTimeoutMilliseconds(insertTimeoutMsOverride);
		string responseBody;
		try {
			reportStage?.Invoke("creating section");
			responseBody = client.ExecutePostRequest(
				serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Insert, environmentSettings),
				requestBody,
				insertTimeoutMs);
		} catch (Exception exception) {
			ApplicationSectionCreateFailureClass? failureClass = ClassifyInsertFailure(exception);
			if (failureClass is null) {
				logger.EndSpinner(false);
				throw;
			}
			if (failureClass == ApplicationSectionCreateFailureClass.CreatioTimeout) {
				return RecoverFromInsertTimeout(
					environmentName,
					beforeInfo,
					resolvedRequest,
					client,
					environmentSettings,
					effectiveCultureName,
					insertTimeoutMs,
					exception);
			}
			logger.EndSpinner(false);
			throw failureClass == ApplicationSectionCreateFailureClass.Transport
				? BuildTransportFailure(resolvedRequest, exception)
				: BuildServerErrorFailure(resolvedRequest, exception);
		}
		EnsureInsertSucceeded(responseBody, resolvedRequest);
		logger.EndSpinner(true);

		// The CLI path leaves readbackTimeoutMsOverride null and keeps the patient Timeout.Infinite
		// default; the MCP/background path passes a finite per-request budget so a wedged readback
		// cannot hold a thread + HTTP connection for the life of the server process (ENG-91316).
		reportStage?.Invoke("loading created section");
		return LoadCreatedSection(
			environmentName,
			beforeInfo,
			resolvedRequest,
			client,
			environmentSettings,
			effectiveCultureName,
			ResolveReadbackTimeout(readbackTimeoutMsOverride));
	}

	private static void ValidateRequest(ApplicationSectionCreateRequest request) {
		if (string.IsNullOrWhiteSpace(request.ApplicationCode)) {
			throw new ArgumentException("application-code is required.");
		}

		if (string.IsNullOrWhiteSpace(request.Caption)) {
			throw new ArgumentException("caption is required.");
		}

		if (request.IconBackground is not null) {
			ApplicationSectionColorPalette.ValidateOrThrow(request.IconBackground);
		}

		if (!string.IsNullOrWhiteSpace(request.Code)) {
			string trimmedCode = request.Code.Trim();
			if (!trimmedCode.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) {
				throw new ArgumentException(
					$"Section code '{trimmedCode}' is invalid. Codes must contain only Latin letters, digits, or underscore.",
					nameof(request));
			}
		}
	}

	private ResolvedApplicationSectionCreateRequest ResolveRequest(
		ApplicationSectionCreateRequest request,
		ApplicationInfoResult applicationInfo,
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string schemaNamePrefix) {
		string sectionCode = ResolveSectionCode(request, schemaNamePrefix);
		string iconBackground = string.IsNullOrWhiteSpace(request.IconBackground)
		? GenerateRandomHexColor()
		: request.IconBackground.Trim();
		logger.WriteInfo("Resolving section icon...");
		string iconId = ResolveRandomIconId(client, environmentSettings);
		return new ResolvedApplicationSectionCreateRequest(
			Guid.NewGuid().ToString(),
			applicationInfo.ApplicationId ?? throw new InvalidOperationException("Application id was not returned by get-app-info."),
			applicationInfo.ApplicationName ?? string.Empty,
			applicationInfo.ApplicationCode ?? string.Empty,
			applicationInfo.ApplicationVersion,
			applicationInfo.PackageUId,
			applicationInfo.PackageName,
			request.Caption.Trim(),
			sectionCode,
			request.Description?.Trim(),
			request.EntitySchemaName?.Trim(),
			request.WithMobilePages,
			iconId,
			iconBackground,
			request.WithMobilePages ? null : WebClientTypeId);
	}

	private void CheckEntitySchemaDoesNotExist(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string schemaName,
		string caption) {
		try {
			SysSchemaExistsResponseDto response = SelectQueryHelper.ExecuteSelectQuery<SysSchemaExistsResponseDto>(
				client,
				serviceUrlBuilderFactory.Create(environmentSettings),
				SelectQueryHelper.BuildSelectQuery(
					"SysSchema",
					[new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name")],
					[new SelectQueryHelper.SelectQueryFilterDefinition("Name", schemaName, SelectQueryHelper.TextDataValueType)]));
			if (response.Rows.Count > 0) {
				throw new InvalidOperationException(
					$"Entity schema '{schemaName}' already exists. "
					+ $"To create section '{caption}' reusing the existing entity, add: --entity-schema-name {schemaName}");
			}
		} catch (InvalidOperationException) {
			throw;
		} catch {
			// schema existence check is best-effort; skip unexpected errors to avoid blocking section creation
		}
	}

	/// <summary>
	/// Verifies that an existing entity schema targeted by <c>--entity-schema-name</c> actually exists before the
	/// section insert is attempted, so a missing object fails with a clear message instead of the opaque server
	/// rejection ("InsertQuery failed.") that Creatio returns for a dangling entity reference. The probe is
	/// best-effort: when the readback query cannot be executed the method proceeds and lets the insert run,
	/// mirroring <see cref="CheckEntitySchemaDoesNotExist"/>. Multiple sections may target the same entity, so no
	/// section-binding uniqueness is enforced here.
	/// </summary>
	/// <param name="client">Environment-scoped application client used for the readback query.</param>
	/// <param name="environmentSettings">Resolved environment settings for the target environment.</param>
	/// <param name="entitySchemaName">Existing entity schema name the section will be bound to.</param>
	/// <param name="caption">Requested section caption, surfaced in the diagnostic message.</param>
	private void CheckEntitySchemaExists(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string entitySchemaName,
		string caption) {
		int matchedSchemaCount;
		try {
			SysSchemaExistsResponseDto response = SelectQueryHelper.ExecuteSelectQuery<SysSchemaExistsResponseDto>(
				client,
				serviceUrlBuilderFactory.Create(environmentSettings),
				SelectQueryHelper.BuildSelectQuery(
					"SysSchema",
					[new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name")],
					[new SelectQueryHelper.SelectQueryFilterDefinition("Name", entitySchemaName, SelectQueryHelper.TextDataValueType)]));
			matchedSchemaCount = response.Rows.Count;
		} catch {
			// The existence probe is best-effort: when the readback query fails (permissions, transport,
			// unexpected payloads) proceed and let the section insert surface the server error instead of
			// blocking a potentially valid creation.
			return;
		}

		if (matchedSchemaCount == 0) {
			throw new InvalidOperationException(
				$"Entity schema '{entitySchemaName}' does not exist in this environment, so section '{caption}' cannot be bound to it. "
				+ "Verify the object name (names are case-sensitive), or omit --entity-schema-name to create a new object for the section.");
		}
	}

	/// <summary>
	/// Builds a human-readable failure message for a rejected ApplicationSection insert.
	/// Propagates the server-supplied error when present and appends an actionable next step. The most
	/// common detail-less rejection is a section-code collision; the message does not assert entity-binding
	/// uniqueness because Creatio allows several sections to target the same entity (missing entities and
	/// invalid codes are already caught before the insert by the resolve/validation steps).
	/// </summary>
	/// <param name="request">Resolved section-create request used to surface the caption, code, entity, and application context.</param>
	/// <param name="serverMessage">Optional error message returned by the InsertQuery response.</param>
	/// <returns>A diagnostic message explaining the failure and how to recover from it.</returns>
	private static string BuildSectionInsertFailureMessage(
		ResolvedApplicationSectionCreateRequest request,
		string? serverMessage) {
		StringBuilder builder = new();
		builder.Append("Failed to create section '")
			.Append(request.Caption)
			.Append("' (code '")
			.Append(request.SectionCode)
			.Append("')");
		if (!string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			builder.Append(" bound to entity '")
				.Append(request.EntitySchemaName)
				.Append('\'');
		}

		if (!string.IsNullOrWhiteSpace(request.ApplicationCode)) {
			builder.Append(" in application '")
				.Append(request.ApplicationCode)
				.Append('\'');
		}

		builder.Append('.');

		string trimmedServerMessage = serverMessage?.Trim() ?? string.Empty;
		if (trimmedServerMessage.Length > 0) {
			builder.Append(" Server error: ").Append(trimmedServerMessage);
			char last = trimmedServerMessage[^1];
			if (last is not ('.' or '!' or '?')) {
				builder.Append('.');
			}
		} else {
			builder.Append(" The server rejected the section insert (InsertQuery failed) without returning a detailed message.");
		}

		builder.Append(" A section with code '")
			.Append(request.SectionCode)
			.Append("' may already exist, or the server rejected the insert for a reason it did not detail. Run 'list-app-sections' to inspect existing sections, then change the caption or pass a different --code to use another section code.");

		return builder.ToString();
	}

	/// <summary>
	/// Parses the InsertQuery response and converts every non-contract payload — HTML, empty,
	/// or an explicit rejection — into a classified server-error failure with the spinner closed.
	/// </summary>
	private void EnsureInsertSucceeded(
		string responseBody,
		ResolvedApplicationSectionCreateRequest resolvedRequest) {
		InsertQueryResponseDto? response;
		try {
			response = JsonSerializer.Deserialize<InsertQueryResponseDto>(responseBody, JsonOptions);
		} catch (JsonException jsonException) {
			logger.EndSpinner(false);
			throw new ApplicationSectionCreateException(
				$"Failed to create section '{resolvedRequest.Caption}' (code '{resolvedRequest.SectionCode}'): "
				+ "Creatio returned a non-JSON response to the section insert (an HTML error page is the usual "
				+ "cause), so the server is likely misconfigured or in a broken state.",
				ApplicationSectionCreateFailureClass.ServerError,
				sectionCreated: null,
				ServerErrorRetryGuidance,
				jsonException);
		}
		if (response is null) {
			logger.EndSpinner(false);
			throw new ApplicationSectionCreateException(
				$"Failed to create section '{resolvedRequest.Caption}' (code '{resolvedRequest.SectionCode}'): "
				+ "Creatio returned an empty insert response, so the actual insert outcome is unknown.",
				ApplicationSectionCreateFailureClass.ServerError,
				sectionCreated: null,
				ServerErrorRetryGuidance);
		}
		if (!response.Success) {
			logger.EndSpinner(false);
			throw new ApplicationSectionCreateException(
				BuildSectionInsertFailureMessage(resolvedRequest, response.ErrorInfo?.Message),
				ApplicationSectionCreateFailureClass.ServerError,
				sectionCreated: false,
				ServerErrorRetryGuidance);
		}
	}

	private static int ResolveInsertTimeoutMilliseconds(int? insertTimeoutMsOverride = null) {
		// An explicit override (the MCP background path) wins over the env var and the default: the
		// response deadline guards the client, so the insert may run long enough to commit the section.
		if (insertTimeoutMsOverride is > 0) {
			return insertTimeoutMsOverride.Value;
		}

		string? raw = Environment.GetEnvironmentVariable(InsertTimeoutEnvironmentVariable);
		if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int seconds) && seconds > 0) {
			return (int)Math.Min(seconds * 1000L, int.MaxValue);
		}

		return DefaultInsertTimeoutMs;
	}

	private static int ResolveReadbackTimeout(int? readbackTimeoutMsOverride) =>
		// A positive override (the MCP background path) bounds each readback HTTP call so a wedged
		// readback cannot park a thread/connection forever; the CLI path passes null and keeps the
		// patient Timeout.Infinite default. The 'is > 0' guard also rejects 0/-1 as a safety net.
		readbackTimeoutMsOverride is > 0 ? readbackTimeoutMsOverride.Value : Timeout.Infinite;

	/// <summary>
	/// Classifies a failed ApplicationSection insert into transport / creatio-timeout / server-error,
	/// or <c>null</c> when the failure is not network-shaped (preserving the original exception).
	/// </summary>
	private static ApplicationSectionCreateFailureClass? ClassifyInsertFailure(Exception exception) {
		bool sawHttpRequestException = false;
		foreach (Exception current in FlattenExceptionChain(exception)) {
			switch (current) {
				case WebException webException when ClassifyWebException(webException) is { } webClass:
					return webClass;
				case SocketException:
					return ApplicationSectionCreateFailureClass.Transport;
				case HttpRequestException { StatusCode: { } statusCode }:
					return ClassifyHttpStatus(statusCode);
				case HttpRequestException:
					// Keep walking the chain: an inner SocketException/TimeoutException is more precise.
					sawHttpRequestException = true;
					break;
				case TimeoutException or TaskCanceledException or OperationCanceledException:
					return ApplicationSectionCreateFailureClass.CreatioTimeout;
			}
		}

		return sawHttpRequestException ? ApplicationSectionCreateFailureClass.Transport : null;
	}

	private static ApplicationSectionCreateFailureClass? ClassifyWebException(WebException exception) =>
		exception.Status switch {
			WebExceptionStatus.ConnectFailure
				or WebExceptionStatus.NameResolutionFailure
				or WebExceptionStatus.ProxyNameResolutionFailure
				or WebExceptionStatus.SecureChannelFailure
				or WebExceptionStatus.TrustFailure => ApplicationSectionCreateFailureClass.Transport,
			WebExceptionStatus.Timeout
				or WebExceptionStatus.RequestCanceled
				or WebExceptionStatus.KeepAliveFailure
				or WebExceptionStatus.ReceiveFailure
				or WebExceptionStatus.ConnectionClosed
				or WebExceptionStatus.PipelineFailure => ApplicationSectionCreateFailureClass.CreatioTimeout,
			WebExceptionStatus.ProtocolError => ClassifyWebProtocolError(exception),
			_ => null
		};

	private static ApplicationSectionCreateFailureClass ClassifyWebProtocolError(WebException exception) =>
		exception.Response is HttpWebResponse httpResponse
			? ClassifyHttpStatus(httpResponse.StatusCode)
			: ApplicationSectionCreateFailureClass.ServerError;

	/// <summary>
	/// Transient statuses from a busy or restarting server (408/429/502/503/504) map to the
	/// retry-after-verification class instead of the non-retryable server-error class.
	/// </summary>
	private static ApplicationSectionCreateFailureClass ClassifyHttpStatus(HttpStatusCode statusCode) =>
		statusCode switch {
			HttpStatusCode.RequestTimeout
				or HttpStatusCode.TooManyRequests
				or HttpStatusCode.BadGateway
				or HttpStatusCode.ServiceUnavailable
				or HttpStatusCode.GatewayTimeout => ApplicationSectionCreateFailureClass.CreatioTimeout,
			_ => ApplicationSectionCreateFailureClass.ServerError
		};

	// Bounds pathological self-referencing exception chains during classification.
	private const int MaxFlattenedExceptions = 32;

	private static IEnumerable<Exception> FlattenExceptionChain(Exception exception) {
		Queue<Exception> pending = new();
		pending.Enqueue(exception);
		int guard = 0;
		while (pending.Count > 0 && guard++ < MaxFlattenedExceptions) {
			Exception current = pending.Dequeue();
			yield return current;
			if (current is AggregateException aggregate) {
				foreach (Exception inner in aggregate.InnerExceptions) {
					pending.Enqueue(inner);
				}
			} else if (current.InnerException is not null) {
				pending.Enqueue(current.InnerException);
			}
		}
	}

	/// <summary>
	/// Handles a timed-out ApplicationSection insert: returns the normal readback result when the
	/// section is already visible despite the timeout, otherwise throws the classified failure.
	/// </summary>
	private ApplicationSectionCreateResult RecoverFromInsertTimeout(
		string environmentName,
		ApplicationInfoResult beforeInfo,
		ResolvedApplicationSectionCreateRequest resolvedRequest,
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string effectiveCultureName,
		int insertTimeoutMs,
		Exception cause) {
		bool? sectionVisible = TryVerifySectionExists(client, environmentSettings, resolvedRequest);
		if (sectionVisible == true) {
			logger.EndSpinner(true);
			logger.WriteInfo(
				$"Insert response timed out after {insertTimeoutMs / 1000}s, but section "
				+ $"'{resolvedRequest.SectionCode}' is already visible — continuing with readback.");
			// The server already proved slow, so the recovery readback runs bounded too —
			// otherwise the budget guarantee would be lost right after the timeout.
			return LoadCreatedSection(
				environmentName,
				beforeInfo,
				resolvedRequest,
				client,
				environmentSettings,
				effectiveCultureName,
				VerificationTimeoutMs);
		}

		logger.EndSpinner(false);
		throw BuildTimeoutFailure(resolvedRequest, insertTimeoutMs, sectionVisible, cause);
	}

	/// <summary>
	/// Bounded post-timeout side-effect check: returns <c>true</c>/<c>false</c> when the
	/// ApplicationSection readback answered, or <c>null</c> when verification itself failed.
	/// Matches strictly by the section identifier generated for this call, so a pre-existing
	/// section bound to the same entity can never produce a false-positive recovery.
	/// </summary>
	private bool? TryVerifySectionExists(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		ResolvedApplicationSectionCreateRequest request) {
		try {
			logger.WriteInfo(
				$"Insert timed out — verifying whether section '{request.SectionCode}' was created anyway...");
			ApplicationSectionSelectQueryResponseDto response =
				SelectQueryHelper.ExecuteSelectQuery<ApplicationSectionSelectQueryResponseDto>(
					client,
					serviceUrlBuilderFactory.Create(environmentSettings),
					BuildSectionSelectQuery(request.ApplicationId),
					VerificationTimeoutMs);
			return response.Rows.Any(row =>
				string.Equals(row.Id, request.Id, StringComparison.OrdinalIgnoreCase));
		} catch (Exception verificationError) {
			// Verification is best-effort by design: its outcome only refines the diagnostic
			// (`section-created: unknown` instead of true/false), so any failure is reported, not thrown.
			logger.WriteInfo($"Post-timeout verification readback failed: {verificationError.Message}");
			return null;
		}
	}

	private static ApplicationSectionCreateException BuildPreparationFailure(
		ApplicationSectionCreateRequest request,
		ApplicationSectionCreateFailureClass failureClass,
		Exception cause) =>
		new(
			$"Failed to create section '{request.Caption}' in application '{request.ApplicationCode}': a "
			+ $"preparation step failed before the section insert was attempted ({RootCauseMessage(cause)}).",
			failureClass,
			sectionCreated: false,
			PreparationRetryGuidance,
			cause);

	private static ApplicationSectionCreateException BuildTransportFailure(
		ResolvedApplicationSectionCreateRequest request,
		Exception cause) =>
		new(
			$"Failed to create section '{request.Caption}' (code '{request.SectionCode}'): the Creatio server "
			+ $"could not be reached ({RootCauseMessage(cause)}). The request never reached the server, so no "
			+ "section was created.",
			ApplicationSectionCreateFailureClass.Transport,
			sectionCreated: false,
			TransportRetryGuidance,
			cause);

	private static ApplicationSectionCreateException BuildTimeoutFailure(
		ResolvedApplicationSectionCreateRequest request,
		int insertTimeoutMs,
		bool? sectionVisible,
		Exception cause) {
		string verificationOutcome = sectionVisible is null
			? "The post-timeout verification readback also failed, so it is unknown whether the section was created."
			: $"A post-timeout check did not find section '{request.SectionCode}' yet — Creatio may still be "
			+ "processing the insert.";
		return new ApplicationSectionCreateException(
			$"Creatio did not respond within {insertTimeoutMs / 1000}s while creating section "
			+ $"'{request.Caption}' (code '{request.SectionCode}'). {verificationOutcome}",
			ApplicationSectionCreateFailureClass.CreatioTimeout,
			sectionVisible,
			sectionVisible is null ? TimeoutUnknownRetryGuidance : TimeoutNotCreatedRetryGuidance,
			cause);
	}

	private static ApplicationSectionCreateException BuildServerErrorFailure(
		ResolvedApplicationSectionCreateRequest request,
		Exception cause) =>
		new(
			$"Failed to create section '{request.Caption}' (code '{request.SectionCode}'): Creatio returned an "
			+ $"error response ({RootCauseMessage(cause)}).",
			ApplicationSectionCreateFailureClass.ServerError,
			sectionCreated: false,
			ServerErrorRetryGuidance,
			cause);

	private static string RootCauseMessage(Exception exception) =>
		exception.GetBaseException().Message;

	private ApplicationSectionCreateResult LoadCreatedSection(
		string environmentName,
		ApplicationInfoResult beforeInfo,
		ResolvedApplicationSectionCreateRequest request,
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string effectiveCultureName,
		int readbackTimeout = Timeout.Infinite) {
		Exception? lastError = null;
		for (int attempt = 1; attempt <= PollAttempts; attempt++) {
			try {
				logger.WriteInfo($"Waiting for section '{request.SectionCode}' to be ready... (attempt {attempt}/{PollAttempts})");
				ApplicationInfoResult afterInfo = applicationInfoService.GetApplicationInfo(
					environmentName,
					null,
					request.ApplicationCode);
				ApplicationSectionRecord createdSection = GetSectionRecord(
					client,
					environmentSettings,
					request.ApplicationId,
					request.Id,
					request.SectionCode,
					entitySchemaName: request.EntitySchemaName,
					requestTimeout: readbackTimeout);
				SetIconBackground(client, environmentSettings, createdSection, request.IconBackground, readbackTimeout);
				string? entitySchemaName = string.IsNullOrWhiteSpace(createdSection.EntitySchemaName)
					? request.EntitySchemaName
					: createdSection.EntitySchemaName;
				ApplicationEntityInfoResult? entity = ResolveEntity(afterInfo, beforeInfo, entitySchemaName);
				IReadOnlyList<PageListItem> createdPages = ResolveCreatedPages(beforeInfo, afterInfo);
				return new ApplicationSectionCreateResult(
					afterInfo.PackageUId,
					afterInfo.PackageName,
					afterInfo.ApplicationId ?? request.ApplicationId,
					afterInfo.ApplicationName ?? request.ApplicationName,
					afterInfo.ApplicationCode ?? request.ApplicationCode,
					afterInfo.ApplicationVersion ?? request.ApplicationVersion,
					new ApplicationSectionInfoResult(
						createdSection.Id,
						createdSection.Code,
						ResolveLocalizedCaption(createdSection.Caption, request.Caption, effectiveCultureName),
						createdSection.Description,
						entitySchemaName,
						createdSection.PackageId,
						createdSection.SectionSchemaUId,
						createdSection.LogoId,
						request.IconBackground,
						createdSection.ClientTypeId),
					entity,
					createdPages);
			} catch (Exception exception) {
				lastError = exception;
				if (attempt < PollAttempts) {
					System.Threading.Thread.Sleep(PollDelay);
				}
			}
		}

		throw new InvalidOperationException(
			$"Section '{request.SectionCode}' was created but its metadata could not be loaded after {PollAttempts} attempts. Last error: {lastError!.Message}",
			lastError);
	}

	private void SetIconBackground(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		ApplicationSectionRecord section,
		string iconBackground,
		int requestTimeout = Timeout.Infinite) {
		string body = JsonSerializer.Serialize(BuildIconBackgroundUpdateBody(section, iconBackground), JsonOptions);
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update, environmentSettings),
			body,
			requestTimeout);
		UpdateQueryResponseDto response = JsonSerializer.Deserialize<UpdateQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("Icon background UpdateQuery returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "Icon background UpdateQuery failed.");
		}
	}

	private static object BuildIconBackgroundUpdateBody(ApplicationSectionRecord section, string iconBackground) {
		Dictionary<string, object> columnItems = new(StringComparer.Ordinal) {
			["Id"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, section.Id),
			["ApplicationId"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, section.ApplicationId),
			[LogoIdField] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, section.LogoId ?? string.Empty),
			[PackageIdField] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, section.PackageId ?? string.Empty),
			["IconBackground"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType, iconBackground)
		};
		return new Dictionary<string, object> {
			["__type"] = "Terrasoft.Nui.ServiceModel.DataContract.UpdateQuery",
			["operationType"] = 2,
			["rootSchemaName"] = ApplicationSectionSchemaName,
			["isForceUpdate"] = false,
			["columnValues"] = new {
				items = columnItems
			},
			["filters"] = new {
				filterType = 6,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				logicalOperation = 0,
				items = new {
					primaryFilter = new {
						filterType = 1,
						comparisonType = 3,
						isEnabled = true,
						trimDateTimeParameterToDate = false,
						leftExpression = new {
							expressionType = 0,
							columnPath = "Id"
						},
						rightExpression = new {
							expressionType = 2,
							parameter = new {
								dataValueType = SelectQueryHelper.TextDataValueType,
								value = section.Id
							}
						}
					}
				}
			}
		};
	}

	private static object BuildSectionSelectQuery(string applicationId) =>
		SelectQueryHelper.BuildSelectQuery(
			ApplicationSectionSchemaName,
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
				new SelectQueryHelper.SelectQueryColumnDefinition(ApplicationIdJsonField, ApplicationIdJsonField),
				new SelectQueryHelper.SelectQueryColumnDefinition("Caption", "Caption"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Code", "Code"),
				new SelectQueryHelper.SelectQueryColumnDefinition("Description", "Description"),
				new SelectQueryHelper.SelectQueryColumnDefinition("EntitySchemaName", "EntitySchemaName"),
				new SelectQueryHelper.SelectQueryColumnDefinition(PackageIdField, PackageIdField),
				new SelectQueryHelper.SelectQueryColumnDefinition("SectionSchemaUId", "SectionSchemaUId"),
				new SelectQueryHelper.SelectQueryColumnDefinition(LogoIdField, LogoIdField),
				new SelectQueryHelper.SelectQueryColumnDefinition("IconBackground", "IconBackground"),
				new SelectQueryHelper.SelectQueryColumnDefinition("ClientTypeId", "ClientTypeId")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					ApplicationIdJsonField,
					applicationId,
					SelectQueryHelper.GuidDataValueType)
			]);

	private ApplicationSectionRecord GetSectionRecord(
		IApplicationClient client,
		EnvironmentSettings environmentSettings,
		string applicationId,
		string sectionId,
		string sectionCode,
		string? entitySchemaName = null,
		int requestTimeout = Timeout.Infinite) {
		ApplicationSectionSelectQueryResponseDto response = SelectQueryHelper.ExecuteSelectQuery<ApplicationSectionSelectQueryResponseDto>(
			client,
			serviceUrlBuilderFactory.Create(environmentSettings),
			BuildSectionSelectQuery(applicationId),
			requestTimeout);
		string searchDescription = string.IsNullOrWhiteSpace(entitySchemaName)
			? $"'{sectionCode}'"
			: $"'{sectionCode}' or entity schema name '{entitySchemaName}'";
		// Prefer the identifier generated for this call: it can never match a pre-existing
		// section, unlike the code/entity fallback kept for servers that rewrite the section id.
		return response.Rows
				.FirstOrDefault(row => string.Equals(row.Id, sectionId, StringComparison.OrdinalIgnoreCase))
			?? response.Rows
				.FirstOrDefault(row =>
					string.Equals(row.Code, sectionCode, StringComparison.OrdinalIgnoreCase)
					|| (!string.IsNullOrWhiteSpace(entitySchemaName)
						&& string.Equals(row.EntitySchemaName, entitySchemaName, StringComparison.OrdinalIgnoreCase)))
			?? throw new InvalidOperationException(
				$"Section {searchDescription} was not found in application '{applicationId}'.");
	}

	private static ApplicationEntityInfoResult? ResolveEntity(
		ApplicationInfoResult afterInfo,
		ApplicationInfoResult beforeInfo,
		string? entitySchemaName) {
		if (!string.IsNullOrWhiteSpace(entitySchemaName)) {
			return afterInfo.Entities.FirstOrDefault(entity =>
				string.Equals(entity.Name, entitySchemaName, StringComparison.OrdinalIgnoreCase));
		}

		HashSet<string> previousEntities = beforeInfo.Entities
			.Select(entity => entity.Name)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return afterInfo.Entities.FirstOrDefault(entity => !previousEntities.Contains(entity.Name));
	}

	private static IReadOnlyList<PageListItem> ResolveCreatedPages(
		ApplicationInfoResult beforeInfo,
		ApplicationInfoResult afterInfo) {
		HashSet<string> previousPageKeys = (beforeInfo.Pages ?? [])
			.Select(CreatePageIdentity)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return (afterInfo.Pages ?? [])
			.Where(page => !previousPageKeys.Contains(CreatePageIdentity(page)))
			.ToList();
	}

	private static string CreatePageIdentity(PageListItem page) =>
		$"{page.SchemaName}|{page.UId}|{page.PackageName}";

	private static string ResolveLocalizedCaption(string? value, string fallbackCaption, string? effectiveCultureName = null) {
		if (string.IsNullOrWhiteSpace(value)) {
			return fallbackCaption;
		}

		try {
			Dictionary<string, string>? localizedValues = JsonSerializer.Deserialize<Dictionary<string, string>>(
				value,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (localizedValues is null || localizedValues.Count == 0) {
				return value;
			}

			// Show the readback caption in the effective culture when that culture key exists in the
			// returned map. Otherwise prefer the en-US value, then any non-empty value. The host
			// machine locale is never consulted here.
			if (!string.IsNullOrWhiteSpace(effectiveCultureName)
				&& localizedValues.TryGetValue(effectiveCultureName, out string? effectiveCaption)
				&& !string.IsNullOrWhiteSpace(effectiveCaption)) {
				return effectiveCaption;
			}

			if (localizedValues.TryGetValue("en-US", out string? enUsCaption) &&
				!string.IsNullOrWhiteSpace(enUsCaption)) {
				return enUsCaption;
			}

			string? firstValue = localizedValues.Values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
			return string.IsNullOrWhiteSpace(firstValue) ? fallbackCaption : firstValue;
		} catch (JsonException) {
			return value;
		}
	}

	private object BuildInsertBody(ResolvedApplicationSectionCreateRequest request) {
		Dictionary<string, object> items = new(StringComparer.Ordinal) {
			["Id"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.Id),
			["Caption"] = CreateParameterExpression(
				SelectQueryHelper.TextDataValueType,
				request.Caption),
			[ApplicationIdJsonField] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.ApplicationId),
			[PackageIdField] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.PackageUId),
			[LogoIdField] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.IconId),
			["IconBackground"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType, request.IconBackground),
			["Type"] = CreateParameterExpression(SelectQueryHelper.IntDataValueType, SectionTypeNormal),
			["Code"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType, request.SectionCode)
		};
		if (!string.IsNullOrWhiteSpace(request.Description)) {
			items["Description"] = CreateParameterExpression(SelectQueryHelper.TextDataValueType, request.Description);
		}

		if (!string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			items["EntitySchemaName"] = CreateParameterExpression(
				SelectQueryHelper.TextDataValueType,
				request.EntitySchemaName);
		}

		if (!string.IsNullOrWhiteSpace(request.ClientTypeId)) {
			items["ClientTypeId"] = CreateParameterExpression(SelectQueryHelper.GuidDataValueType, request.ClientTypeId);
		}

		return new {
			rootSchemaName = ApplicationSectionSchemaName,
			columnValues = new {
				items
			}
		};
	}

	private static object CreateParameterExpression(int dataValueType, object value) =>
		new {
			expressionType = 2,
			parameter = new {
				dataValueType,
				value
			}
		};

	private string ResolveRandomIconId(IApplicationClient client, EnvironmentSettings environmentSettings) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(SelectQueryRoute, environmentSettings),
			JsonSerializer.Serialize(BuildRandomIconQuery()));
		IconSelectQueryResponseDto response = JsonSerializer.Deserialize<IconSelectQueryResponseDto>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("SysAppIcons query returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "Failed to query SysAppIcons.");
		}

		if (response.Rows.Count == 0) {
			throw new InvalidOperationException("No icons found in SysAppIcons.");
		}

		int index = Random.Shared.Next(response.Rows.Count);
		return response.Rows[index].Id;
	}

	private static object BuildRandomIconQuery() =>
		new {
			rootSchemaName = "SysAppIcons",
			operationType = 0,
			allColumns = false,
			isDistinct = false,
			ignoreDisplayValues = false,
			rowCount = -1,
			rowsOffset = -1,
			isPageable = false,
			conditionalValues = (object?)null,
			isHierarchical = false,
			hierarchicalMaxDepth = 0,
			hierarchicalColumnFiltersValue = new {
				filterType = 6,
				isEnabled = true,
				items = new Dictionary<string, object>(),
				logicalOperation = 0,
				trimDateTimeParameterToDate = false
			},
			hierarchicalColumnName = (string?)null,
			hierarchicalColumnValue = (object?)null,
			hierarchicalFullDataLoad = false,
			useLocalization = false,
			useRecordDeactivation = false,
			columns = new {
				items = new Dictionary<string, object> {
					["Id"] = new {
						expression = new {
							expressionType = 0,
							columnPath = "Id"
						},
						orderDirection = 0,
						orderPosition = -1,
						isVisible = true
					}
				}
			},
			filters = new {
				filterType = 6,
				isEnabled = true,
				logicalOperation = 0,
				trimDateTimeParameterToDate = false,
				items = new Dictionary<string, object>()
			},
			__type = "Terrasoft.Nui.ServiceModel.DataContract.SelectQuery",
			queryKind = 0,
			serverESQCacheParameters = new {
				cacheLevel = 0,
				cacheGroup = string.Empty,
				cacheItemName = string.Empty
			},
			queryOptimize = false,
			useMetrics = false,
			querySource = 0
		};

	/// <summary>
	/// Resolves the section code to insert: uses the explicit caller-supplied code when provided
	/// (ensuring the environment schema-name prefix and validating it is a Latin identifier), otherwise
	/// generates one from the caption.
	/// </summary>
	/// <param name="request">Section creation request carrying the optional explicit code and the caption.</param>
	/// <param name="schemaNamePrefix">Environment schema-name prefix (for example <c>Usr</c>).</param>
	/// <returns>A valid Latin section code.</returns>
	private static string ResolveSectionCode(ApplicationSectionCreateRequest request, string schemaNamePrefix) {
		if (string.IsNullOrWhiteSpace(request.Code)) {
			return GenerateCodeFromCaption(request.Caption, schemaNamePrefix);
		}

		string explicitCode = request.Code.Trim();
		if (!string.IsNullOrEmpty(schemaNamePrefix)) {
			if (explicitCode.StartsWith(schemaNamePrefix, StringComparison.OrdinalIgnoreCase)) {
				// Re-canonicalize casing: --code usrContacts with prefix Usr becomes UsrContacts.
				explicitCode = schemaNamePrefix + explicitCode[schemaNamePrefix.Length..];
			} else {
				explicitCode = schemaNamePrefix + explicitCode;
			}
		}

		if (!SectionCodeRegex.IsMatch(explicitCode)) {
			throw new ArgumentException(
				$"Section code '{explicitCode}' is invalid. Section codes must start with a Latin letter and contain only "
				+ "Latin letters, digits, or underscore.",
				nameof(request));
		}

		return explicitCode;
	}

	private static string GenerateCodeFromCaption(string caption, string schemaNamePrefix) {
		string[] words = CodeWordRegex.Split(caption.Trim())
			.Where(item => !string.IsNullOrWhiteSpace(item))
			.ToArray();
		if (words.Length == 0) {
			throw new ArgumentException(
				$"Caption '{caption}' has no Latin letters or digits to generate a section code. "
				+ "Provide an explicit code via --code (for example --code UsrContacts), or use a Latin caption.",
				nameof(caption));
		}

		StringBuilder builder = new(schemaNamePrefix);
		foreach (string word in words) {
			string normalizedWord = NormalizeWord(word);
			if (!string.IsNullOrWhiteSpace(normalizedWord)) {
				builder.Append(normalizedWord);
			}
		}

		int prefixLength = schemaNamePrefix.Length;
		if (builder.Length == prefixLength) {
			throw new ArgumentException(
				$"Caption '{caption}' has no Latin letters or digits to generate a section code. "
				+ "Provide an explicit code via --code (for example --code UsrContacts), or use a Latin caption.",
				nameof(caption));
		}

		if (prefixLength < builder.Length && char.IsDigit(builder[prefixLength])) {
			builder.Insert(prefixLength, "_");
		}

		return builder.ToString();
	}

	private static string NormalizeWord(string value) {
		// Section codes must be Latin identifiers, so only ASCII letters/digits feed the generated code.
		// Non-Latin captions (for example Cyrillic "Контакти") therefore yield no code and are reported as an
		// actionable error that points the caller at --code, instead of producing an invalid code that the
		// Creatio InsertQuery silently rejects.
		string sanitizedValue = new(value.Where(char.IsAsciiLetterOrDigit).ToArray());
		if (string.IsNullOrWhiteSpace(sanitizedValue)) {
			return string.Empty;
		}

		return sanitizedValue.Length == 1
			? sanitizedValue.ToUpperInvariant()
			: char.ToUpperInvariant(sanitizedValue[0]) + sanitizedValue[1..];
	}

	private static string GenerateRandomHexColor() => ApplicationSectionColorPalette.PickRandom();

	private sealed class InsertQueryResponseDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }
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

	private sealed class SysSchemaExistsResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<SysSchemaNameRowDto> Rows { get; set; } = [];
	}

	private sealed class SysSchemaNameRowDto {
		[JsonPropertyName("Name")]
		public string? Name { get; set; }
	}

	private sealed class IconSelectQueryResponseDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto? ErrorInfo { get; set; }

		[JsonPropertyName("rows")]
		public List<IconRowDto> Rows { get; set; } = [];
	}

	private sealed class IconRowDto {
		[JsonPropertyName("Id")]
		public string Id { get; set; } = string.Empty;
	}

	private sealed class ApplicationSectionSelectQueryResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<ApplicationSectionRecord> Rows { get; set; } = [];
	}

	private sealed record ResolvedApplicationSectionCreateRequest(
		string Id,
		string ApplicationId,
		string ApplicationName,
		string ApplicationCode,
		string? ApplicationVersion,
		string PackageUId,
		string PackageName,
		string Caption,
		string SectionCode,
		string? Description,
		string? EntitySchemaName,
		bool WithMobilePages,
		string IconId,
		string IconBackground,
		string? ClientTypeId);
}

/// <summary>
/// Creates a section inside an existing installed application and prints the structured readback payload.
/// </summary>
public sealed class CreateAppSectionCommand(
	IApplicationSectionCreateService applicationSectionCreateService,
	ILogger logger)
	: Command<CreateAppSectionOptions> {
	/// <inheritdoc />
	public override int Execute(CreateAppSectionOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}
			CreateAppSectionOptions.ValidateMobilePagesOption(options.WithMobilePagesValue);

			ApplicationSectionCreateResult result = applicationSectionCreateService.CreateSection(
				options.Environment,
				new ApplicationSectionCreateRequest(
					options.ApplicationCode,
					options.Caption,
					options.Description,
					options.EntitySchemaName,
					options.WithMobilePages,
					options.IconBackground,
					options.CaptionCulture,
					options.Code));
			logger.WriteInfo(JsonSerializer.Serialize(result));
			return 0;
		} catch (ApplicationSectionCreateException exception) {
			logger.WriteError(exception.Message);
			logger.WriteError($"Next step: {exception.RetryGuidance}");
			return 1;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Request payload for existing-app section creation.
/// </summary>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="Caption">Section caption.</param>
/// <param name="Description">Optional section description.</param>
/// <param name="EntitySchemaName">Optional existing entity schema name. When provided, the section reuses that entity.</param>
/// <param name="WithMobilePages">Whether to create mobile pages.</param>
/// <param name="IconBackground">Optional icon background color in #RRGGBB format. Defaults to a random color when omitted.</param>
/// <param name="Code">Optional explicit section code (Latin identifier). When omitted, the code is generated from the caption.</param>
public sealed record ApplicationSectionCreateRequest(
	string ApplicationCode,
	string Caption,
	string? Description = null,
	string? EntitySchemaName = null,
	bool WithMobilePages = true,
	string? IconBackground = null,
	string? CaptionCulture = null,
	string? Code = null);

/// <summary>
/// Structured result for existing-app section creation.
/// </summary>
/// <param name="PackageUId">Primary package identifier.</param>
/// <param name="PackageName">Primary package name.</param>
/// <param name="ApplicationId">Installed application identifier.</param>
/// <param name="ApplicationName">Installed application display name.</param>
/// <param name="ApplicationCode">Installed application code.</param>
/// <param name="ApplicationVersion">Installed application version.</param>
/// <param name="Section">Created section metadata.</param>
/// <param name="Entity">Created or targeted entity metadata.</param>
/// <param name="Pages">Pages created by the section flow when available.</param>
public sealed record ApplicationSectionCreateResult(
	string PackageUId,
	string PackageName,
	string ApplicationId,
	string ApplicationName,
	string ApplicationCode,
	string? ApplicationVersion,
	ApplicationSectionInfoResult Section,
	ApplicationEntityInfoResult? Entity,
	IReadOnlyList<PageListItem> Pages);

/// <summary>
/// Structured section metadata returned by existing-app section creation.
/// </summary>
/// <param name="Id">Section identifier.</param>
/// <param name="Code">Section code.</param>
/// <param name="Caption">Section caption.</param>
/// <param name="Description">Optional section description.</param>
/// <param name="EntitySchemaName">Target entity schema name.</param>
/// <param name="PackageId">Package identifier used for the section.</param>
/// <param name="SectionSchemaUId">Generated section schema identifier when available.</param>
/// <param name="IconId">Resolved icon identifier.</param>
/// <param name="IconBackground">Resolved icon background color.</param>
/// <param name="ClientTypeId">Optional client type selector used for page generation.</param>
public sealed record ApplicationSectionInfoResult(
	string Id,
	string Code,
	string Caption,
	string? Description,
	string? EntitySchemaName,
	string? PackageId,
	string? SectionSchemaUId,
	string? IconId,
	string? IconBackground,
	string? ClientTypeId);

/// <summary>
/// Section readback row from the ApplicationSection virtual object.
/// </summary>
/// <param name="Id">Section identifier.</param>
/// <param name="ApplicationId">Installed application identifier.</param>
/// <param name="Caption">Localized caption payload.</param>
/// <param name="Code">Section code.</param>
/// <param name="Description">Optional section description.</param>
/// <param name="EntitySchemaName">Target entity schema name.</param>
/// <param name="PackageId">Package identifier.</param>
/// <param name="SectionSchemaUId">Section list page schema UId.</param>
/// <param name="LogoId">Icon identifier.</param>
/// <param name="IconBackground">Icon background color.</param>
/// <param name="ClientTypeId">Optional client type selector.</param>
/// <param name="CardSchemaUId">Section form page schema UId.</param>
/// <param name="SysModuleEntityId">Identifier of the associated SysModuleEntity record.</param>
public sealed record ApplicationSectionRecord(
	[property: JsonPropertyName("Id")] string Id,
	[property: JsonPropertyName("ApplicationId")] string ApplicationId,
	[property: JsonPropertyName("Caption")] string? Caption,
	[property: JsonPropertyName("Code")] string Code,
	[property: JsonPropertyName("Description")] string? Description,
	[property: JsonPropertyName("EntitySchemaName")] string? EntitySchemaName,
	[property: JsonPropertyName("PackageId")] string? PackageId,
	[property: JsonPropertyName("SectionSchemaUId")] string? SectionSchemaUId,
	[property: JsonPropertyName("LogoId")] string? LogoId,
	[property: JsonPropertyName("IconBackground")] string? IconBackground,
	[property: JsonPropertyName("ClientTypeId")] string? ClientTypeId,
	[property: JsonPropertyName("CardSchemaUId")] string? CardSchemaUId,
	[property: JsonPropertyName("SysModuleEntityId")] string? SysModuleEntityId);
