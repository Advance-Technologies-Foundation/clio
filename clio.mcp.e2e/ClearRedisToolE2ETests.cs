using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Redis;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the clear-redis MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("clear-redis-db")]
public sealed class ClearRedisToolE2ETests {
	private const string EnvironmentToolName = ClearRedisTool.ClearRedisByEnvironmentName;
	private const string CredentialsToolName = ClearRedisTool.ClearRedisByCredentialsToolName;
	private string? _seedKey;
	private SandboxEnvironmentContext? _sandboxContext;

	[Test]
	[Description("Starts the real clio MCP server, invokes clear-redis against a configured sandbox environment, and verifies the seeded Redis key is removed.")]
	[AllureTag(EnvironmentToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes clear-redis against a configured sandbox environment, and verifies the seeded Redis key is removed.")]
	[AllureName("Clear Redis Tool removes seeded key from sandbox environment")]
	public async Task ClearRedis_Should_Remove_Seeded_Key_When_Invoked_Through_Mcp() {
		// Arrange
		await using ClearRedisArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		ClearRedisActResult actResult = await ActAsync(arrangeContext);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult);
		AssertSuccessIncludesInfoMessage(actResult);
		AssertDatabaseConnectionWasResolved(arrangeContext);
		await AssertSeededKeyWasDeletedAsync(arrangeContext);
	}

	[Test]
	[Description("Invokes clear-redis with a non-existent environment name and verifies that the MCP result reports failure diagnostics without mutating sandbox Redis state.")]
	[AllureTag(EnvironmentToolName)]
	[AllureDescription("Invokes clear-redis with a non-existent environment name and verifies that the MCP result reports a failure with human-readable diagnostics.")]
	[AllureName("Clear Redis Tool reports invalid environment name failures")]
	public async Task ClearRedis_Should_Report_Failure_When_Environment_Name_Is_Invalid() {
		// Arrange
		await using ClearRedisArrangeContext arrangeContext = await ArrangeAsync();
		string invalidEnvironmentName = $"{arrangeContext.SandboxContext.EnvironmentName}-missing";

		// Act
		ClearRedisActResult actResult = await ActWithEnvironmentNameAsync(arrangeContext, invalidEnvironmentName);

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureMessageMentionsInvalidEnvironment(actResult, invalidEnvironmentName);
		AssertFailureIncludesErrorMessage(actResult);
		await AssertSeededKeyRemainsAsync(arrangeContext);
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes clear-redis-by-credentials with the registered sandbox URL and credentials, and verifies the seeded Redis key is removed.")]
	[AllureTag(CredentialsToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes clear-redis-by-credentials with the registered sandbox URL and credentials, and verifies the seeded Redis key is removed.")]
	[AllureName("Clear Redis Tool removes seeded key by explicit credentials")]
	public async Task ClearRedisByCredentials_Should_Remove_Seeded_Key_When_Invoked_Through_Mcp() {
		// Arrange
		await using ClearRedisArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		ClearRedisActResult actResult = await ActWithCredentialsAsync(arrangeContext, arrangeContext.SandboxContext.Password, isNetCore: true);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult);
		AssertSuccessIncludesInfoMessage(actResult);
		await AssertSeededKeyWasDeletedAsync(arrangeContext);
	}

	[Test]
	[Description("Invokes clear-redis-by-credentials with an invalid URL and verifies that the MCP result reports failure diagnostics without mutating sandbox Redis state.")]
	[AllureTag(CredentialsToolName)]
	[AllureDescription("Invokes clear-redis-by-credentials with an invalid URL and verifies that the MCP result reports a failure with human-readable diagnostics.")]
	[AllureName("Clear Redis Tool reports invalid URL failures")]
	public async Task ClearRedisByCredentials_Should_Report_Failure_When_Url_Is_Invalid() {
		// Arrange
		await using ClearRedisArrangeContext arrangeContext = await ArrangeAsync();
		UriBuilder invalidUrl = new(arrangeContext.SandboxContext.Uri) {
			Port = 49999
		};

		// Act
		ClearRedisActResult actResult = await ActWithCredentialsAsync(
			arrangeContext,
			arrangeContext.SandboxContext.Password,
			invalidUrl.Uri.ToString(),
			isNetCore: true);

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMessageMentionsInvalidUrl(actResult);
		await AssertSeededKeyRemainsAsync(arrangeContext);
	}

	[AllureStep("Arrange clear-redis sandbox state")]
	[AllureDescription("Arrange by seeding a Redis key in the sandbox environment and starting an MCP server session")]
	private async Task<ClearRedisArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive MCP end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		_sandboxContext = SandboxEnvironmentResolver.Resolve(settings);
		_seedKey = $"{settings.Sandbox.SeedKeyPrefix}:{Guid.NewGuid():N}";
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));

		RedisSandboxClient redis = await RedisSandboxClient.ConnectAsync(_sandboxContext.RedisConnectionString);
		await redis.SeedKeyAsync(_seedKey, "seeded-by-clio-mcp-e2e");
		(await redis.KeyExistsAsync(_seedKey)).Should().BeTrue(
			because: "the test must prove clear-redis removes data that was present before the MCP call");

		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ClearRedisArrangeContext(_sandboxContext, _seedKey, redis, session, cancellationTokenSource);
	}

