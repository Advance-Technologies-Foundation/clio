using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using CommandLine;
using ConsoleTables;

namespace Clio.Command;

[Verb("idp-list", HelpText = "List identity providers")]
public sealed class IdentityProviderListOptions : RemoteCommandOptions {
	[Option("json", Required = false, Default = false, HelpText = "Output as indented JSON instead of a table")]
	public bool JsonFormat { get; set; }
}

[Verb("idp-upsert", HelpText = "Create or update an identity provider")]
public sealed class IdentityProviderUpsertOptions : RemoteCommandOptions {
	[Option("id", Required = false, HelpText = "Identity provider ID to update")]
	public string Id { get; set; }

	[Option("name", Required = true, HelpText = "Identity provider name")]
	public string Name { get; set; }

	[Option("description", Required = false, HelpText = "Identity provider description")]
	public string Description { get; set; }

	[Option("server-url", Required = true, HelpText = "Identity provider server URL")]
	public string ServerUrl { get; set; }

	[Option("client-id", Required = true, HelpText = "OAuth client ID")]
	public string ClientId { get; set; }

	[Option("client-secret", Required = false, HelpText = "OAuth client secret")]
	public string ClientSecret { get; set; }
}

[Verb("idp-set-secret", HelpText = "Set identity provider client secret")]
public sealed class IdentityProviderSetSecretOptions : RemoteCommandOptions {
	[Option("id", Required = false, HelpText = "Identity provider ID")]
	public string Id { get; set; }

	[Option("name", Required = false, HelpText = "Identity provider name")]
	public string Name { get; set; }

	[Option("client-secret", Required = true, HelpText = "OAuth client secret")]
	public string ClientSecret { get; set; }
}

[Verb("idp-delete", HelpText = "Delete an identity provider")]
public sealed class IdentityProviderDeleteOptions : RemoteCommandOptions {
	[Option("id", Required = false, HelpText = "Identity provider ID")]
	public string Id { get; set; }

	[Option("name", Required = false, HelpText = "Identity provider name")]
	public string Name { get; set; }
}

[Verb("idp-set-default", HelpText = "Set the default identity provider")]
public sealed class IdentityProviderSetDefaultOptions : RemoteCommandOptions {
	[Option("id", Required = false, HelpText = "Identity provider ID")]
	public string Id { get; set; }

	[Option("name", Required = false, HelpText = "Identity provider name")]
	public string Name { get; set; }
}

[Verb("idp-bind", HelpText = "Bind an identity provider to an external service code")]
public sealed class IdentityProviderBindOptions : RemoteCommandOptions {
	[Option("provider-id", Required = false, HelpText = "Identity provider ID")]
	public string ProviderId { get; set; }

	[Option("provider-name", Required = false, HelpText = "Identity provider name")]
	public string ProviderName { get; set; }

	[Option("service-code", Required = true, HelpText = "External service or feature code")]
	public string ServiceCode { get; set; }

	[Option("create-service", Required = false, Default = false, HelpText = "Create the external service when missing")]
	public bool CreateService { get; set; }
}

[Verb("idp-unbind", HelpText = "Unbind an identity provider from an external service code")]
public sealed class IdentityProviderUnbindOptions : RemoteCommandOptions {
	[Option("service-code", Required = true, HelpText = "External service or feature code")]
	public string ServiceCode { get; set; }
}

[Verb("idp-services", HelpText = "List external services and identity provider bindings")]
public sealed class IdentityProviderServicesOptions : RemoteCommandOptions {
	[Option("json", Required = false, Default = false, HelpText = "Output as indented JSON instead of a table")]
	public bool JsonFormat { get; set; }
}

public interface IIdentityProviderManagementClient {
	IReadOnlyList<IdentityProviderInfo> GetProviders(int requestTimeout);
	IdentityProviderInfo SaveProvider(IdentityProviderSaveModel request, int requestTimeout);
	void SetProviderCredentials(ProviderSelector selector, string clientSecret, int requestTimeout);
	void DeleteProvider(ProviderSelector selector, int requestTimeout);
	void SetDefaultProvider(ProviderSelector selector, int requestTimeout);
	IReadOnlyList<IdentityProviderServiceInfo> GetServices(int requestTimeout);
	BindingInfo BindProviderToService(BindProviderModel request, int requestTimeout);
	void UnbindProviderFromService(string serviceCode, int requestTimeout);
}

