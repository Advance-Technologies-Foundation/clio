using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Reads and updates Creatio email-designer content.</summary>
public interface IEmailTemplateContentService {
	/// <summary>Reads all content variants stored for one email host record.</summary>
	EmailTemplateContentResponse Get(string environmentName, Guid emailId, string language, string languageId);

	/// <summary>Updates one content variant after checking its optimistic-concurrency checksum.</summary>
	EmailTemplateUpdateResponse Update(EmailTemplateUpdateArgs args);
}

/// <summary>MCP tools for Creatio marketing-email and message-template content.</summary>
[McpServerToolType]
public sealed class EmailTemplateTool(IEmailTemplateContentService service) {
	internal const string GetToolName = "get-email-template";
	internal const string UpdateToolName = "update-email-template";

	/// <summary>Reads legacy Content designer and current Beefree content for an email host.</summary>
	[McpServerTool(Name = GetToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Reads the content of a Creatio marketing email (BulkEmail) or message template (EmailTemplate). " +
		"Returns every current Beefree variant (BfEmailTemplate PageJson/PageHtml/AmpHtml) and every legacy " +
		"variant (Body/Subject/TemplateConfig), each with a checksum for guarded updates. Omitting language returns the " +
		"Beefree variant the email sends by default (is-default), which is not necessarily the one with an empty language. " +
		"Use the returned " +
		"email-id and checksum with update-email-template; do not author legacy TemplateConfig when Beefree content is available.")]
	public EmailTemplateContentResponse Get(
		[Description("Parameters: email-id and environment-name (both required).")]
		[Required] EmailTemplateGetArgs args) {
		if (!Guid.TryParse(args.EmailId, out Guid emailId)) {
			return EmailTemplateContentResponse.Failure("email-id must be a GUID.");
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return EmailTemplateContentResponse.Failure("environment-name is required.");
		}
		if (!string.IsNullOrWhiteSpace(args.LanguageId) && !Guid.TryParse(args.LanguageId, out _)) {
			return EmailTemplateContentResponse.Failure("language-id must be a GUID.");
		}
		return service.Get(args.EnvironmentName, emailId, args.Language, args.LanguageId);
	}

