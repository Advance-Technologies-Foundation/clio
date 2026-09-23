namespace Clio.Common.RuntimeAttachment;

/// <summary>Durable ownership record for one local workspace attachment.</summary>
public sealed class AttachmentReceipt {
	/// <summary>Whether synchronization was explicitly stopped; retained for safe reattachment.</summary>
	public bool Detached { get; set; }
	/// <summary>Receipt schema version.</summary>
	public int Version { get; set; } = 1;
	/// <summary>Provider identifier.</summary>
	public string Provider { get; set; }
	/// <summary>Canonical local workspace.</summary>
	public string Workspace { get; set; }
	/// <summary>Unique Mutagen session name.</summary>
	public string Session { get; set; }
	/// <summary>Trusted SSH alias.</summary>
	public string SshAlias { get; set; }
	/// <summary>Remote workspace path.</summary>
	public string RemoteWorkspace { get; set; }
	/// <summary>Owned package names.</summary>
	public string[] Packages { get; set; } = [];
	/// <summary>Discovered provider resource identity.</summary>
	public RuntimeTarget Target { get; set; }
}

/// <summary>Operator runtime connection and identity information; contains no credentials.</summary>
public sealed class RuntimeTarget {
	/// <summary>Kubernetes context.</summary>
	public string Context { get; set; }
	/// <summary>Kubernetes namespace.</summary>
	public string Namespace { get; set; }
	/// <summary>Custom resource name.</summary>
	public string Name { get; set; }
	/// <summary>Custom resource immutable identity.</summary>
	public string Uid { get; set; }
	/// <summary>Deployment name.</summary>
	public string Deployment { get; set; }
	/// <summary>Workspace parent directory.</summary>
	public string WorkspaceRoot { get; set; }
	/// <summary>FSM package directory.</summary>
	public string PackageRoot { get; set; }
	/// <summary>Clio HTTP MCP URL supplied by the operator.</summary>
	public string ClioMcpUrl { get; set; }
	/// <summary>Creatio URL supplied by the operator.</summary>
	public string CreatioUrl { get; set; }
}