public sealed class IdentityProviderManagementClient(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: IIdentityProviderManagementClient {
	private const string ServiceRoute = "/rest/IdentityProviderManagementService";
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	public IReadOnlyList<IdentityProviderInfo> GetProviders(int requestTimeout) =>
		Post<ProvidersResponse>("GetProviders", new { }, requestTimeout).Providers ?? [];

	public IdentityProviderInfo SaveProvider(IdentityProviderSaveModel request, int requestTimeout) {
		ArgumentNullException.ThrowIfNull(request);
		ProviderResponse response = Post<ProviderResponse>("SaveProvider", request, requestTimeout);
		return response.Provider ?? throw new InvalidOperationException("Identity provider service returned an empty provider.");
	}

	public void SetProviderCredentials(ProviderSelector selector, string clientSecret, int requestTimeout) {
		ArgumentNullException.ThrowIfNull(selector);
		Post<IdentityProviderServiceResponse>(
			"SetProviderCredentials",
			new ProviderCredentialsRequest(selector.Id, selector.Name, clientSecret),
			requestTimeout);
	}

	public void DeleteProvider(ProviderSelector selector, int requestTimeout) {
		ArgumentNullException.ThrowIfNull(selector);
		Post<IdentityProviderServiceResponse>("DeleteProvider", selector, requestTimeout);
	}

	public void SetDefaultProvider(ProviderSelector selector, int requestTimeout) {
		ArgumentNullException.ThrowIfNull(selector);
		Post<IdentityProviderServiceResponse>("SetDefaultProvider", selector, requestTimeout);
	}

	public IReadOnlyList<IdentityProviderServiceInfo> GetServices(int requestTimeout) =>
		Post<ServicesResponse>("GetServices", new { }, requestTimeout).Services ?? [];

	public BindingInfo BindProviderToService(BindProviderModel request, int requestTimeout) {
		ArgumentNullException.ThrowIfNull(request);
		BindingResponse response = Post<BindingResponse>("BindProviderToService", request, requestTimeout);
		return response.Binding ?? new BindingInfo(request.ServiceCode, request.ProviderId, null);
	}

	public void UnbindProviderFromService(string serviceCode, int requestTimeout) =>
		Post<IdentityProviderServiceResponse>(
			"UnbindProviderFromService",
			new ServiceCodeRequest(serviceCode),
			requestTimeout);

	private TResponse Post<TResponse>(string methodName, object request, int requestTimeout)
		where TResponse : IdentityProviderServiceResponse {
		string url = serviceUrlBuilder.Build($"{ServiceRoute}/{methodName}");
		string responseBody = applicationClient.ExecutePostRequest(
			url,
			JsonSerializer.Serialize(request, JsonOptions),
			requestTimeout);
		TResponse response = DeserializeResponse<TResponse>(responseBody);
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? $"Identity provider endpoint '{methodName}' failed.");
		}
		return response;
	}

	private static TResponse DeserializeResponse<TResponse>(string responseBody)
		where TResponse : IdentityProviderServiceResponse {
		if (string.IsNullOrWhiteSpace(responseBody)) {
			throw new InvalidOperationException("Identity provider service returned an empty response.");
		}
		using JsonDocument document = JsonDocument.Parse(responseBody);
		JsonElement root = document.RootElement;
		if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out JsonElement wrappedResult)) {
			root = wrappedResult;
		}
		return JsonSerializer.Deserialize<TResponse>(root.GetRawText(), JsonOptions)
			?? throw new InvalidOperationException("Identity provider service returned an invalid response.");
	}
}