	[AllureStep("Act by invoking clear-redis through MCP")]
	[AllureDescription("Act by discovering the clear-redis MCP tool and invoking it with the configured sandbox environment name")]
	private static async Task<ClearRedisActResult> ActAsync(ClearRedisArrangeContext arrangeContext) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(EnvironmentToolName,
			because: "the sandbox test path depends on the environment-name clear-redis tool being advertised by the MCP server");

		return await ActWithEnvironmentNameAsync(arrangeContext, arrangeContext.SandboxContext.EnvironmentName);
	}

	[AllureStep("Act by invoking clear-redis with invalid environment name")]
	[AllureDescription("Act by invoking the environment-name clear-redis MCP tool with an environment key that is not registered in clio")]
	private static async Task<ClearRedisActResult> ActWithEnvironmentNameAsync(
		ClearRedisArrangeContext arrangeContext,
		string environmentName) {
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			EnvironmentToolName,
			new Dictionary<string, object?> {
				["environmentName"] = environmentName
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = ExtractExecution(callResult);
		return new ClearRedisActResult(callResult, execution);
	}

	[AllureStep("Act by invoking clear-redis with explicit credentials")]
	[AllureDescription("Act by invoking the credentials clear-redis MCP tool with the registered sandbox URL and credentials")]
	private static async Task<ClearRedisActResult> ActWithCredentialsAsync(
		ClearRedisArrangeContext arrangeContext,
		string password,
		string? url = null,
		bool? isNetCore = null) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(CredentialsToolName,
			because: "the credentials clear-redis tool must be advertised by the MCP server for credentials-path end-to-end coverage");

		Dictionary<string, object?> arguments = new() {
			["url"] = url ?? arrangeContext.SandboxContext.Uri,
			["userName"] = arrangeContext.SandboxContext.Login,
			["password"] = password
		};
		if (isNetCore.HasValue) {
			arguments["isNetCore"] = isNetCore.Value;
		}

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			CredentialsToolName,
			arguments,
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = ExtractExecution(callResult);
		return new ClearRedisActResult(callResult, execution);
	}

	[AllureStep("Assert MCP tool result is successful")]
	[AllureDescription("Assert that the MCP tool call completed without returning an MCP error result")]
	private static void AssertToolCallSucceeded(ClearRedisActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "a successful clear-redis invocation should return a normal MCP tool result");
	}

	[AllureStep("Assert clear-redis command exit code")]
	[AllureDescription("Assert that the underlying clear-redis command completed with exit code 0")]
	private static void AssertCommandExitCode(ClearRedisActResult actResult) {
		actResult.Execution.ExitCode.Should().Be(0,
			because: "the underlying clio clear-redis command should complete successfully for the configured sandbox");
	}

	[AllureStep("Assert success output contains info message")]
	[AllureDescription("Assert that successful clear-redis execution includes at least one Info log message in the MCP command output")]
	private static void AssertSuccessIncludesInfoMessage(ClearRedisActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "successful MCP command execution should emit human-readable log messages");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info,
			because: "successful clear-redis execution should report progress or completion using info-level log output");
	}

	[AllureStep("Assert sandbox database connection string was resolved")]
	[AllureDescription("Assert that the sandbox resolver captured the database connection string from ConnectionStrings.config for later end-to-end scenarios")]
	private static void AssertDatabaseConnectionWasResolved(ClearRedisArrangeContext arrangeContext) {
		arrangeContext.SandboxContext.DatabaseConnectionString.Should().NotBeNullOrWhiteSpace(
			because: "the sandbox resolver must capture the database connection string for later MCP end-to-end scenarios");
	}

	[AllureStep("Assert seeded Redis key was deleted")]
	[AllureDescription("Assert that the seeded Redis key disappears after the clear-redis MCP tool runs")]
	private static async Task AssertSeededKeyWasDeletedAsync(ClearRedisArrangeContext arrangeContext) {
		await arrangeContext.Redis.WaitUntilKeyDeletedAsync(
			arrangeContext.SeedKey,
			TimeSpan.FromSeconds(10),
			arrangeContext.CancellationTokenSource.Token);
		(await arrangeContext.Redis.KeyExistsAsync(arrangeContext.SeedKey)).Should().BeFalse(
			because: "clear-redis must remove the key that was seeded before the MCP tool call");
	}

	[AllureStep("Assert failed clear-redis request reported failure")]
	[AllureDescription("Assert that the clear-redis MCP tool reports failure instead of succeeding silently when the request is invalid")]
	private static void AssertToolCallFailed(ClearRedisActResult actResult) {
		bool failed = actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0;
		failed.Should().BeTrue(
			because: "clear-redis should fail when the requested input cannot be executed successfully");
	}

	[AllureStep("Assert failure diagnostics are human-readable")]
	[AllureDescription("Assert that the failure output explains the failed clear-redis invocation, ideally by naming the invalid environment or at minimum by identifying the failing MCP tool call")]
	private static void AssertFailureMessageMentionsInvalidEnvironment(ClearRedisActResult actResult, string invalidEnvironmentName) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? [])
			.Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed clear-redis execution should provide diagnostics that explain why the environment lookup failed");
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|clear-redis-by-environment|error occurred invoking)",
			because: "the failure log should help a human understand that the request failed, even when the MCP server surfaces a top-level tool invocation error instead of the underlying environment lookup message");
	}

	[AllureStep("Assert failure output contains error message type")]
	[AllureDescription("Assert that failed clear-redis execution emits at least one Error log message when execution output is available")]
	private static void AssertFailureIncludesErrorMessage(ClearRedisActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed MCP command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Error,
			because: "failed clear-redis execution should report its diagnostics as error-level log output");
	}

	[AllureStep("Assert failure diagnostics mention invalid URL")]
	[AllureDescription("Assert that the failure output identifies the failed credentials-based clear-redis invocation or the connection problem caused by the invalid URL")]
	private static void AssertFailureMessageMentionsInvalidUrl(ClearRedisActResult actResult) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? [])
			.Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed credentials-based clear-redis execution should provide diagnostics that explain why the URL could not be reached");
		combinedOutput.Should().MatchRegex(
			"(?is)(clear-redis-by-credentials|error occurred invoking|connection|refused|could not be reached|actively refused)",
			because: "the failure log should help a human understand that the credentials-based request failed because the target URL was invalid");
	}

	[AllureStep("Assert seeded Redis key remains after failed request")]
	[AllureDescription("Assert that a failed clear-redis request targeting an invalid environment does not remove the seeded sandbox Redis key")]
	private static async Task AssertSeededKeyRemainsAsync(ClearRedisArrangeContext arrangeContext) {
		(await arrangeContext.Redis.KeyExistsAsync(arrangeContext.SeedKey)).Should().BeTrue(
			because: "a failed clear-redis invocation against an invalid environment must not mutate the sandbox Redis state");
	}

	[TearDown]
	public async Task TearDownAsync() {
		try {
			if (!string.IsNullOrWhiteSpace(_seedKey) && _sandboxContext is not null) {
				await using RedisSandboxClient redis = await RedisSandboxClient.ConnectAsync(_sandboxContext.RedisConnectionString);
				await redis.DeleteKeyIfExistsAsync(_seedKey);
			}
		}
		finally {
			_seedKey = null;
			_sandboxContext = null;
		}
	}

	private sealed record ClearRedisArrangeContext(
		SandboxEnvironmentContext SandboxContext,
		string SeedKey,
		RedisSandboxClient Redis,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			await Redis.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record ClearRedisActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);

	private static CommandExecutionEnvelope ExtractExecution(CallToolResult callResult) {
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtractExecutionFromElement(structuredContent, callResult.IsError == true, out CommandExecutionEnvelope? structuredExecution)) {
			return structuredExecution!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtractExecutionFromElement(content, callResult.IsError == true, out CommandExecutionEnvelope? contentExecution)) {
			return contentExecution!;
		}

		if (callResult.IsError == true) {
			return new CommandExecutionEnvelope(
				1,
				[
					new CommandLogMessageEnvelope(
						LogDecoratorType.Error,
						"MCP tool call returned an error result without a parsable execution payload.")
				]);
		}

		return new CommandExecutionEnvelope(0);
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}

		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryExtractExecutionFromElement(
		JsonElement element,
		bool isErrorResult,
		out CommandExecutionEnvelope? execution) {
		if (TryParseExecutionEnvelope(element, out execution)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array) {
			List<CommandLogMessageEnvelope> messages = [];
			int? exitCode = null;

			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryParseExecutionEnvelope(item, out CommandExecutionEnvelope? nestedExecution)) {
					CommandExecutionEnvelope parsedNestedExecution = nestedExecution!;
					exitCode ??= parsedNestedExecution.ExitCode;
					if (parsedNestedExecution.Output is not null) {
						messages.AddRange(parsedNestedExecution.Output);
					}
					continue;
				}

				if (!TryGetTextPayload(item, out string? textPayload) || string.IsNullOrWhiteSpace(textPayload)) {
					continue;
				}

				if (TryParseJson(textPayload, out JsonElement textPayloadElement) &&
					TryParseExecutionEnvelope(textPayloadElement, out CommandExecutionEnvelope? textExecution)) {
					CommandExecutionEnvelope parsedTextExecution = textExecution!;
					exitCode ??= parsedTextExecution.ExitCode;
					if (parsedTextExecution.Output is not null) {
						messages.AddRange(parsedTextExecution.Output);
					}
					continue;
				}

				if (TryExtractExitCode(textPayload, out int parsedExitCode)) {
					exitCode ??= parsedExitCode;
				}

				messages.Add(new CommandLogMessageEnvelope(
					isErrorResult ? LogDecoratorType.Error : LogDecoratorType.Info,
					textPayload));
			}

			if (exitCode.HasValue || messages.Count > 0) {
				execution = new CommandExecutionEnvelope(
					exitCode ?? (isErrorResult ? 1 : 0),
					messages.Count > 0 ? messages : null);
				return true;
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload)) {
				if (TryParseJson(textPayload, out JsonElement textPayloadElement) &&
					TryParseExecutionEnvelope(textPayloadElement, out CommandExecutionEnvelope? textExecution)) {
					execution = textExecution;
					return true;
				}

				if (TryExtractExitCode(textPayload, out int parsedExitCode)) {
					execution = new CommandExecutionEnvelope(parsedExitCode, [
						new CommandLogMessageEnvelope(
							isErrorResult ? LogDecoratorType.Error : LogDecoratorType.Info,
							textPayload)
					]);
					return true;
				}

				execution = new CommandExecutionEnvelope(
					isErrorResult ? 1 : 0,
					[
						new CommandLogMessageEnvelope(
							isErrorResult ? LogDecoratorType.Error : LogDecoratorType.Info,
							textPayload)
					]);
				return true;
			}
		}

		execution = null;
		return false;
	}

	private static bool TryParseExecutionEnvelope(JsonElement element, out CommandExecutionEnvelope? execution) {
		execution = null;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}

		if (!TryGetProperty(element, "exit-code", "exitCode", "ExitCode", out JsonElement exitCodeElement) ||
			!TryReadInt32(exitCodeElement, out int exitCode)) {
			return false;
		}

		IReadOnlyList<CommandLogMessageEnvelope>? output = null;
		if (TryGetProperty(
			element,
			"execution-log-messages",
			"executionLogMessages",
			"output",
			"Output",
			out JsonElement outputElement)) {
			output = ParseLogMessages(outputElement);
		}

		execution = new CommandExecutionEnvelope(exitCode, output);
		return true;
	}

	private static IReadOnlyList<CommandLogMessageEnvelope>? ParseLogMessages(JsonElement element) {
		if (element.ValueKind != JsonValueKind.Array) {
			return null;
		}

		List<CommandLogMessageEnvelope> messages = [];
		foreach (JsonElement item in element.EnumerateArray()) {
			if (item.ValueKind != JsonValueKind.Object) {
				continue;
			}

			LogDecoratorType messageType = ParseMessageType(item);
			string? value = TryGetProperty(item, "value", "Value", out JsonElement valueElement)
				? valueElement.ToString()
				: null;
			messages.Add(new CommandLogMessageEnvelope(messageType, value));
		}

		return messages.Count > 0 ? messages : null;
	}

	private static LogDecoratorType ParseMessageType(JsonElement element) {
		if (!TryGetProperty(element, "message-type", "messageType", "logDecoratorType", "LogDecoratorType", out JsonElement messageTypeElement)) {
			return LogDecoratorType.None;
		}

		if (messageTypeElement.ValueKind == JsonValueKind.Number &&
			messageTypeElement.TryGetInt32(out int numericMessageType) &&
			Enum.IsDefined(typeof(LogDecoratorType), numericMessageType)) {
			return (LogDecoratorType)numericMessageType;
		}

		if (messageTypeElement.ValueKind == JsonValueKind.String &&
			Enum.TryParse(messageTypeElement.GetString(), ignoreCase: true, out LogDecoratorType parsedMessageType)) {
			return parsedMessageType;
		}

		return LogDecoratorType.None;
	}

	private static bool TryGetTextPayload(JsonElement element, out string? textPayload) {
		textPayload = null;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}

		if (TryGetProperty(element, "text", "Text", out JsonElement textElement) &&
			textElement.ValueKind == JsonValueKind.String) {
			textPayload = textElement.GetString();
			return true;
		}

		return false;
	}

	private static bool TryParseJson(string value, out JsonElement element) {
		try {
			element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
			return true;
		}
		catch (JsonException) {
			element = default;
			return false;
		}
	}

	private static bool TryExtractExitCode(string value, out int exitCode) {
		Match jsonExitCode = Regex.Match(value, "\"exit-code\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
		if (jsonExitCode.Success && int.TryParse(jsonExitCode.Groups[1].Value, out exitCode)) {
			return true;
		}

		Match textExitCode = Regex.Match(value, "ExitCode\\s*[=:]\\s*(\\d+)", RegexOptions.IgnoreCase);
		if (textExitCode.Success && int.TryParse(textExitCode.Groups[1].Value, out exitCode)) {
			return true;
		}

		exitCode = default;
		return false;
	}

	private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement propertyValue) {
		if (element.TryGetProperty(propertyName, out propertyValue)) {
			return true;
		}

		propertyValue = default;
		return false;
	}

	private static bool TryGetProperty(JsonElement element, string propertyName, string alternatePropertyName, out JsonElement propertyValue) {
		if (TryGetProperty(element, propertyName, out propertyValue)) {
			return true;
		}

		return TryGetProperty(element, alternatePropertyName, out propertyValue);
	}

	private static bool TryGetProperty(
		JsonElement element,
		string propertyName,
		string alternatePropertyName,
		string secondAlternatePropertyName,
		out JsonElement propertyValue) {
		if (TryGetProperty(element, propertyName, alternatePropertyName, out propertyValue)) {
			return true;
		}

		return TryGetProperty(element, secondAlternatePropertyName, out propertyValue);
	}

	private static bool TryGetProperty(
		JsonElement element,
		string propertyName,
		string alternatePropertyName,
		string secondAlternatePropertyName,
		string thirdAlternatePropertyName,
		out JsonElement propertyValue) {
		if (TryGetProperty(element, propertyName, alternatePropertyName, secondAlternatePropertyName, out propertyValue)) {
			return true;
		}

		return TryGetProperty(element, thirdAlternatePropertyName, out propertyValue);
	}

	private static bool TryReadInt32(JsonElement element, out int value) {
		if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value)) {
			return true;
		}

		value = default;
		return false;
	}
}
