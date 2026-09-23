using System;
using Clio.Common;
using Clio.Common.RuntimeAttachment;
using CommandLine;

namespace Clio.Command;

/// <summary>Options for attaching a local workspace to an existing runtime.</summary>
[Verb("attach", HelpText = "Attach a local workspace to an existing Kubernetes Creatio runtime.")]
public class AttachOptions {
	/// <summary>Operator instance name.</summary>
	[Value(0, Required = true, MetaName = "environment")]
	public string Environment { get; set; }
	/// <summary>Local Clio workspace directory.</summary>
	[Option("workspace", Required = true)]
	public string Workspace { get; set; }
	/// <summary>Attachment provider.</summary>
	[Option("provider", Default = "operator-kubernetes")]
	public string Provider { get; set; }
	/// <summary>Kubernetes context; defaults to the current context.</summary>
	[Option("context")]
	public string Context { get; set; }
	/// <summary>Instance namespace; discovery fails if the name is ambiguous.</summary>
	[Option("namespace")]
	public string Namespace { get; set; }
	/// <summary>Existing OpenSSH alias with key authentication and a trusted host key.</summary>
	[Option("ssh-alias", Required = true)]
	public string SshAlias { get; set; }
	/// <summary>Attachment mode.</summary>
	[Option("mode", Default = "develop")]
	public string Mode { get; set; }
}

/// <summary>Options for ending an attachment without destroying its runtime or files.</summary>
[Verb("detach", HelpText = "Detach a workspace while preserving the runtime and workspace files.")]
public class DetachOptions {
	/// <summary>Previously attached local workspace.</summary>
	[Option("workspace", Required = true)]
	public string Workspace { get; set; }
}

/// <summary>Host-side workspace attachment command.</summary>
public class AttachCommand(IRuntimeAttachmentService service, ILogger logger) : Command<AttachOptions> {
	/// <inheritdoc/>
	public override int Execute(AttachOptions options) {
		try { service.Attach(options); return 0; }
		catch (Exception ex) { logger.WriteError(ex.Message); return 1; }
	}
}

/// <summary>Host-side workspace detachment command.</summary>
public class DetachCommand(IRuntimeAttachmentService service, ILogger logger) : Command<DetachOptions> {
	/// <inheritdoc/>
	public override int Execute(DetachOptions options) {
		try { service.Detach(options.Workspace); return 0; }
		catch (Exception ex) { logger.WriteError(ex.Message); return 1; }
	}
}
