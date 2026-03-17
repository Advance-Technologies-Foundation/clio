using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Common.McpProtocol;

public class McpRequest
{
	[JsonPropertyName("jsonrpc")]
	public string JsonRpc { get; set; } = "2.0";

	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("method")]
	public string Method { get; set; }

	[JsonPropertyName("params")]
	public object Params { get; set; }
}

public class McpInitializeParams
{
	[JsonPropertyName("protocolVersion")]
	public string ProtocolVersion { get; set; } = "2024-11-05";

	[JsonPropertyName("capabilities")]
	public object Capabilities { get; set; } = new { };

	[JsonPropertyName("clientInfo")]
	public McpClientInfo ClientInfo { get; set; }
}

public class McpClientInfo
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("version")]
	public string Version { get; set; }
}

public class McpToolCallParams
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("arguments")]
	public Dictionary<string, object> Arguments { get; set; }
}
