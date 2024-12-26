using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Clio.Command.StartProcess;

[JsonObject]
public record ProcessStartArgs
{

	[JsonObject]
	public class ParameterValues
	{
		[JsonPropertyName("name")]
		[JsonProperty("name")]
		public string Name { get; init; }

		[JsonPropertyName("value")]
		[JsonProperty("value")]
		public string Value { get; init; }
	}

	[JsonProperty("schemaName")]
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	[JsonProperty("parameterValues")]
	[JsonPropertyName("parameterValues")]
	public ParameterValues[] Values { get; init; }

	[JsonProperty("resultParameterNames")]
	[JsonPropertyName("resultParameterNames")]
	public string[] Result { get; init; }

}


[JsonObject]
public record ProcessStartResponse
{
	[JsonPropertyName("processId")]
	[JsonProperty("processId")]
	public Guid ProcessId { get; init; }
	
	[JsonPropertyName("processStatus")]
	[JsonProperty("processStatus")]
	public int ProcessStatus { get; init; }
	
	[JsonPropertyName("resultParameterValues")]
	[JsonProperty("resultParameterValues")]
	public Dictionary<string, object> ResultParameterValues { get; init; }
	
	[JsonPropertyName("executionData")]
	[JsonProperty("executionData")]
	public object ExecutionData { get; init; }
	
	[JsonPropertyName("success")]
	[JsonProperty("success")]
	public bool Success { get; init; }

	[JsonPropertyName("errorInfo")]
	[JsonProperty("errorInfo")]
	public object ErrorInfo { get; init; }
}