public sealed class IdentityProviderListCommand(
	IIdentityProviderManagementClient client,
	ILogger logger)
	: Command<IdentityProviderListOptions> {
	private static readonly JsonSerializerOptions WriteIndentedOptions = IdentityProviderCommandJson.WriteIndentedOptions;

	public override int Execute(IdentityProviderListOptions options) =>
		IdentityProviderCommandExecutor.Execute(logger, () => {
			IReadOnlyList<IdentityProviderInfo> providers = client.GetProviders(options.TimeOut);
			if (options.JsonFormat) {
				logger.WriteInfo(JsonSerializer.Serialize(providers, WriteIndentedOptions));
				return;
			}
			ConsoleTable table = new("Id", "Name", "ServerUrl", "ClientId", "Default");
			foreach (IdentityProviderInfo provider in providers) {
				table.AddRow(provider.Id, provider.Name, provider.ServerUrl, provider.ClientId, provider.IsDefault);
			}
			logger.PrintTable(table);
		});
}

public sealed class IdentityProviderUpsertCommand(
	IIdentityProviderManagementClient client,
	ILogger logger)
	: Command<IdentityProviderUpsertOptions> {
	public override int Execute(IdentityProviderUpsertOptions options) =>
		IdentityProviderCommandExecutor.Execute(logger, () => {
			IdentityProviderInfo provider = client.SaveProvider(
				new IdentityProviderSaveModel(
					options.Id,
					options.Name,
					options.Description,
					options.ServerUrl,
					options.ClientId,
					options.ClientSecret),
				options.TimeOut);
			logger.WriteInfo($"Identity provider saved: {provider.Id} {provider.Name}");
		});
}

public sealed class IdentityProviderSetSecretCommand(
	IIdentityProviderManagementClient client,
	ILogger logger)
	: Command<IdentityProviderSetSecretOptions> {
	public override int Execute(IdentityProviderSetSecretOptions options) =>
		IdentityProviderCommandExecutor.Execute(logger, () => {
			ProviderSelector selector = IdentityProviderCommandExecutor.CreateSelector(options.Id, options.Name);
			client.SetProviderCredentials(selector, options.ClientSecret, options.TimeOut);
			logger.WriteInfo("Identity provider secret updated.");
		});
}

public sealed class IdentityProviderDeleteCommand(
	IIdentityProviderManagementClient client,
	ILogger logger)
	: Command<IdentityProviderDeleteOptions> {
	public override int Execute(IdentityProviderDeleteOptions options) =>
		IdentityProviderCommandExecutor.Execute(logger, () => {
			ProviderSelector selector = IdentityProviderCommandExecutor.CreateSelector(options.Id, options.Name);
			client.DeleteProvider(selector, options.TimeOut);
			logger.WriteInfo("Identity provider deleted.");
		});
}

public sealed class IdentityProviderSetDefaultCommand(
	IIdentityProviderManagementClient client,
	ILogger logger)
	: Command<IdentityProviderSetDefaultOptions> {
	public override int Execute(IdentityProviderSetDefaultOptions options) =>
		IdentityProviderCommandExecutor.Execute(logger, () => {
			ProviderSelector selector = IdentityProviderCommandExecutor.CreateSelector(options.Id, options.Name);
			client.SetDefaultProvider(selector, options.TimeOut);
			logger.WriteInfo("Default identity provider updated.");
		});
}

public sealed class IdentityProviderBindCommand(
	IIdentityProviderManagementClient client,
	ILogger logger)
	: Command<IdentityProviderBindOptions> {
	public override int Execute(IdentityProviderBindOptions options) =>
		IdentityProviderCommandExecutor.Execute(logger, () => {
			ProviderSelector selector = IdentityProviderCommandExecutor.CreateSelector(options.ProviderId, options.ProviderName);
			BindingInfo binding = client.BindProviderToService(
				new BindProviderModel(selector.Id, selector.Name, options.ServiceCode, options.CreateService),
				options.TimeOut);
			logger.WriteInfo($"Identity provider bound to service: {binding.ServiceCode}");
		});
}

public sealed class IdentityProviderUnbindCommand(
	IIdentityProviderManagementClient client,
	ILogger logger)
	: Command<IdentityProviderUnbindOptions> {
	public override int Execute(IdentityProviderUnbindOptions options) =>
		IdentityProviderCommandExecutor.Execute(logger, () => {
			client.UnbindProviderFromService(options.ServiceCode, options.TimeOut);
			logger.WriteInfo($"Identity provider unbound from service: {options.ServiceCode}");
		});
}

