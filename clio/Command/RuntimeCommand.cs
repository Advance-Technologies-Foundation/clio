using System;
using Clio.Common;
using Clio.Common.OperatorBootstrap;
using Clio.Common.RuntimeAttachment;
using CommandLine;

namespace Clio.Command;

/// <summary>Targets an operator-managed runtime in a named Kubernetes context.</summary>
[Verb("runtime", HelpText = "Experimental: build, images, create, list, inspect, attach or detach Creatio runtimes.")]
[FeatureToggle(Clio.Command.ExperimentalFeature.Runtime)]
public class RuntimeOptions {
	/// <summary>Emit image distributions as JSON instead of a table.</summary>
	[Option("json")]
	public bool Json { get; set; }
	/// <summary>Namespace of the operator serving the image catalogue.</summary>
	[Option("operator-namespace", Default = "creatio-system")]
	public string OperatorNamespace { get; set; } = "creatio-system";
	/// <summary>Operation: build, images, create, list, status, attach or detach.</summary>
	[Value(0, Required = true, MetaName = "action")]
	public string Action { get; set; }
	/// <summary>Instance name for create, status and attach.</summary>
	[Value(1, MetaName = "name")]
	public string Name { get; set; }
	/// <summary>Explicit Kubernetes context, required except for detach which uses its receipt.</summary>
	[Option("context")]
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
	/// <summary>Local workspace required for attach and detach.</summary>
	[Option("workspace")]
	public string Workspace { get; set; }
	/// <summary>Existing SSH alias required for attach.</summary>
	[Option("ssh-alias")]
	public string SshAlias { get; set; }
	/// <summary>Attachment discovery provider.</summary>
	[Option("provider", Default = "operator-kubernetes")]
	public string Provider { get; set; }
	/// <summary>Attachment mode.</summary>
	[Option("mode", Default = "develop")]
	public string Mode { get; set; }

}

/// <summary>Dispatches host runtime lifecycle requests through the operator's Kubernetes API.</summary>
public class RuntimeCommand(IOperatorRuntimeService service, IRuntimeAttachmentService attachment, ILogger logger) : Command<RuntimeOptions> {
	/// <inheritdoc/>
	public override int Execute(RuntimeOptions options) {
		try {
			if (options.Action is "attach" or "detach") {
				if (string.IsNullOrWhiteSpace(options.Workspace)) throw new ArgumentException("Specify --workspace.");
				if (options.Action == "detach") {
					attachment.Detach(options.Workspace);
				} else {
					if (string.IsNullOrWhiteSpace(options.Name)) throw new ArgumentException("Specify a runtime name.");
					if (string.IsNullOrWhiteSpace(options.Context)) throw new ArgumentException("Specify --context explicitly.");
					if (string.IsNullOrWhiteSpace(options.SshAlias)) throw new ArgumentException("Specify --ssh-alias.");
					attachment.Attach(new AttachOptions {
						Environment = options.Name, Workspace = options.Workspace, SshAlias = options.SshAlias,
						Context = options.Context, Namespace = options.Namespace, Provider = options.Provider, Mode = options.Mode
					});
				}
			} else {
				service.Execute(options);
			}
			return 0;
		}
		catch (Exception ex) { logger.WriteError(ex.Message); return 1; }
	}
}
