using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.ProcessModel;

public class ProcessModel(Guid id, string code){
	public Guid Id { get; private set; } = id;
	public string Code { get; private set; } = code;
	public string Name { get; set; }
	
	public string Description { get; set; }

	public List<ProcessParameter> Parameters { get; set; }

	/// <summary>
	/// The process's flow elements as its schema stores them, flows included; <c>null</c> when the schema carried
	/// none. run-process reads them to tell which result parameters take a value a web service returned.
	/// </summary>
	[JsonIgnore]
	public List<FlowElement> FlowElements { get; set; }
}

public class Resources{
	[JsonPropertyName("Caption")]
	public Dictionary<string, string>? Caption { get; set; }
};
