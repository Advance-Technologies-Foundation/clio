using System;
using System.Collections.Generic;
using Clio.UserEnvironment;

namespace Clio.Common;

public interface IMcpHttpClientFactory
{
	IMcpHttpClient CreateClient(EnvironmentSettings environmentSettings);
	IMcpHttpClient CreateClient(string url, string username, string password);
}

public class McpHttpClientFactory : IMcpHttpClientFactory
{
	private readonly ILogger _logger;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	public McpHttpClientFactory(ILogger logger, IServiceUrlBuilder serviceUrlBuilder)
	{
		_logger = logger;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	public IMcpHttpClient CreateClient(EnvironmentSettings environmentSettings)
	{
		if (environmentSettings == null)
		{
			throw new ArgumentNullException(nameof(environmentSettings));
		}

		var mcpUrl = _serviceUrlBuilder.BuildMcpUrl(environmentSettings);
		var client = new McpHttpClient(_logger);
		
		if (!string.IsNullOrEmpty(environmentSettings.ClientId))
		{
			throw new NotSupportedException(
				"OAuth authentication is not supported for MCP endpoints. Use basic authentication.");
		}

		client.InitializeAsync(mcpUrl, environmentSettings.Login, environmentSettings.Password)
			.GetAwaiter()
			.GetResult();

		return client;
	}

	public IMcpHttpClient CreateClient(string url, string username, string password)
	{
		if (string.IsNullOrEmpty(url))
		{
			throw new ArgumentNullException(nameof(url));
		}

		var mcpUrl = url.TrimEnd('/') + "/mcp";
		
		var client = new McpHttpClient(_logger);
		client.InitializeAsync(mcpUrl, username, password)
			.GetAwaiter()
			.GetResult();
		return client;
	}
}
