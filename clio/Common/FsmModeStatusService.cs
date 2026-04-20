using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.UserEnvironment;

namespace Clio.Common;

/// <summary>
/// Resolves the current file system mode state for a registered Creatio environment.
/// </summary>
public interface IFsmModeStatusService
{
	/// <summary>
	/// Gets the current FSM mode for the specified registered environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <returns>Structured FSM mode status.</returns>
	FsmModeStatusResult GetStatus(string environmentName);
}

/// <summary>
/// Queries Creatio application info and derives FSM mode from the response payload.
/// </summary>
public sealed class FsmModeStatusService : IFsmModeStatusService
{
	private readonly ISettingsRepository _settingsRepository;
	private readonly IApplicationClientFactory _applicationClientFactory;
	private readonly IServiceUrlBuilderFactory _serviceUrlBuilderFactory;

	/// <summary>
	/// Initializes a new instance of the <see cref="FsmModeStatusService"/> class.
	/// </summary>
	/// <param name="settingsRepository">Repository used to resolve registered environments.</param>
	/// <param name="applicationClientFactory">Factory used to create environment clients.</param>
	/// <param name="serviceUrlBuilderFactory">Factory used to create service URL builders for runtime-resolved environments.</param>
	public FsmModeStatusService(
		ISettingsRepository settingsRepository,
		IApplicationClientFactory applicationClientFactory,
		IServiceUrlBuilderFactory serviceUrlBuilderFactory)
	{
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_applicationClientFactory = applicationClientFactory ?? throw new ArgumentNullException(nameof(applicationClientFactory));
		_serviceUrlBuilderFactory = serviceUrlBuilderFactory ?? throw new ArgumentNullException(nameof(serviceUrlBuilderFactory));
	}

