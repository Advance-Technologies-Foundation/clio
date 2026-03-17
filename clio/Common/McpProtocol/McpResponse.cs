using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Common.McpProtocol;

public class McpResponse
{
	[JsonPropertyName("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("result")]
	public object Result { get; set; }

	[JsonPropertyName("error")]
	public McpError Error { get; set; }
}

public class McpError
{
	[JsonPropertyName("code")]
	public int Code { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }

	[JsonPropertyName("data")]
	public object Data { get; set; }
}

public class McpInitializeResult
{
	[JsonPropertyName("protocolVersion")]
	public string ProtocolVersion { get; set; }

	[JsonPropertyName("capabilities")]
	public object Capabilities { get; set; }

	[JsonPropertyName("serverInfo")]
	public McpServerInfo ServerInfo { get; set; }
}

public class McpServerInfo
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("version")]
	public string Version { get; set; }
}

public class McpToolCallResult
{
	[JsonPropertyName("content")]
	public List<McpContent> Content { get; set; }

	[JsonPropertyName("isError")]
	public bool? IsError { get; set; }
}

public class McpContent
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("text")]
	public string Text { get; set; }
}
