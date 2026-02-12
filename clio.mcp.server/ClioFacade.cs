using Autofac;
using Clio;
using Clio.Command;
using Clio.Common;
using Clio.Query;
using Clio.UserEnvironment;
using System.Text.Json;

namespace Clio.McpServer;

public sealed class ClioFacade {
	private readonly ISettingsRepository _settingsRepository = new SettingsRepository();

	public ToolExecutionResult ListEnvironments(JsonElement args) {
		bool includeSecrets = args.GetBooleanOrDefault("includeSecrets", false);
		string active = _settingsRepository.GetDefaultEnvironmentName();
		Dictionary<string, EnvironmentSettings> environments = _settingsRepository.GetAllEnvironments();
		var items = environments
			.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
			.Select(x => ToEnvironmentDto(x.Key, x.Value, active == x.Key, includeSecrets))
			.ToList();

		return Success($"Loaded {items.Count} environment(s).", new {
			activeEnvironment = active,
			environments = items
		});
	}

	public ToolExecutionResult GetEnvironment(JsonElement args) {
		string? name = args.GetStringOrDefault("name");
		if (string.IsNullOrWhiteSpace(name)) {
			return Error("Argument 'name' is required.");
		}
		if (!_settingsRepository.IsEnvironmentExists(name)) {
			return Error($"Environment '{name}' not found.", new { name });
		}
		bool includeSecrets = args.GetBooleanOrDefault("includeSecrets", false);
		string active = _settingsRepository.GetDefaultEnvironmentName();
		EnvironmentSettings env = _settingsRepository.GetEnvironment(name);
		return Success($"Environment '{name}' loaded.", new {
			environment = ToEnvironmentDto(name, env, active == name, includeSecrets)
		});
	}

	public ToolExecutionResult SetActiveEnvironment(JsonElement args) {
		string? name = args.GetStringOrDefault("name");
		if (string.IsNullOrWhiteSpace(name)) {
			return Error("Argument 'name' is required.");
		}
		if (!_settingsRepository.IsEnvironmentExists(name)) {
			return Error($"Environment '{name}' not found.", new { name });
		}
		_settingsRepository.SetActiveEnvironment(name);
		return Success($"Environment '{name}' is active now.", new { activeEnvironment = name });
	}

	public ToolExecutionResult UpsertEnvironment(JsonElement args) {
		string? name = args.GetStringOrDefault("name");
		if (string.IsNullOrWhiteSpace(name)) {
			return Error("Argument 'name' is required.");
		}

		EnvironmentSettings update = new();
		if (args.TryGetProperty("uri", out JsonElement uri) && uri.ValueKind == JsonValueKind.String) {
			update.Uri = uri.GetString();
		}
		if (args.TryGetProperty("login", out JsonElement login) && login.ValueKind == JsonValueKind.String) {
			update.Login = login.GetString();
		}
		if (args.TryGetProperty("password", out JsonElement password) && password.ValueKind == JsonValueKind.String) {
			update.Password = password.GetString();
		}
		if (args.TryGetProperty("isNetCore", out JsonElement isNetCore) && isNetCore.ValueKind is JsonValueKind.True or JsonValueKind.False) {
			update.IsNetCore = isNetCore.GetBoolean();
		}
		if (args.TryGetProperty("maintainer", out JsonElement maintainer) && maintainer.ValueKind == JsonValueKind.String) {
			update.Maintainer = maintainer.GetString();
		}
		if (args.TryGetProperty("clientId", out JsonElement clientId) && clientId.ValueKind == JsonValueKind.String) {
			update.ClientId = clientId.GetString();
		}
		if (args.TryGetProperty("clientSecret", out JsonElement clientSecret) && clientSecret.ValueKind == JsonValueKind.String) {
			update.ClientSecret = clientSecret.GetString();
		}
		if (args.TryGetProperty("authAppUri", out JsonElement authAppUri) && authAppUri.ValueKind == JsonValueKind.String) {
			update.AuthAppUri = authAppUri.GetString();
		}
		if (args.TryGetProperty("workspacePathes", out JsonElement workspacePathes) && workspacePathes.ValueKind == JsonValueKind.String) {
			update.WorkspacePathes = workspacePathes.GetString();
		}
		if (args.TryGetProperty("environmentPath", out JsonElement environmentPath) && environmentPath.ValueKind == JsonValueKind.String) {
			update.EnvironmentPath = environmentPath.GetString() ?? string.Empty;
		}

		_settingsRepository.ConfigureEnvironment(name, update);
		bool setActive = args.GetBooleanOrDefault("setActive", false);
		if (setActive) {
			_settingsRepository.SetActiveEnvironment(name);
		}

		string active = _settingsRepository.GetDefaultEnvironmentName();
		EnvironmentSettings result = _settingsRepository.GetEnvironment(name);
		return Success($"Environment '{name}' has been saved.", new {
			environment = ToEnvironmentDto(name, result, active == name, includeSecrets: false),
			activeEnvironment = active
		});
	}

	public ToolExecutionResult Ping(JsonElement args) {
		PingAppOptions options = new();
		ApplyEnvironmentOptions(options, args);
		options.Endpoint = args.GetStringOrDefault("endpoint") ?? "/ping";

		return RunCommand<PingAppCommand, PingAppOptions>(options);
	}