	/// <summary>Updates or creates one guarded email-content variant.</summary>
	[McpServerTool(Name = UpdateToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description(
		"Updates one Creatio email-content variant with an optimistic checksum guard. Supports current Beefree " +
		"storage and legacy content for both BulkEmail and EmailTemplate hosts. A Beefree update creates the " +
		"BfEmailTemplate row when the target host has none, enabling a get-source then update-target copy workflow " +
		"without converting the content to legacy TemplateConfig. Omitting language edits the existing default Beefree row " +
		"in place rather than adding a second one, and is-default is set only when a row is created. config-type and " +
		"is-html-body apply to EmailTemplate message-template hosts only; a request whose fields the host cannot carry is " +
		"refused rather than partially applied. Call get-email-template immediately before this " +
		"tool and pass that variant's checksum. Requires confirm=true.")]
	public EmailTemplateUpdateResponse Update(
		[Description("Parameters: email-id, environment-name, format, expected-checksum, confirm, and format-specific content.")]
		[Required] EmailTemplateUpdateArgs args) => service.Update(args);
}

/// <summary>Arguments for <c>get-email-template</c>.</summary>
public sealed record EmailTemplateGetArgs {
	/// <summary>BulkEmail or EmailTemplate record identifier.</summary>
	[JsonPropertyName("email-id")]
	[Description("GUID of the BulkEmail marketing email or EmailTemplate message-template host record.")]
	[Required]
	public required string EmailId { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Optional Beefree language whose absence should receive a guarded placeholder.</summary>
	[JsonPropertyName("language")]
	[Description("Optional Beefree language code. When that variant is absent, returns an exists=false variant with a creation checksum.")]
	public string Language { get; init; }

	/// <summary>Optional translated legacy language whose absence should receive a guarded placeholder.</summary>
	[JsonPropertyName("language-id")]
	[Description("Optional SysLanguage GUID for EmailTemplateLang. When absent, returns an exists=false legacy variant with a creation checksum.")]
	public string LanguageId { get; init; }
}

/// <summary>Arguments for <c>update-email-template</c>.</summary>
public sealed record EmailTemplateUpdateArgs {
	/// <summary>BulkEmail or EmailTemplate record identifier.</summary>
	[JsonPropertyName("email-id")]
	[Description("GUID of the existing BulkEmail or EmailTemplate host record.")]
	[Required]
	public required string EmailId { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Content storage format.</summary>
	[JsonPropertyName("format")]
	[Description("Content format: beefree for BfEmailTemplate PageJson/PageHtml, or legacy for Body/TemplateConfig.")]
	[Required]
	public required string Format { get; init; }

	/// <summary>Checksum returned by the immediately preceding read.</summary>
	[JsonPropertyName("expected-checksum")]
	[Description("Checksum returned for this exact format/language variant by get-email-template. The write is refused if current content differs.")]
	[Required]
	public required string ExpectedChecksum { get; init; }

	/// <summary>Explicit write confirmation.</summary>
	[JsonPropertyName("confirm")]
	[Description("Must be true to authorize the update. False or omitted performs no remote write.")]
	public bool Confirm { get; init; }

	/// <summary>Beefree language code; empty selects the default variant.</summary>
	[JsonPropertyName("language")]
	[Description("Beefree language code. Omit or pass an empty string for the default variant.")]
	public string Language { get; init; }

	/// <summary>Legacy EmailTemplateLang language record identifier.</summary>
	[JsonPropertyName("language-id")]
	[Description("Legacy SysLanguage GUID. Omit for the primary EmailTemplate/BulkEmail content.")]
	public string LanguageId { get; init; }

	/// <summary>Beefree designer JSON.</summary>
	[JsonPropertyName("page-json")]
	[Description("Required for format=beefree. Complete Beefree PageJson document.")]
	public string PageJson { get; init; }

	/// <summary>Rendered Beefree HTML.</summary>
	[JsonPropertyName("page-html")]
	[Description("Required for format=beefree. Complete Beefree PageHtml document matching page-json.")]
	public string PageHtml { get; init; }

	/// <summary>Optional Beefree AMP HTML.</summary>
	[JsonPropertyName("amp-html")]
	[Description("Optional Beefree AMP HTML for the same variant.")]
	public string AmpHtml { get; init; }

	/// <summary>Beefree template version.</summary>
	[JsonPropertyName("template-version")]
	[Description("Optional Beefree template version. Omit to preserve the current value; a new row defaults to 0.")]
	public int? TemplateVersion { get; init; }

	/// <summary>Legacy email subject.</summary>
	[JsonPropertyName("subject")]
	[Description("Subject for format=legacy. Omit to preserve the current value.")]
	public string Subject { get; init; }

	/// <summary>Legacy rendered body.</summary>
	[JsonPropertyName("body")]
	[Description("Body for format=legacy. Omit to preserve the current value.")]
	public string Body { get; init; }

	/// <summary>Legacy Content designer configuration.</summary>
	[JsonPropertyName("template-config")]
	[Description("TemplateConfig for format=legacy. Omit to preserve the current value. Do not put Beefree PageJson here.")]
	public string TemplateConfig { get; init; }

	/// <summary>Legacy template configuration type.</summary>
	[JsonPropertyName("config-type")]
	[Description("Legacy EmailTemplate ConfigType. Omit to preserve the current value.")]
	public int? ConfigType { get; init; }

	/// <summary>Whether the legacy body is HTML.</summary>
	[JsonPropertyName("is-html-body")]
	[Description("Legacy IsHtmlBody. Omit to preserve the current value.")]
	public bool? IsHtmlBody { get; init; }
}

/// <summary>Result of reading email content.</summary>
public sealed record EmailTemplateContentResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string Error,
	[property: JsonPropertyName("email-id")] string EmailId,
	[property: JsonPropertyName("host-type")] string HostType,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("variants")] IReadOnlyList<EmailTemplateContentVariant> Variants) {
	/// <summary>Creates a failed read response.</summary>
	public static EmailTemplateContentResponse Failure(string error) =>
		new(false, error, null, null, null, []);
}

/// <summary>One language and storage-format variant of an email.</summary>
public sealed record EmailTemplateContentVariant(
	[property: JsonPropertyName("format")] string Format,
	[property: JsonPropertyName("exists")] bool Exists,
	[property: JsonPropertyName("record-id")] string RecordId,
	[property: JsonPropertyName("language")] string Language,
	[property: JsonPropertyName("language-id")] string LanguageId,
	[property: JsonPropertyName("checksum")] string Checksum,
	[property: JsonPropertyName("page-json")] string PageJson,
	[property: JsonPropertyName("page-html")] string PageHtml,
	[property: JsonPropertyName("amp-html")] string AmpHtml,
	[property: JsonPropertyName("template-version")] int? TemplateVersion,
	[property: JsonPropertyName("subject")] string Subject,
	[property: JsonPropertyName("body")] string Body,
	[property: JsonPropertyName("template-config")] string TemplateConfig,
	[property: JsonPropertyName("config-type")] int? ConfigType,
	[property: JsonPropertyName("is-html-body")] bool? IsHtmlBody,
	[property: JsonPropertyName("is-default")] bool? IsDefault);

/// <summary>Result of an email-content update.</summary>
public sealed record EmailTemplateUpdateResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string Error,
	[property: JsonPropertyName("email-id")] string EmailId,
	[property: JsonPropertyName("format")] string Format,
	[property: JsonPropertyName("created")] bool Created,
	[property: JsonPropertyName("checksum")] string Checksum) {
	/// <summary>Creates a failed update response.</summary>
	public static EmailTemplateUpdateResponse Failure(string error) => new(false, error, null, null, false, null);
}