public sealed class IdentityProviderServicesCommand(
	IIdentityProviderManagementClient client,
	ILogger logger)
	: Command<IdentityProviderServicesOptions> {
	private static readonly JsonSerializerOptions WriteIndentedOptions = IdentityProviderCommandJson.WriteIndentedOptions;

	public override int Execute(IdentityProviderServicesOptions options) =>
		IdentityProviderCommandExecutor.Execute(logger, () => {
			IReadOnlyList<IdentityProviderServiceInfo> services = client.GetServices(options.TimeOut);
			if (options.JsonFormat) {
				logger.WriteInfo(JsonSerializer.Serialize(services, WriteIndentedOptions));
				return;
			}
			ConsoleTable table = new("Id", "Code", "Name", "ProviderId", "ProviderName");
			foreach (IdentityProviderServiceInfo service in services) {
				table.AddRow(
					service.Id,
					service.Code,
					service.Name,
					service.BoundProviderId ?? string.Empty,
					service.BoundProviderName ?? string.Empty);
			}
			logger.PrintTable(table);
		});
}

internal static class IdentityProviderCommandExecutor {
	public static int Execute(ILogger logger, Action action) {
		try {
			action();
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	public static ProviderSelector CreateSelector(string id, string name) {
		bool hasId = !string.IsNullOrWhiteSpace(id);
		bool hasName = !string.IsNullOrWhiteSpace(name);
		if (hasId == hasName) {
			throw new InvalidOperationException("Specify exactly one provider selector: --id or --name.");
		}
		return new ProviderSelector(hasId ? id : null, hasName ? name : null);
	}
}

internal static class IdentityProviderCommandJson {
	public static readonly JsonSerializerOptions WriteIndentedOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
}

public sealed record IdentityProviderInfo(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("description")] string Description,
	[property: JsonPropertyName("serverUrl")] string ServerUrl,
	[property: JsonPropertyName("clientId")] string ClientId,
	[property: JsonPropertyName("isDefault")] bool IsDefault);

public sealed record IdentityProviderServiceInfo(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("boundProviderId")] string BoundProviderId,
	[property: JsonPropertyName("boundProviderName")] string BoundProviderName);

public sealed record BindingInfo(
	[property: JsonPropertyName("serviceCode")] string ServiceCode,
	[property: JsonPropertyName("providerId")] string ProviderId,
	[property: JsonPropertyName("serviceId")] string ServiceId);

public sealed record ProviderSelector(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("name")] string Name);

public sealed record IdentityProviderSaveModel(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("description")] string Description,
	[property: JsonPropertyName("serverUrl")] string ServerUrl,
	[property: JsonPropertyName("clientId")] string ClientId,
	[property: JsonPropertyName("clientSecret")] string ClientSecret);

public sealed record BindProviderModel(
	[property: JsonPropertyName("providerId")] string ProviderId,
	[property: JsonPropertyName("providerName")] string ProviderName,
	[property: JsonPropertyName("serviceCode")] string ServiceCode,
	[property: JsonPropertyName("createServiceIfMissing")] bool CreateServiceIfMissing);

internal sealed record ProviderCredentialsRequest(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("clientSecret")] string ClientSecret);

internal sealed record ServiceCodeRequest(
	[property: JsonPropertyName("serviceCode")] string ServiceCode);

public class IdentityProviderServiceResponse {
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("errorInfo")]
	public IdentityProviderServiceErrorInfo ErrorInfo { get; set; }
}

public sealed class IdentityProviderServiceErrorInfo {
	[JsonPropertyName("message")]
	public string Message { get; set; }

	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; set; }
}

public sealed class ProvidersResponse : IdentityProviderServiceResponse {
	[JsonPropertyName("providers")]
	public List<IdentityProviderInfo> Providers { get; set; } = [];
}

public sealed class ProviderResponse : IdentityProviderServiceResponse {
	[JsonPropertyName("provider")]
	public IdentityProviderInfo Provider { get; set; }
}

public sealed class ServicesResponse : IdentityProviderServiceResponse {
	[JsonPropertyName("services")]
	public List<IdentityProviderServiceInfo> Services { get; set; } = [];
}

public sealed class BindingResponse : IdentityProviderServiceResponse {
	[JsonPropertyName("binding")]
	public BindingInfo Binding { get; set; }
}
