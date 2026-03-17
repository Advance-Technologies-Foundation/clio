using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.McpProtocol;

namespace Clio.Common;

public interface IMcpHttpClient : IDisposable
{
	Task<string> InitializeAsync(string mcpEndpointUrl, string username, string password, CancellationToken cancellationToken = default);
	Task<McpToolCallResult> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);
}

public class McpHttpClient : IMcpHttpClient, IDisposable
{
	private readonly HttpClient _httpClient;
	private readonly ILogger _logger;
	private string _sessionId;
	private string _mcpEndpointUrl;
	private int _requestId;
	private readonly object _requestIdLock = new();
	
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
	};

	public McpHttpClient(ILogger logger)
	{
		_httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(5)
		};
		_logger = logger;
		_requestId = 0;
	}

	public async Task<string> InitializeAsync(
		string mcpEndpointUrl,
		string username,
		string password,
		CancellationToken cancellationToken = default)
	{
		_mcpEndpointUrl = mcpEndpointUrl;
		
		var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
		_httpClient.DefaultRequestHeaders.Accept.Clear();
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

		var initRequest = new McpRequest
		{
			Id = GetNextRequestId(),
			Method = "initialize",
			Params = new McpInitializeParams
			{
				ClientInfo = new McpClientInfo
				{
					Name = "clio",
					Version = "8.0.0"
				}
			}
		};

		var response = await SendMcpRequestAsync(initRequest, cancellationToken);
		
		if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
		{
			_sessionId = string.Join("", sessionIds);
			_logger.WriteInfo($"MCP session initialized: {_sessionId}");
		}
		else
		{
			throw new InvalidOperationException("MCP server did not return session ID");
		}

		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		var jsonContent = ParseSseContent(content);
		var mcpResponse = JsonSerializer.Deserialize<McpResponse>(jsonContent, JsonOptions);
		
		if (mcpResponse?.Error != null)
		{
			throw new InvalidOperationException($"MCP initialization failed: {mcpResponse.Error.Message}");
		}

		return _sessionId;
	}

	private static string ParseSseContent(string sseContent)
	{
		var lines = sseContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		foreach (var line in lines)
		{
			if (line.StartsWith("data: "))
			{
				return line.Substring(6);
			}
		}
		throw new InvalidOperationException($"No data line found in SSE response. Content: {sseContent}");
	}

	public async Task<McpToolCallResult> CallToolAsync(
		string toolName,
		Dictionary<string, object> arguments,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(_sessionId))
		{
			throw new InvalidOperationException("MCP client not initialized. Call InitializeAsync first.");
		}

		var toolCallRequest = new McpRequest
		{
			Id = GetNextRequestId(),
			Method = "tools/call",
			Params = new McpToolCallParams
			{
				Name = toolName,
				Arguments = arguments ?? new Dictionary<string, object>()
			}
		};

		var response = await SendMcpRequestAsync(toolCallRequest, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		
		var jsonContent = ParseSseContent(content);
		var mcpResponse = JsonSerializer.Deserialize<McpResponse>(jsonContent, JsonOptions);
		
		if (mcpResponse?.Error != null)
		{
			throw new InvalidOperationException(
				$"MCP tool call '{toolName}' failed: {mcpResponse.Error.Message}");
		}

		if (mcpResponse?.Result == null)
		{
			throw new InvalidOperationException($"MCP tool call '{toolName}' returned no result");
		}

		var resultJson = JsonSerializer.Serialize(mcpResponse.Result, JsonOptions);
		var result = JsonSerializer.Deserialize<McpToolCallResult>(resultJson, JsonOptions);
		
		return result;
	}

	private async Task<HttpResponseMessage> SendMcpRequestAsync(
		McpRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(_mcpEndpointUrl))
		{
			throw new InvalidOperationException("MCP endpoint URL not set");
		}

		var jsonRequest = JsonSerializer.Serialize(request, JsonOptions);
		var httpContent = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

		var httpRequest = new HttpRequestMessage(HttpMethod.Post, _mcpEndpointUrl)
		{
			Content = httpContent
		};

		if (!string.IsNullOrEmpty(_sessionId))
		{
			httpRequest.Headers.Add("Mcp-Session-Id", _sessionId);
		}

		_logger.WriteInfo($"Sending MCP request: {request.Method} (id: {request.Id})");
		
		try
		{
			var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
			response.EnsureSuccessStatusCode();
			return response;
		}
		catch (HttpRequestException ex)
		{
			throw new InvalidOperationException(
				$"HTTP request to MCP endpoint failed: {ex.Message}", ex);
		}
	}

	private int GetNextRequestId()
	{
		lock (_requestIdLock)
		{
			return ++_requestId;
		}
	}

	public void Dispose()
	{
		_httpClient?.Dispose();
	}
}
