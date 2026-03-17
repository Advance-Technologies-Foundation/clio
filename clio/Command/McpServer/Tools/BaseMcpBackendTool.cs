using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Common.McpProtocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public abstract class BaseMcpBackendTool<T> where T : EnvironmentOptions
{
	private readonly IMcpHttpClientFactory _mcpClientFactory;
	private readonly ILogger _logger;
	private static readonly object CommandExecutionLock = new();

	protected BaseMcpBackendTool(IMcpHttpClientFactory mcpClientFactory, ILogger logger)
	{
		_mcpClientFactory = mcpClientFactory ?? throw new ArgumentNullException(nameof(mcpClientFactory));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	protected CommandExecutionResult ExecuteMcpTool(
		T options,
		string mcpToolName,
		Dictionary<string, object> arguments)
	{
		lock (CommandExecutionLock)
		{
			bool previousPreserveMessages = _logger.PreserveMessages;
			_logger.PreserveMessages = true;
			
			try
			{
				var mcpClient = _mcpClientFactory.CreateClient(options.Uri, options.Login, options.Password);
				
				var result = mcpClient.CallToolAsync(mcpToolName, arguments)
					.GetAwaiter()
					.GetResult();

				if (result.IsError == true)
				{
					var errorMessage = GetErrorMessage(result);
					_logger.WriteError(errorMessage);
					return new CommandExecutionResult(1, [.. _logger.LogMessages]);
				}

				var successMessage = GetSuccessMessage(result);
				if (!string.IsNullOrEmpty(successMessage))
				{
					_logger.WriteInfo(successMessage);
				}

				return new CommandExecutionResult(0, [.. _logger.LogMessages]);
			}
			catch (Exception ex)
			{
				_logger.WriteError($"MCP tool '{mcpToolName}' failed: {ex.Message}");
				if (ex.InnerException != null)
				{
					_logger.WriteError($"Inner exception: {ex.InnerException.Message}");
				}
				_logger.WriteError($"Stack trace: {ex.StackTrace}");
				return new CommandExecutionResult(1, [.. _logger.LogMessages, new ErrorMessage($"{ex.Message}\n{ex.StackTrace}")]);
			}
			finally
			{
				_logger.PreserveMessages = previousPreserveMessages;
				_logger.ClearMessages();
			}
		}
	}

	protected CommandExecutionResult ExecuteMcpToolWithEnvironment(
		T options,
		string mcpToolName,
		Dictionary<string, object> arguments,
		IToolCommandResolver commandResolver)
	{
		if (commandResolver == null)
		{
			throw new InvalidOperationException(
				$"{GetType().Name} requires IToolCommandResolver for environment-based operations.");
		}

		try
		{
			_logger.WriteInfo($"Resolving environment settings for: {options.Environment ?? options.Uri}");
			var environmentSettings = commandResolver.ResolveEnvironmentSettings(options);
			_logger.WriteInfo($"Environment settings resolved: Uri={environmentSettings.Uri}, Login={environmentSettings.Login}");
			
			var mcpClient = _mcpClientFactory.CreateClient(environmentSettings);
			return ExecuteMcpToolCore(mcpClient, mcpToolName, arguments);
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Failed to resolve environment or create MCP client: {ex.Message}");
			if (ex.InnerException != null)
			{
				_logger.WriteError($"Inner exception: {ex.InnerException.Message}");
			}
			_logger.WriteError($"Stack trace: {ex.StackTrace}");
			return new CommandExecutionResult(1, [.. _logger.LogMessages, new ErrorMessage($"{ex.Message}\n{ex.StackTrace}")]);
		}
	}

	private CommandExecutionResult ExecuteMcpToolCore(
		IMcpHttpClient mcpClient,
		string mcpToolName,
		Dictionary<string, object> arguments)
	{
		lock (CommandExecutionLock)
		{
			bool previousPreserveMessages = _logger.PreserveMessages;
			_logger.PreserveMessages = true;
			
			try
			{
				var result = mcpClient.CallToolAsync(mcpToolName, arguments)
					.GetAwaiter()
					.GetResult();

				if (result.IsError == true)
				{
					var errorMessage = GetErrorMessage(result);
					_logger.WriteError(errorMessage);
					return new CommandExecutionResult(1, [.. _logger.LogMessages]);
				}

				var successMessage = GetSuccessMessage(result);
				if (!string.IsNullOrEmpty(successMessage))
				{
					_logger.WriteInfo(successMessage);
				}

				return new CommandExecutionResult(0, [.. _logger.LogMessages]);
			}
			catch (Exception ex)
			{
				_logger.WriteError($"MCP tool '{mcpToolName}' failed: {ex.Message}");
				if (ex.InnerException != null)
				{
					_logger.WriteError($"Inner exception: {ex.InnerException.Message}");
				}
				_logger.WriteError($"Stack trace: {ex.StackTrace}");
				return new CommandExecutionResult(1, [.. _logger.LogMessages, new ErrorMessage($"{ex.Message}\n{ex.StackTrace}")]);
			}
			finally
			{
				_logger.PreserveMessages = previousPreserveMessages;
				_logger.ClearMessages();
			}
		}
	}

	protected TResult ExecuteMcpToolWithResult<TResult>(
		T options,
		string mcpToolName,
		Dictionary<string, object> arguments)
	{
		var mcpClient = _mcpClientFactory.CreateClient(options.Uri, options.Login, options.Password);
		
		var result = mcpClient.CallToolAsync(mcpToolName, arguments)
			.GetAwaiter()
			.GetResult();

		if (result.IsError == true)
		{
			var errorMessage = GetErrorMessage(result);
			throw new InvalidOperationException(errorMessage);
		}

		return DeserializeResult<TResult>(result);
	}

	private string GetErrorMessage(McpToolCallResult result)
	{
		if (result?.Content == null || !result.Content.Any())
		{
			return "Unknown error occurred";
		}

		var errorContent = result.Content.FirstOrDefault(c => c.Type == "text");
		return errorContent?.Text ?? "Unknown error occurred";
	}

	private string GetSuccessMessage(McpToolCallResult result)
	{
		if (result?.Content == null || !result.Content.Any())
		{
			return string.Empty;
		}

		var textContent = result.Content.FirstOrDefault(c => c.Type == "text");
		return textContent?.Text ?? string.Empty;
	}

	private TResult DeserializeResult<TResult>(McpToolCallResult result)
	{
		var contentText = GetSuccessMessage(result);
		if (string.IsNullOrEmpty(contentText))
		{
			return default;
		}

		try
		{
			return JsonSerializer.Deserialize<TResult>(contentText, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});
		}
		catch (JsonException ex)
		{
			throw new InvalidOperationException($"Failed to deserialize MCP result: {ex.Message}", ex);
		}
	}
}