/// <summary>OData-backed email-content implementation.</summary>
public sealed class EmailTemplateContentService(IToolCommandResolver commandResolver) : IEmailTemplateContentService {
	private const int RequestTimeout = 30_000;
	private const string BeefreeFormat = "beefree";
	private const string LegacyFormat = "legacy";
	private const string BulkEmailHost = "bulk-email";
	private const string MessageTemplateHost = "message-template";

	/// <inheritdoc />
	public EmailTemplateContentResponse Get(string environmentName, Guid emailId, string language, string languageId) {
		try {
			(IApplicationClient client, IServiceUrlBuilder urls) = Resolve(environmentName);
			return Load(client, urls, emailId, language, languageId);
		} catch (Exception ex) {
			return EmailTemplateContentResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}

	/// <inheritdoc />
	public EmailTemplateUpdateResponse Update(EmailTemplateUpdateArgs args) {
		if (!Guid.TryParse(args.EmailId, out Guid emailId)) {
			return EmailTemplateUpdateResponse.Failure("email-id must be a GUID.");
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return EmailTemplateUpdateResponse.Failure("environment-name is required.");
		}
		if (!args.Confirm) {
			return EmailTemplateUpdateResponse.Failure("confirm=true is required; no email content was changed.");
		}
		string format = args.Format?.Trim().ToLowerInvariant();
		if (format is not BeefreeFormat and not LegacyFormat) {
			return EmailTemplateUpdateResponse.Failure("format must be 'beefree' or 'legacy'.");
		}
		if (string.IsNullOrWhiteSpace(args.ExpectedChecksum)) {
			return EmailTemplateUpdateResponse.Failure("expected-checksum is required. Call get-email-template immediately before updating.");
		}
		if (!string.IsNullOrWhiteSpace(args.LanguageId) && !Guid.TryParse(args.LanguageId, out _)) {
			return EmailTemplateUpdateResponse.Failure("language-id must be a GUID.");
		}
		try {
			(IApplicationClient client, IServiceUrlBuilder urls) = Resolve(args.EnvironmentName);
			EmailTemplateContentResponse current = Load(client, urls, emailId, args.Language, args.LanguageId);
			if (!current.Success) {
				return EmailTemplateUpdateResponse.Failure(current.Error);
			}
			EmailTemplateContentVariant variant = FindVariant(current, args, format);
			string currentChecksum = variant?.Checksum ?? AbsentChecksum(format, args.Language, args.LanguageId);
			if (!string.Equals(currentChecksum, args.ExpectedChecksum.Trim(), StringComparison.OrdinalIgnoreCase)) {
				return EmailTemplateUpdateResponse.Failure(
					$"Email content changed after it was read. Expected checksum '{args.ExpectedChecksum}', current checksum '{currentChecksum}'. Read again and reapply the edit.");
			}
			EmailTemplateUpdateResponse written = format == BeefreeFormat
				? UpdateBeefree(client, urls, emailId, args, variant)
				: UpdateLegacy(client, urls, current.HostType, emailId, args, variant);
			return written.Success
				? WithPersistedChecksum(client, urls, emailId, args, format, written)
				: written;
		} catch (Exception ex) {
			return EmailTemplateUpdateResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}

	private (IApplicationClient Client, IServiceUrlBuilder Urls) Resolve(string environmentName) {
		EnvironmentOptions options = new() { Environment = environmentName };
		return (commandResolver.Resolve<IApplicationClient>(options), commandResolver.Resolve<IServiceUrlBuilder>(options));
	}

	private static EmailTemplateContentResponse Load(
		IApplicationClient client, IServiceUrlBuilder urls, Guid emailId, string requestedLanguage, string requestedLanguageId) {
		JsonElement bulkEmail = First(Read(client, urls, "BulkEmail",
			$"Id eq {emailId:D}", "Id,Name,TemplateSubject,TemplateBody,TemplateConfig", 1));
		JsonElement messageTemplate = First(Read(client, urls, "EmailTemplate",
			$"Id eq {emailId:D}", "Id,Name,Subject,Body,TemplateConfig,ConfigType,IsHtmlBody", 1));
		string hostType = ResolveHostType(bulkEmail, messageTemplate);
		if (hostType is null) {
			return EmailTemplateContentResponse.Failure(
				$"No BulkEmail or EmailTemplate host record exists with Id {emailId:D}.");
		}
		JsonElement host = hostType == BulkEmailHost ? bulkEmail : messageTemplate;
		List<EmailTemplateContentVariant> variants = ReadBeefreeVariants(client, urls, emailId, requestedLanguage);
		if (hostType == BulkEmailHost) {
			if (!string.IsNullOrWhiteSpace(requestedLanguageId)) {
				return EmailTemplateContentResponse.Failure(
					"language-id is supported only for EmailTemplate message-template hosts.");
			}
			variants.Add(LegacyVariant(host, languageId: null));
		} else {
			AddLegacyVariants(client, urls, emailId, host, requestedLanguageId, variants);
		}
		return new EmailTemplateContentResponse(
			true, null, emailId.ToString("D"), hostType, String(host, "Name"), variants);
	}

	private static string ResolveHostType(JsonElement bulkEmail, JsonElement messageTemplate) {
		if (bulkEmail.ValueKind == JsonValueKind.Object) {
			return BulkEmailHost;
		}
		return messageTemplate.ValueKind == JsonValueKind.Object ? MessageTemplateHost : null;
	}

	/// <summary>
	/// Reads every beefree variant of the email and guarantees the requested language is represented,
	/// adding an absent-variant placeholder when the environment holds no row for it.
	/// </summary>
	private static List<EmailTemplateContentVariant> ReadBeefreeVariants(
		IApplicationClient client, IServiceUrlBuilder urls, Guid emailId, string requestedLanguage) {
		List<EmailTemplateContentVariant> variants = [];
		foreach (JsonElement row in Read(client, urls, "BfEmailTemplate", $"EmailId eq {emailId:D}",
			"Id,EmailId,Language,TemplateLanguageId,PageJson,PageHtml,AmpHtml,TemplateVersion,IsDefault", 100)) {
			variants.Add(BeefreeVariant(row));
		}
		// An omitted language means "the one this email sends by default", which is the IsDefault row - not
		// necessarily the row whose Language is empty. Reading it as the empty language reported exists=false
		// for an email that has content, and an update against that placeholder created a second default row.
		string beefreeLanguage = requestedLanguage ?? DefaultBeefreeLanguage(variants);
		if (!variants.Any(variant => variant.Format == BeefreeFormat
				&& string.Equals(variant.Language ?? string.Empty, beefreeLanguage, StringComparison.OrdinalIgnoreCase))) {
			variants.Add(AbsentBeefreeVariant(beefreeLanguage));
		}
		return variants;
	}

	/// <summary>
	/// Language of the beefree variant the email sends when the caller names none: the row flagged
	/// <c>IsDefault</c>, falling back to the empty language when no row claims the flag.
	/// </summary>
	private static string DefaultBeefreeLanguage(IEnumerable<EmailTemplateContentVariant> variants) =>
		variants.FirstOrDefault(variant => variant.Format == BeefreeFormat && variant.IsDefault == true)
			?.Language ?? string.Empty;

	/// <summary>
	/// Appends the primary legacy variant and every EmailTemplateLang translation of a message-template
	/// host, plus an absent-variant placeholder when the requested language id has no row yet.
	/// </summary>
	private static void AddLegacyVariants(
		IApplicationClient client, IServiceUrlBuilder urls, Guid emailId, JsonElement host,
		string requestedLanguageId, List<EmailTemplateContentVariant> variants) {
		variants.Add(LegacyVariant(host, languageId: null));
		foreach (JsonElement row in Read(client, urls, "EmailTemplateLang", $"EmailTemplateId eq {emailId:D}",
			"Id,EmailTemplateId,LanguageId,Subject,Body,TemplateConfig,IsHtmlBody", 100)) {
			variants.Add(LegacyVariant(row, String(row, "LanguageId")));
		}
		string normalizedLanguageId = NormalizeGuid(requestedLanguageId);
		if (!string.IsNullOrEmpty(normalizedLanguageId) && !variants.Any(variant =>
				variant.Format == LegacyFormat
				&& string.Equals(NormalizeGuid(variant.LanguageId), normalizedLanguageId,
					StringComparison.OrdinalIgnoreCase))) {
			variants.Add(AbsentLegacyVariant(normalizedLanguageId));
		}
	}

	private static EmailTemplateContentVariant FindVariant(
		EmailTemplateContentResponse current, EmailTemplateUpdateArgs args, string format) {
		if (format == BeefreeFormat) {
			// Same resolution the read used, so an update with no language edits the default row instead of
			// creating a second one alongside it.
			string language = args.Language ?? DefaultBeefreeLanguage(current.Variants);
			return current.Variants.FirstOrDefault(v => v.Format == BeefreeFormat
				&& string.Equals(v.Language ?? string.Empty, language, StringComparison.OrdinalIgnoreCase));
		}
		string languageId = NormalizeGuid(args.LanguageId);
		return current.Variants.FirstOrDefault(v => v.Format == LegacyFormat
			&& string.Equals(NormalizeGuid(v.LanguageId), languageId, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Replaces the receipt with a digest of the row Creatio actually stored, read back through the same
	/// path the next <c>get-email-template</c> uses. Hashing the request values instead cannot be
	/// reproduced by that read: a create leaves every column the caller omitted at the platform default
	/// (<c>''</c> for text, <c>false</c> for boolean) rather than null, and a PATCH may coerce what it
	/// persisted, so the very next guarded update would be refused as a concurrent change.
	/// </summary>
	private static EmailTemplateUpdateResponse WithPersistedChecksum(
		IApplicationClient client, IServiceUrlBuilder urls, Guid emailId, EmailTemplateUpdateArgs args,
		string format, EmailTemplateUpdateResponse written) {
		EmailTemplateContentResponse persisted = Load(client, urls, emailId, args.Language, args.LanguageId);
		EmailTemplateContentVariant variant = persisted.Success ? FindVariant(persisted, args, format) : null;
		if (variant is null || !variant.Exists) {
			// The write itself succeeded, so reporting it as unwritten would invite a duplicate. Withholding
			// the receipt is the honest answer: the caller must read again before the next guarded update.
			return written with {
				Success = false,
				Error = "Email content was written, but its checksum could not be confirmed by reading the row "
					+ "back. Call get-email-template again before the next guarded update.",
				Checksum = null
			};
		}
		return written with { Checksum = variant.Checksum };
	}

	private static EmailTemplateUpdateResponse UpdateBeefree(
		IApplicationClient client, IServiceUrlBuilder urls, Guid emailId, EmailTemplateUpdateArgs args,
		EmailTemplateContentVariant current) {
		if (string.IsNullOrWhiteSpace(args.PageJson) || string.IsNullOrWhiteSpace(args.PageHtml)) {
			return EmailTemplateUpdateResponse.Failure("page-json and page-html are required for format=beefree.");
		}
		// current carries the resolved language even when the row does not exist yet, because the read added
		// its placeholder under that language.
		string language = current?.Language ?? args.Language ?? string.Empty;
		var data = new Dictionary<string, object> {
			["EmailId"] = emailId,
			["Language"] = language,
			["PageJson"] = args.PageJson,
			["PageHtml"] = args.PageHtml,
			["AmpHtml"] = args.AmpHtml ?? current?.AmpHtml ?? string.Empty,
			["TemplateVersion"] = args.TemplateVersion ?? current?.TemplateVersion ?? 0
		};
		bool created = current is null || !current.Exists;
		if (created) {
			// Only on create. Sending it on every update rewrote a column the caller never asked about, and the
			// read could not even show its current value, so the caller had no way to preserve it.
			data["IsDefault"] = string.IsNullOrEmpty(language);
		}
		Write(client, urls, "BfEmailTemplate", created ? null : current.RecordId, data);
		// The receipt is filled in by WithPersistedChecksum from a re-read; hashing the request values here
		// could not be reproduced by the next read.
		return new(true, null, emailId.ToString("D"), BeefreeFormat, created, null);
	}

	private static EmailTemplateUpdateResponse UpdateLegacy(
		IApplicationClient client, IServiceUrlBuilder urls, string hostType, Guid emailId,
		EmailTemplateUpdateArgs args, EmailTemplateContentVariant current) {
		bool translated = !string.IsNullOrWhiteSpace(args.LanguageId);
		string validationError = ValidateLegacyUpdate(hostType, args, translated);
		if (validationError is not null) {
			return EmailTemplateUpdateResponse.Failure(validationError);
		}
		Dictionary<string, object> data = BuildLegacyPayload(hostType, args, translated);
		if (data.Count == 0) {
			// Every requested field was dropped by the host mapping. Issuing the PATCH anyway would send an
			// empty body and report success with a checksum computed from values that were never written.
			return EmailTemplateUpdateResponse.Failure(
				"None of the requested fields apply to this email; no email content was changed.");
		}
		bool created = translated && (current is null || !current.Exists);
		if (created) {
			if (!Guid.TryParse(args.LanguageId, out Guid languageId)) {
				return EmailTemplateUpdateResponse.Failure("language-id must be a GUID.");
			}
			data["EmailTemplateId"] = emailId;
			data["LanguageId"] = languageId;
			Write(client, urls, "EmailTemplateLang", null, data);
		} else {
			Write(client, urls, LegacyEntityName(hostType, translated),
				translated ? current.RecordId : emailId.ToString("D"), data);
		}
		// The receipt is filled in by WithPersistedChecksum from a re-read; hashing the request values here
		// could not be reproduced by the next read.
		return new(true, null, emailId.ToString("D"), LegacyFormat, created, null);
	}

	/// <summary>
	/// Returns the reason a legacy update cannot be applied, or null when the arguments are usable.
	/// The order of the checks is the order the caller reported them in before they were extracted.
	/// </summary>
	private static string ValidateLegacyUpdate(string hostType, EmailTemplateUpdateArgs args, bool translated) {
		if (args.Subject is null && args.Body is null && args.TemplateConfig is null
				&& args.ConfigType is null && args.IsHtmlBody is null) {
			return "At least one of subject, body, template-config, config-type, or is-html-body is required for format=legacy.";
		}
		if (translated && args.ConfigType is not null) {
			return "config-type is supported only for the primary EmailTemplate variant, not EmailTemplateLang translations.";
		}
		if (translated && hostType != MessageTemplateHost) {
			return "language-id is supported only for EmailTemplate message-template hosts.";
		}
		// BuildLegacyPayload maps ConfigType and IsHtmlBody only for a message-template host, so on any other
		// host they were silently dropped and the returned checksum was computed from the dropped values.
		if (hostType != MessageTemplateHost && (args.ConfigType is not null || args.IsHtmlBody is not null)) {
			return "config-type and is-html-body are supported only for EmailTemplate message-template hosts.";
		}
		return null;
	}

	/// <summary>
	/// Maps the update arguments onto the column names the host entity uses; BulkEmail carries the
	/// subject and body under Template-prefixed columns, EmailTemplate under the plain ones.
	/// </summary>
	private static Dictionary<string, object> BuildLegacyPayload(
		string hostType, EmailTemplateUpdateArgs args, bool translated) {
		var data = new Dictionary<string, object>();
		bool bulkEmailHost = hostType == BulkEmailHost;
		Add(data, bulkEmailHost ? "TemplateSubject" : "Subject", args.Subject);
		Add(data, bulkEmailHost ? "TemplateBody" : "Body", args.Body);
		Add(data, "TemplateConfig", args.TemplateConfig);
		if (hostType == MessageTemplateHost) {
			if (!translated) {
				Add(data, "ConfigType", args.ConfigType);
			}
			Add(data, "IsHtmlBody", args.IsHtmlBody);
		}
		return data;
	}

	private static string LegacyEntityName(string hostType, bool translated) {
		if (translated) {
			return "EmailTemplateLang";
		}
		return hostType == BulkEmailHost ? "BulkEmail" : "EmailTemplate";
	}

	private static void Add(IDictionary<string, object> data, string name, object value) {
		if (value is not null) {
			data[name] = value;
		}
	}

	private static void Write(
		IApplicationClient client, IServiceUrlBuilder urls, string entity, string recordId,
		IReadOnlyDictionary<string, object> data) {
		string payload = JsonSerializer.Serialize(data);
		string response = string.IsNullOrWhiteSpace(recordId)
			? client.ExecutePostRequest(urls.Build(ODataKeyFormatter.CollectionPath(entity)), payload, RequestTimeout)
			: client.ExecutePatchRequest(urls.Build(ODataKeyFormatter.KeyPath(entity, recordId)), payload, RequestTimeout);
		if (!string.IsNullOrWhiteSpace(response)) {
			using JsonDocument document = JsonDocument.Parse(response);
			if (CreatioResponseError.TryDetect(document.RootElement, CreatioResponseContext.ODataPayload, out string error)) {
				throw new InvalidOperationException(error);
			}
		}
	}

	private static IReadOnlyList<JsonElement> Read(
		IApplicationClient client, IServiceUrlBuilder urls, string entity, string filter, string select, int top) {
		string path = $"odata/{entity}?$filter={Uri.EscapeDataString(filter)}&$select={select}&$top={top}";
		string json = client.ExecuteGetRequest(urls.Build(path), RequestTimeout);
		if (string.IsNullOrWhiteSpace(json)) {
			throw new InvalidOperationException($"Creatio OData {entity} response was empty.");
		}
		using JsonDocument document = JsonDocument.Parse(json);
		if (CreatioResponseError.TryDetect(document.RootElement, CreatioResponseContext.ODataPayload, out string error)) {
			throw new InvalidOperationException(error);
		}
		if (!document.RootElement.TryGetProperty("value", out JsonElement value)
				|| value.ValueKind != JsonValueKind.Array) {
			throw new InvalidOperationException($"Creatio OData {entity} response did not contain a value array.");
		}
		return value.EnumerateArray().Select(item => item.Clone()).ToArray();
	}

	private static JsonElement First(IReadOnlyList<JsonElement> rows) =>
		rows.Count == 0 ? default : rows[0];

	private static EmailTemplateContentVariant BeefreeVariant(JsonElement row) {
		string language = String(row, "Language") ?? string.Empty;
		string languageId = String(row, "TemplateLanguageId");
		string pageJson = String(row, "PageJson");
		string pageHtml = String(row, "PageHtml");
		string ampHtml = String(row, "AmpHtml");
		int? version = Int(row, "TemplateVersion");
		return new EmailTemplateContentVariant(
			BeefreeFormat, true, String(row, "Id"), language, languageId,
			Hash(BeefreeFormat, language, languageId, pageJson, pageHtml, ampHtml, version?.ToString()),
			pageJson, pageHtml, ampHtml, version, null, null, null, null, null, Bool(row, "IsDefault"));
	}

	private static EmailTemplateContentVariant LegacyVariant(JsonElement row, string languageId) {
		string subject = String(row, "Subject") ?? String(row, "TemplateSubject");
		string body = String(row, "Body") ?? String(row, "TemplateBody");
		string config = String(row, "TemplateConfig");
		int? configType = Int(row, "ConfigType");
		bool? isHtml = Bool(row, "IsHtmlBody");
		return new EmailTemplateContentVariant(
			LegacyFormat, true, String(row, "Id"), null, languageId,
			Hash(LegacyFormat, null, NormalizeGuid(languageId), subject, body, config, configType?.ToString(),
				isHtml?.ToString()),
			null, null, null, null, subject, body, config, configType, isHtml, null);
	}

	private static EmailTemplateContentVariant AbsentBeefreeVariant(string language) =>
		new(BeefreeFormat, false, null, language, null,
			AbsentChecksum(BeefreeFormat, language, null),
			null, null, null, null, null, null, null, null, null, null);

	private static EmailTemplateContentVariant AbsentLegacyVariant(string languageId) =>
		new(LegacyFormat, false, null, null, languageId,
			AbsentChecksum(LegacyFormat, null, languageId),
			null, null, null, null, null, null, null, null, null, null);

	// The legacy language-id slot is always normalized, so the digest a write returns matches the digest the
	// next read produces: an omitted language-id and a null one are the same primary variant, and a GUID
	// written in any casing or format is the same translation.
	private static string AbsentChecksum(string format, string language, string languageId) =>
		Hash(format, language, format == LegacyFormat ? NormalizeGuid(languageId) : languageId, "<absent>");

	private static string Hash(params string[] values) {
		var builder = new StringBuilder();
		foreach (string value in values) {
			string normalized = value ?? "<null>";
			builder.Append(normalized.Length).Append(':').Append(normalized).Append('|');
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}

	private static string NormalizeGuid(string value) =>
		Guid.TryParse(value, out Guid id) ? id.ToString("D") : string.Empty;

	private static string String(JsonElement row, string name) =>
		row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out JsonElement value)
			&& value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static int? Int(JsonElement row, string name) =>
		row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out JsonElement value)
			&& value.TryGetInt32(out int result) ? result : null;

	private static bool? Bool(JsonElement row, string name) =>
		row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out JsonElement value)
			&& value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
