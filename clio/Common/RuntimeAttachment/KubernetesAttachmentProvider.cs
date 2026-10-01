using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command;

namespace Clio.Common.RuntimeAttachment;

/// <summary>Provider-specific runtime discovery and attachment preparation.</summary>
public interface IRuntimeAttachmentProvider {
	/// <summary>Stable provider identifier saved in the attachment receipt.</summary>
	string Name { get; }
	/// <summary>Checks dependencies specific to this runtime provider before attachment mutations.</summary>
	void CheckDependencies();
	/// <summary>Discovers an existing runtime and verifies the supplied SSH destination.</summary>
	RuntimeTarget Discover(AttachOptions options);
	/// <summary>Creates only absent workspace/package links, refusing ownership collisions.</summary>
	void Prepare(AttachmentReceipt receipt);
	/// <summary>Verifies release ownership while preserving the runtime's FSM links and workspace data.</summary>
	void Release(AttachmentReceipt receipt);
}

/// <inheritdoc/>
public class KubernetesAttachmentProvider(IAttachmentProcess process) : IRuntimeAttachmentProvider {
	/// <inheritdoc/>
	public string Name => "operator-kubernetes";

	/// <inheritdoc/>
	public void CheckDependencies() {
		try { process.Run("kubectl", ["version", "--client"]); }
		catch (Exception) { throw new InvalidOperationException("Install kubectl locally and configure access to the runtime cluster."); }
	}

	private string Kubectl(string context, params string[] arguments) =>
		process.Run("kubectl", new[] { "--context", context }.Concat(arguments).ToArray());

	private string Exec(RuntimeTarget target, string input, params string[] arguments) =>
		process.Run("kubectl", new[] { "--context", target.Context, "-n", target.Namespace,
			"exec", "-i", "deployment/" + target.Deployment, "-c", "creatio", "--" }
			.Concat(arguments).ToArray(), input);

	/// <inheritdoc/>
	public RuntimeTarget Discover(AttachOptions options) {
		if (!Regex.IsMatch(options.SshAlias ?? "", @"^[a-zA-Z0-9][a-zA-Z0-9._-]*$")) {
			throw new InvalidOperationException("--ssh-alias must be an OpenSSH Host alias, not a command or URL.");
		}
		string context = options.Context ?? process.Run("kubectl", ["config", "current-context"]);
		string[] scope = string.IsNullOrEmpty(options.Namespace) ? ["-A"] : ["-n", options.Namespace];
		using JsonDocument resources = JsonDocument.Parse(Kubectl(context,
			new[] { "get", "creatioinstances" }.Concat(scope).Concat(new[] { "-o", "json" }).ToArray()));
		JsonElement[] matches = resources.RootElement.GetProperty("items").EnumerateArray()
			.Where(item => item.GetProperty("metadata").GetProperty("name").GetString() == options.Environment).ToArray();
		if (matches.Length != 1) {
			throw new InvalidOperationException("Expected exactly one matching CreatioInstance. Check the name, context and --namespace.");
		}
		JsonElement metadata = matches[0].GetProperty("metadata"), status = matches[0].GetProperty("status");
		if (status.GetProperty("phase").GetString() != "Ready") {
			throw new InvalidOperationException("The runtime must be Ready before attachment.");
		}
		RuntimeTarget target = new() {
			Context = context, Namespace = metadata.GetProperty("namespace").GetString(),
			Name = options.Environment, Uid = metadata.GetProperty("uid").GetString(),
			Deployment = status.GetProperty("deploymentName").GetString(),
			ClioMcpUrl = status.TryGetProperty("clioMcpUrl", out JsonElement mcp) ? mcp.GetString() : null,
			CreatioUrl = status.GetProperty("serviceUrl").GetString()
		};
		using JsonDocument descriptor = JsonDocument.Parse(Exec(target, null, "cat", "/opt/creatio-attachment/attachment.json"));
		target.WorkspaceRoot = descriptor.RootElement.GetProperty("workspaceRoot").GetString();
		target.PackageRoot = descriptor.RootElement.GetProperty("packageRoot").GetString();
		foreach (string path in new[] { target.WorkspaceRoot, target.PackageRoot }) {
			if (string.IsNullOrEmpty(path) || !path.StartsWith('/') || path == "/" || path.Split('/').Contains("..")) {
				throw new InvalidOperationException("Runtime descriptor contains an invalid absolute path.");
			}
		}
		Exec(target, target.PackageRoot, "python3", "-c", """
import pathlib, sys, xml.etree.ElementTree as ET
root = pathlib.Path(sys.stdin.read()).parent.parent
config = ET.parse(root / 'Terrasoft.WebHost.dll.config')
fsm = config.find('.//fileDesignMode')
if fsm is None or fsm.get('enabled', '').lower() != 'true':
    raise RuntimeError('FSM is disabled. Enable FSM, restart Creatio and export packages before attaching in develop mode.')
""");
		string expectedKey = Exec(target, null, "cat", "/app/.attachment/ssh/ssh_host_ed25519_key.pub");
		string actualKey = process.Run("ssh", ["-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes",
			options.SshAlias, "cat /app/.attachment/ssh/ssh_host_ed25519_key.pub"]);
		if (!expectedKey.Split(' ').Take(2).SequenceEqual(actualKey.Split(' ').Take(2))) {
			throw new InvalidOperationException("The SSH alias does not identify the selected Kubernetes runtime.");
		}
		return target;
	}