	public ToolExecutionResult GetInfo(JsonElement args) {
		GetCreatioInfoCommandOptions options = new();
		ApplyEnvironmentOptions(options, args);
		return RunCommand<GetCreatioInfoCommand, GetCreatioInfoCommandOptions>(options);
	}

	public ToolExecutionResult CallService(JsonElement args) {
		string? servicePath = args.GetStringOrDefault("servicePath");
		if (string.IsNullOrWhiteSpace(servicePath)) {
			return Error("Argument 'servicePath' is required.");
		}

		CallServiceCommandOptions options = new() {
			ServicePath = servicePath,
			HttpMethodName = args.GetStringOrDefault("httpMethod") ?? "POST",
			RequestBody = args.GetStringOrDefault("requestBody"),
			IsSilent = false
		};
		ApplyEnvironmentOptions(options, args);

		if (args.TryGetProperty("variables", out JsonElement variablesElement) && variablesElement.ValueKind == JsonValueKind.Array) {
			options.Variables = variablesElement
				.EnumerateArray()
				.Where(x => x.ValueKind == JsonValueKind.String)
				.Select(x => x.GetString() ?? string.Empty)
				.Where(x => !string.IsNullOrWhiteSpace(x))
				.ToList();
		}

		return RunCommand<CallServiceCommand, CallServiceCommandOptions>(options);
	}

	private ToolExecutionResult RunCommand<TCommand, TOptions>(TOptions options)
		where TCommand : Command<TOptions> {
		CapturingLogger logger = new();
		try {
			EnvironmentSettings? settings = options is EnvironmentOptions envOptions
				? _settingsRepository.GetEnvironment(envOptions)
				: null;

			using IContainer container = new BindingsModule().Register(settings, builder => {
				builder.RegisterInstance(logger).As<ILogger>();
			});

			TCommand command = container.Resolve<TCommand>();
			int code = command.Execute(options);

			string message = code == 0 ? "Command executed." : "Command failed.";
			object payload = new {
				exitCode = code,
				logs = logger.Logs,
				errorLogs = logger.ErrorLogs
			};
			return code == 0 ? Success(message, payload) : Error(message, payload);
		}
		catch (Exception ex) {
			logger.WriteError(ex.Message);
			return Error($"Command execution error: {ex.Message}", new {
				logs = logger.Logs,
				errorLogs = logger.ErrorLogs
			});
		}
	}

	private static void ApplyEnvironmentOptions(EnvironmentOptions options, JsonElement args) {
		options.Environment = args.GetStringOrDefault("environment");
		options.Uri = args.GetStringOrDefault("uri");
		options.Login = args.GetStringOrDefault("login");
		options.Password = args.GetStringOrDefault("password");
		options.ClientId = args.GetStringOrDefault("clientId");
		options.ClientSecret = args.GetStringOrDefault("clientSecret");
		options.AuthAppUri = args.GetStringOrDefault("authAppUri");
		if (args.TryGetProperty("isNetCore", out JsonElement isNetCore) && isNetCore.ValueKind is JsonValueKind.True or JsonValueKind.False) {
			options.IsNetCore = isNetCore.GetBoolean();
		}
	}

	private static object ToEnvironmentDto(string name, EnvironmentSettings environment, bool isActive, bool includeSecrets) {
		return new {
			name,
			isActive,
			uri = environment.Uri,
			login = environment.Login,
			password = includeSecrets ? environment.Password : MaskSecret(environment.Password),
			isNetCore = environment.IsNetCore,
			maintainer = environment.Maintainer,
			clientId = environment.ClientId,
			clientSecret = includeSecrets ? environment.ClientSecret : MaskSecret(environment.ClientSecret),
			authAppUri = environment.AuthAppUri,
			workspacePathes = environment.WorkspacePathes,
			environmentPath = environment.EnvironmentPath,
			safe = environment.Safe,
			developerModeEnabled = environment.DeveloperModeEnabled
		};
	}

	private static string? MaskSecret(string? value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return value;
		}
		if (value.Length <= 4) {
			return "****";
		}
		return value[..2] + "***" + value[^2..];
	}

	private static ToolExecutionResult Success(string message, object payload) {
		return new ToolExecutionResult(false, message, payload);
	}

	private static ToolExecutionResult Error(string message, object? payload = null) {
		return new ToolExecutionResult(true, message, payload ?? new {
			status = "error"
		});
	}
}

internal static class JsonElementExtensions {
	public static string? GetStringOrDefault(this JsonElement args, string propertyName) {
		if (args.ValueKind != JsonValueKind.Object) {
			return null;
		}
		if (!args.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String) {
			return null;
		}
		return value.GetString();
	}

	public static bool GetBooleanOrDefault(this JsonElement args, string propertyName, bool defaultValue) {
		if (args.ValueKind != JsonValueKind.Object) {
			return defaultValue;
		}
		if (!args.TryGetProperty(propertyName, out JsonElement value)) {
			return defaultValue;
		}
		return value.ValueKind switch {
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			_ => defaultValue
		};
	}
}
