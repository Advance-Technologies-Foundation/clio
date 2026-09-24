using System;
using Clio.Common;
using Clio.Common.OperatorBootstrap;
using CommandLine;

namespace Clio.Command;

/// <summary>Targets an operator-managed runtime in a named Kubernetes context.</summary>
[Verb("runtime", HelpText = "Build, create, list or inspect Creatio operator runtimes in a selected cluster.")]
public class RuntimeOptions {
	/// <summary>Operation: build, create, list or status.</summary>
	[Value(0, Required = true, MetaName = "action")]
	public string Action { get; set; }
	/// <summary>Instance name for create/status.</summary>
	[Value(1, MetaName = "name")]
	public string Name { get; set; }
	/// <summary>Explicit Kubernetes context; never defaults to the ambient context.</summary>
	[Option("context", Required = true)]
	public string Context { get; set; }
	/// <summary>Namespace for the runtime.</summary>
	[Option("namespace", Default = "creatio-runtimes")]
	public string Namespace { get; set; }
	/// <summary>Tagged image available to the selected cluster, required for create.</summary>
	[Option("image")]
	public string Image { get; set; }
	/// <summary>Creatio ZIP for a local Rancher build.</summary>
	[Option("from")]
	public string Source { get; set; }
}

/// <summary>Dispatches host runtime lifecycle requests through the operator's Kubernetes API.</summary>
public class RuntimeCommand(IOperatorRuntimeService service, ILogger logger) : Command<RuntimeOptions> {
	/// <inheritdoc/>
	public override int Execute(RuntimeOptions options) {
		try { service.Execute(options); return 0; }
		catch (Exception ex) { logger.WriteError(ex.Message); return 1; }
	}
}
