namespace Clio.Command;

/// <summary>Options for attaching a local workspace to an existing runtime.</summary>
public class AttachOptions {
	/// <summary>Operator instance name.</summary>
	public string Environment { get; set; }
	/// <summary>Local Clio workspace directory.</summary>
	public string Workspace { get; set; }
	/// <summary>Attachment provider.</summary>
	public string Provider { get; set; }
	/// <summary>Kubernetes context; defaults to the current context.</summary>
	public string Context { get; set; }
	/// <summary>Instance namespace; discovery fails if the name is ambiguous.</summary>
	public string Namespace { get; set; }
	/// <summary>Existing OpenSSH alias with key authentication and a trusted host key.</summary>
	public string SshAlias { get; set; }
	/// <summary>Attachment mode.</summary>
	public string Mode { get; set; }
}