	/// <inheritdoc />
	public FsmModeStatusResult GetStatus(string environmentName)
	{
		if (string.IsNullOrWhiteSpace(environmentName))
		{
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		EnvironmentSettings environmentSettings = _settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");

		IApplicationClient client = _applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		string requestUrl = _serviceUrlBuilderFactory.Create(environmentSettings)
			.Build(ServiceUrlBuilder.KnownRoute.GetApplicationInfo);
		string response = client.ExecutePostRequest(requestUrl, string.Empty);

		if (string.IsNullOrWhiteSpace(response))
		{
			throw new InvalidOperationException("GetApplicationInfo returned an empty response.");
		}

		using JsonDocument document = JsonDocument.Parse(response);
		JsonElement applicationInfoElement = GetApplicationInfoElement(document.RootElement);

		if (!TryGetProperty(applicationInfoElement, "useStaticFileContent", out JsonElement useStaticFileContentElement))
		{
			throw new InvalidOperationException("GetApplicationInfo response does not contain 'useStaticFileContent'.");
		}

		if (!TryGetProperty(applicationInfoElement, "staticFileContent", out JsonElement staticFileContentElement))
		{
			throw new InvalidOperationException("GetApplicationInfo response does not contain 'staticFileContent'.");
		}

		if (useStaticFileContentElement.ValueKind != JsonValueKind.True &&
			useStaticFileContentElement.ValueKind != JsonValueKind.False)
		{
			throw new InvalidOperationException(
				"'useStaticFileContent' must be a boolean value in GetApplicationInfo response.");
		}

		bool useStaticFileContent = useStaticFileContentElement.GetBoolean();
		FsmMode mode = DetectMode(useStaticFileContent, staticFileContentElement);

		return new FsmModeStatusResult(
			environmentName,
			mode.ToString().ToLowerInvariant(),
			useStaticFileContent,
			staticFileContentElement.ValueKind == JsonValueKind.Null
				? null
				: JsonSerializer.Deserialize<StaticFileContentInfo>(staticFileContentElement.GetRawText(), JsonOptions));
	}

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private static FsmMode DetectMode(bool useStaticFileContent, JsonElement staticFileContentElement)
	{
		bool hasPopulatedStaticFileContentObject = TryDeserializeStaticFileContent(
			staticFileContentElement,
			out StaticFileContentInfo? staticFileContent);
		bool isStaticFileContentNull = staticFileContentElement.ValueKind == JsonValueKind.Null;

		if (!useStaticFileContent && isStaticFileContentNull)
		{
			return FsmMode.On;
		}

		if (useStaticFileContent && hasPopulatedStaticFileContentObject)
		{
			return FsmMode.Off;
		}

		throw new InvalidOperationException(
			"Could not determine FSM mode from GetApplicationInfo response. " +
			"Expected either useStaticFileContent=false with staticFileContent=null or " +
			"useStaticFileContent=true with staticFileContent populated.");
	}

	private static bool TryDeserializeStaticFileContent(
		JsonElement staticFileContentElement,
		out StaticFileContentInfo? staticFileContent)
	{
		staticFileContent = null;
		if (staticFileContentElement.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		staticFileContent = JsonSerializer.Deserialize<StaticFileContentInfo>(
			staticFileContentElement.GetRawText(),
			JsonOptions);
		if (staticFileContent is null)
		{
			return false;
		}

		return !string.IsNullOrWhiteSpace(staticFileContent.SchemasRuntimePath) ||
			!string.IsNullOrWhiteSpace(staticFileContent.ResourcesRuntimePath);
	}

	private static JsonElement GetApplicationInfoElement(JsonElement rootElement)
	{
		List<JsonElement> candidates = [];
		CollectApplicationInfoCandidates(rootElement, candidates);
		if (candidates.Count == 1)
		{
			return candidates[0];
		}

		if (candidates.Count == 0)
		{
			throw new InvalidOperationException(
				"GetApplicationInfo response does not contain a canonical payload with both 'useStaticFileContent' and 'staticFileContent'.");
		}

		throw new InvalidOperationException(
			"GetApplicationInfo response contains multiple payload candidates with 'useStaticFileContent' and 'staticFileContent'.");
	}

	private static void CollectApplicationInfoCandidates(JsonElement element, ICollection<JsonElement> candidates)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			if (TryGetProperty(element, "useStaticFileContent", out _) &&
				TryGetProperty(element, "staticFileContent", out _))
			{
				candidates.Add(element);
			}

			foreach (JsonProperty property in element.EnumerateObject())
			{
				CollectApplicationInfoCandidates(property.Value, candidates);
			}
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				CollectApplicationInfoCandidates(item, candidates);
			}
		}
	}

	private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			value = default;
			return false;
		}

		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		value = default;
		return false;
	}
}

/// <summary>
/// Structured FSM status returned to MCP clients.
/// </summary>
/// <param name="EnvironmentName">Registered environment name.</param>
/// <param name="Mode">Derived FSM mode value: on or off.</param>
/// <param name="UseStaticFileContent">Raw useStaticFileContent flag from GetApplicationInfo.</param>
/// <param name="StaticFileContent">Raw staticFileContent payload from GetApplicationInfo.</param>
public sealed record FsmModeStatusResult(
	string EnvironmentName,
	string Mode,
	bool UseStaticFileContent,
	StaticFileContentInfo? StaticFileContent);

/// <summary>
/// Static file content paths returned by GetApplicationInfo when FSM mode is off.
/// </summary>
/// <param name="SchemasRuntimePath">Schemas runtime path.</param>
/// <param name="ResourcesRuntimePath">Resources runtime path.</param>
public sealed record StaticFileContentInfo(
	string? SchemasRuntimePath,
	string? ResourcesRuntimePath);

/// <summary>
/// Supported FSM mode values.
/// </summary>
public enum FsmMode
{
	/// <summary>
	/// File system mode is enabled.
	/// </summary>
	On,

	/// <summary>
	/// File system mode is disabled.
	/// </summary>
	Off
}