	private void VerifyIdentity(RuntimeTarget target) {
		using JsonDocument current = JsonDocument.Parse(Kubectl(target.Context, "-n", target.Namespace,
			"get", "creatioinstance", target.Name, "-o", "json"));
		if (current.RootElement.GetProperty("metadata").GetProperty("uid").GetString() != target.Uid) {
			throw new InvalidOperationException("The runtime was replaced. Refusing to modify a different instance.");
		}
	}

	/// <inheritdoc/>
	public void Prepare(AttachmentReceipt receipt) => UpdateLinks(receipt, false);
	/// <inheritdoc/>
	public void Release(AttachmentReceipt receipt) => UpdateLinks(receipt, true);

	private void UpdateLinks(AttachmentReceipt receipt, bool release) {
		VerifyIdentity(receipt.Target);
		// JSON is stdin data; no user-supplied path is interpolated into executable code.
		string payload = JsonSerializer.Serialize(new {
			workspace = receipt.RemoteWorkspace, packageRoot = receipt.Target.PackageRoot,
			packages = receipt.Packages, owner = receipt.Session, release
		});
		Exec(receipt.Target, payload, "python3", "-c", """
import json, os, pathlib, sys
d = json.load(sys.stdin)
w = pathlib.Path(d['workspace'])
p = pathlib.Path(d['packageRoot'])
marker = w / '.clio-attachment-owner'
if marker.exists() and marker.read_text() != d['owner']:
    raise RuntimeError('Remote workspace belongs to another attachment')
if d['release']:
    if not marker.exists():
        raise RuntimeError('Remote ownership marker is missing; files have been preserved')
else:
    if w.exists() and not marker.exists():
        raise RuntimeError('Unowned remote workspace already exists')
    w.mkdir(parents=True, exist_ok=True)
    marker.write_text(d['owner'])
for name in d['packages']:
    if not name or name in ('.', '..') or '/' in name or '\\' in name:
        raise RuntimeError('Invalid package name')
    link = p / name
    source = w / 'packages' / name
    if not d['release'] and os.path.lexists(link) and (not link.is_symlink() or os.readlink(link) != str(source)):
        raise RuntimeError('Package belongs to another workspace: ' + name)
for name in d['packages']:
    link = p / name
    source = w / 'packages' / name
    if not d['release']:
        source.mkdir(parents=True, exist_ok=True)
        try:
            link.symlink_to(source, target_is_directory=True)
        except FileExistsError:
            if not link.is_symlink() or os.readlink(link) != str(source):
                raise RuntimeError('Package acquired by another workspace: ' + name)
print('Detached; workspace files preserved' if d['release'] else 'Workspace prepared')
""");
	}
}
