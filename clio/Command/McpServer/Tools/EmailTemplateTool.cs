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
		"variant (Body/Subject/TemplateConfig), each with a checksum for guarded updates. Use the returned " +
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
		"without converting the content to legacy TemplateConfig. Call get-email-template immediately before this " +
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
	[property: JsonPropertyName("is-html-body")] bool? IsHtmlBody);

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
			return format == BeefreeFormat
				? UpdateBeefree(client, urls, emailId, args, variant)
				: UpdateLegacy(client, urls, current.HostType, emailId, args, variant);
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
			"Id,EmailId,Language,TemplateLanguageId,PageJson,PageHtml,AmpHtml,TemplateVersion", 100)) {
			variants.Add(BeefreeVariant(row));
		}
		string beefreeLanguage = requestedLanguage ?? string.Empty;
		if (!variants.Any(variant => variant.Format == BeefreeFormat
				&& string.Equals(variant.Language ?? string.Empty, beefreeLanguage, StringComparison.OrdinalIgnoreCase))) {
			variants.Add(AbsentBeefreeVariant(beefreeLanguage));
		}
		return variants;
	}

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
			string language = args.Language ?? string.Empty;
			return current.Variants.FirstOrDefault(v => v.Format == BeefreeFormat
				&& string.Equals(v.Language ?? string.Empty, language, StringComparison.OrdinalIgnoreCase));
		}
		string languageId = NormalizeGuid(args.LanguageId);
		return current.Variants.FirstOrDefault(v => v.Format == LegacyFormat
			&& string.Equals(NormalizeGuid(v.LanguageId), languageId, StringComparison.OrdinalIgnoreCase));
	}

	private static EmailTemplateUpdateResponse UpdateBeefree(
		IApplicationClient client, IServiceUrlBuilder urls, Guid emailId, EmailTemplateUpdateArgs args,
		EmailTemplateContentVariant current) {
		if (string.IsNullOrWhiteSpace(args.PageJson) || string.IsNullOrWhiteSpace(args.PageHtml)) {
			return EmailTemplateUpdateResponse.Failure("page-json and page-html are required for format=beefree.");
		}
		var data = new Dictionary<string, object> {
			["EmailId"] = emailId,
			["Language"] = args.Language ?? string.Empty,
			["PageJson"] = args.PageJson,
			["PageHtml"] = args.PageHtml,
			["AmpHtml"] = args.AmpHtml ?? current?.AmpHtml ?? string.Empty,
			["TemplateVersion"] = args.TemplateVersion ?? current?.TemplateVersion ?? 0,
			["IsDefault"] = string.IsNullOrEmpty(args.Language)
		};
		bool created = current is null || !current.Exists;
		Write(client, urls, "BfEmailTemplate", created ? null : current.RecordId, data);
		string checksum = Hash(BeefreeFormat, args.Language, current?.LanguageId, args.PageJson, args.PageHtml,
			data["AmpHtml"]?.ToString(), data["TemplateVersion"]?.ToString());
		return new(true, null, emailId.ToString("D"), BeefreeFormat, created, checksum);
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
		string checksum = Hash(LegacyFormat, null, NormalizeGuid(args.LanguageId),
			args.Subject ?? current?.Subject, args.Body ?? current?.Body,
			args.TemplateConfig ?? current?.TemplateConfig,
			(args.ConfigType ?? current?.ConfigType)?.ToString(),
			(args.IsHtmlBody ?? current?.IsHtmlBody)?.ToString());
		return new(true, null, emailId.ToString("D"), LegacyFormat, created, checksum);
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
			if (ODataResponseError.TryDetect(document.RootElement, out string error)) {
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
		if (ODataResponseError.TryDetect(document.RootElement, out string error)) {
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
			pageJson, pageHtml, ampHtml, version, null, null, null, null, null);
	}

	private static EmailTemplateContentVariant LegacyVariant(JsonElement row, string languageId) {
		string subject = String(row, "Subject") ?? String(row, "TemplateSubject");
		string body = String(row, "Body") ?? String(row, "TemplateBody");
		string config = String(row, "TemplateConfig");
		int? configType = Int(row, "ConfigType");
		bool? isHtml = Bool(row, "IsHtmlBody");
		return new EmailTemplateContentVariant(
			LegacyFormat, true, String(row, "Id"), null, languageId,
			Hash(LegacyFormat, null, languageId, subject, body, config, configType?.ToString(), isHtml?.ToString()),
			null, null, null, null, subject, body, config, configType, isHtml);
	}

	private static EmailTemplateContentVariant AbsentBeefreeVariant(string language) =>
		new(BeefreeFormat, false, null, language, null,
			AbsentChecksum(BeefreeFormat, language, null),
			null, null, null, null, null, null, null, null, null);

	private static EmailTemplateContentVariant AbsentLegacyVariant(string languageId) =>
		new(LegacyFormat, false, null, null, languageId,
			AbsentChecksum(LegacyFormat, null, languageId),
			null, null, null, null, null, null, null, null, null);

	private static string AbsentChecksum(string format, string language, string languageId) =>
		Hash(format, language, languageId, "<absent>");

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
