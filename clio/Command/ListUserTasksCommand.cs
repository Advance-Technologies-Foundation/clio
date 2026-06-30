using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Options for listing the user-facing user tasks (the visual designer palette) available on an environment.
/// Consumed by the MCP <c>list-user-tasks</c> tool, which sets these properties directly.
/// </summary>
[RequiresPackage("clioprocessbuilder", Hint = "This experimental feature requires the clioprocessbuilder package on the target environment.")]
public sealed class ListUserTasksOptions : EnvironmentOptions { }

/// <summary>
/// Reads the catalog of user-facing user tasks from a Creatio environment via the ProcessDesignService package.
/// </summary>
public interface IListUserTasksService {
	/// <summary>
	/// Returns the user tasks registered in the environment's process designer palette.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <returns>The list of available user tasks (name + UId).</returns>
	IReadOnlyList<UserTaskInfoResult> GetUserTasks(string environmentName);
}

/// <summary>
/// Default ProcessDesignService-backed implementation of <see cref="IListUserTasksService"/>.
/// </summary>
public sealed class ListUserTasksService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: IListUserTasksService {
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public IReadOnlyList<UserTaskInfoResult> GetUserTasks(string environmentName) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));
		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ListUserTasks, environmentSettings);
		logger.WriteInfo($"Reading user task catalog from '{environmentName}'...");

		// ProcessDesignService uses BodyStyle=Wrapped: a parameterless operation accepts an empty JSON object.
		string responseBody = client.ExecutePostRequest(url, "{}");
		ListUserTasksResponseEnvelope envelope =
			JsonSerializer.Deserialize<ListUserTasksResponseEnvelope>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("ListUserTasks returned an empty response.");
		ListUserTasksResultDto result = envelope.Result
			?? throw new InvalidOperationException("ListUserTasks returned an unexpected response shape.");
		if (!result.Success) {
			throw new InvalidOperationException(result.ErrorMessage ?? "ListUserTasks failed.");
		}

		return (result.UserTasks ?? [])
			.Select(t => new UserTaskInfoResult(t.Name, t.UId))
			.ToList();
	}

	#region DTOs (wire shape)

	private sealed class ListUserTasksResponseEnvelope {
		[JsonPropertyName("ListUserTasksResult")]
		public ListUserTasksResultDto? Result { get; set; }
	}

	private sealed class ListUserTasksResultDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("userTasks")]
		public List<UserTaskItemDto>? UserTasks { get; set; }

		[JsonPropertyName("errorMessage")]
		public string? ErrorMessage { get; set; }
	}

	private sealed class UserTaskItemDto {
		[JsonPropertyName("name")]
		public string? Name { get; set; }

		[JsonPropertyName("uid")]
		public string? UId { get; set; }
	}

	#endregion
}

/// <summary>
/// Lists the user-facing user tasks of a Creatio environment and prints them to the logger.
/// </summary>
public class ListUserTasksCommand(
	IListUserTasksService listUserTasksService,
	ILogger logger)
	: Command<ListUserTasksOptions> {
	/// <inheritdoc />
	public override int Execute(ListUserTasksOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			IReadOnlyList<UserTaskInfoResult> userTasks = listUserTasksService.GetUserTasks(options.Environment);
			foreach (UserTaskInfoResult userTask in userTasks) {
				logger.WriteInfo($"{userTask.Name}\t{userTask.UId}");
			}

			logger.WriteInfo($"Total user tasks: {userTasks.Count}");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// A user-facing user task available on an environment.
/// </summary>
/// <param name="Name">User-task schema name/code (pass as the user-task name when building a process).</param>
/// <param name="UId">User-task schema UId.</param>
public sealed record UserTaskInfoResult(string? Name, string? UId);
