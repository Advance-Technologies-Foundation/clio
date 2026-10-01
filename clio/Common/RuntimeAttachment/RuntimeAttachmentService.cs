using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Command;

namespace Clio.Common.RuntimeAttachment;

/// <summary>Manages host-side attachment sessions without owning runtime deployment.</summary>
public interface IRuntimeAttachmentService {
	/// <summary>Attaches or resumes a local workspace.</summary>
	void Attach(AttachOptions options);
	/// <summary>Stops an attachment, retaining runtime links and files so the deployed application remains usable.</summary>
	void Detach(string workspace);
}

/// <inheritdoc/>
public class RuntimeAttachmentService(IEnumerable<IRuntimeAttachmentProvider> providers,
	IAttachmentProcess process, System.IO.Abstractions.IFileSystem files, ILogger logger) : IRuntimeAttachmentService {
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private IRuntimeAttachmentProvider Provider(string name) => providers.SingleOrDefault(p => p.Name == name)
		?? throw new InvalidOperationException($"Unsupported attachment provider '{name}'.");

	private string Workspace(string value) {
		string path = files.Path.GetFullPath(value);
		if (!files.File.Exists(files.Path.Combine(path, ".clio", "workspaceSettings.json")) ||
			!files.Directory.Exists(files.Path.Combine(path, "packages"))) {
			throw new InvalidOperationException("Expected an existing Clio workspace. Run clio createw first.");
		}
		return files.Path.TrimEndingDirectorySeparator(path);
	}

	private void Preflight() {
		foreach ((string executable, string[] arguments, string instruction) in new[] {
			("mutagen", new[] { "version" }, "Install Mutagen locally and add it to PATH: https://mutagen.io/documentation/introduction/installation/"),
			("ssh", new[] { "-V" }, "Install OpenSSH locally and add it to PATH.")
		}) {
			try {
				string output = process.Run(executable, arguments);
				if (executable == "mutagen" && (!Version.TryParse(output, out Version version) || version < new Version(0, 18))) {
					throw new InvalidOperationException("Mutagen 0.18 or later is required.");
				}
			}
			catch (Exception) { throw new InvalidOperationException(instruction + (executable == "mutagen" ? " Version 0.18 or later is required." : "")); }
		}
	}

	private string ReceiptPath(string workspace) => files.Path.Combine(workspace, ".clio", "attachment.local.json");

	private AttachmentReceipt Read(string path) {
		AttachmentReceipt receipt = JsonSerializer.Deserialize<AttachmentReceipt>(files.File.ReadAllText(path));
		if (receipt?.Version != 1 || receipt.Target == null ||
			!System.Text.RegularExpressions.Regex.IsMatch(receipt.Session ?? "", "^clio-[a-f0-9]{32}$") ||
			receipt.RemoteWorkspace != receipt.Target.WorkspaceRoot.TrimEnd('/') + "/" + receipt.Session) {
			throw new InvalidOperationException("Invalid attachment receipt. No resources were changed.");
		}
		return receipt;
	}

	private void Save(string path, AttachmentReceipt receipt) {
		string temporary = path + ".tmp";
		files.File.WriteAllText(temporary, JsonSerializer.Serialize(receipt, JsonOptions));
		files.File.Move(temporary, path, true);
	}

	private bool HasSession(AttachmentReceipt receipt) {
		using JsonDocument sessions = JsonDocument.Parse(process.Run("mutagen", ["sync", "list", "--template", "{{json .}}"]));
		JsonElement[] matches = sessions.RootElement.EnumerateArray()
			.Where(s => s.TryGetProperty("name", out JsonElement name) && name.GetString() == receipt.Session).ToArray();
		if (matches.Length == 0) { return false; }
		if (matches.Length != 1 || matches[0].GetProperty("alpha").GetProperty("path").GetString() !=
			receipt.Workspace ||
			matches[0].GetProperty("beta").GetProperty("host").GetString() != receipt.SshAlias ||
			matches[0].GetProperty("beta").GetProperty("path").GetString() != receipt.RemoteWorkspace) {
			throw new InvalidOperationException("Mutagen session ownership does not match the attachment receipt.");
		}
		return true;
	}

	private void Flush(AttachmentReceipt receipt) {
		process.Run("mutagen", ["sync", "flush", receipt.Session]);
		using JsonDocument sessions = JsonDocument.Parse(process.Run("mutagen", ["sync", "list", receipt.Session, "--template", "{{json .}}"]));
		JsonElement session = sessions.RootElement.EnumerateArray().Single();
		foreach (string endpointName in new[] { "alpha", "beta" }) {
			JsonElement endpoint = session.GetProperty(endpointName);
			if (!endpoint.TryGetProperty("connected", out JsonElement connected) || !connected.GetBoolean() ||
				!endpoint.TryGetProperty("scanned", out JsonElement scanned) || !scanned.GetBoolean() ||
				new[] { "scanProblems", "transitionProblems" }.Any(name =>
					endpoint.TryGetProperty(name, out JsonElement problems) && problems.GetArrayLength() > 0)) {
				throw new InvalidOperationException("Synchronization endpoint is not ready. Inspect mutagen sync list --long; files are preserved.");
			}
		}
		if ((session.TryGetProperty("conflicts", out JsonElement conflicts) && conflicts.GetArrayLength() > 0) ||
			(session.TryGetProperty("lastError", out JsonElement error) && !string.IsNullOrEmpty(error.GetString()))) {
			throw new InvalidOperationException("Synchronization has unresolved conflicts or errors. Both copies are preserved; inspect mutagen sync list --long.");
		}
	}

	/// <inheritdoc/>
	public void Attach(AttachOptions options) {
		IRuntimeAttachmentProvider provider = Provider(options.Provider);
		if (options.Mode != "develop") { throw new InvalidOperationException("Only --mode develop is supported."); }
		string workspace = Workspace(options.Workspace), path = ReceiptPath(workspace);
		Preflight();
		provider.CheckDependencies();
		using Stream workspaceLock = files.File.Open(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		RuntimeTarget target = provider.Discover(options);
		AttachmentReceipt receipt = null;
		if (files.File.Exists(path)) {
			receipt = Read(path);
			if (receipt.Provider != provider.Name || receipt.Workspace != workspace || receipt.SshAlias != options.SshAlias ||
				receipt.Target.Uid != target.Uid || receipt.Target.Context != target.Context || receipt.Target.Namespace != target.Namespace) {
				if (!receipt.Detached) { throw new InvalidOperationException("This workspace is already attached elsewhere. Detach it first."); }
				receipt = null;
			}
		}
		if (receipt == null) {
			string session = "clio-" + Guid.NewGuid().ToString("N");
			receipt = new() { Provider = provider.Name, Workspace = workspace, Target = target,
				SshAlias = options.SshAlias, Session = session, RemoteWorkspace = target.WorkspaceRoot.TrimEnd('/') + "/" + session };
		}
		receipt.Detached = false;
		receipt.Packages = files.Directory.GetDirectories(files.Path.Combine(workspace, "packages"))
			.Select(files.Path.GetFileName).ToArray();
		Save(path, receipt);
		provider.Prepare(receipt);
		process.Run("mutagen", ["daemon", "start"]);
		if (!HasSession(receipt)) {
			process.Run("mutagen", ["sync", "create", "--name", receipt.Session, "--mode", "two-way-safe",
				"--no-global-configuration", "--ignore-vcs", "--ignore", "bin", "--ignore", "obj",
				"--ignore", ".codex", "--ignore", ".claude", "--ignore", ".mcp.json",
				"--ignore", ".clio/attachment.local.json*", "--ignore", ".clio-attachment-owner",
				workspace, receipt.SshAlias + ":" + receipt.RemoteWorkspace]);
		} else { process.Run("mutagen", ["sync", "resume", receipt.Session]); }
		Flush(receipt);
		logger.WriteLine($"Attached. Remote workspace: {receipt.RemoteWorkspace}");
		logger.WriteLine($"Clio MCP: {receipt.Target.ClioMcpUrl}");
		logger.WriteLine("Build on the runtime. Run attach again after adding packages. Agent MCP configuration is not changed.");
	}

	/// <inheritdoc/>
	public void Detach(string workspace) {
		workspace = Workspace(workspace);
		string path = ReceiptPath(workspace);
		if (!files.File.Exists(path)) { logger.WriteLine("Workspace is not attached."); return; }
		Preflight();
		using Stream workspaceLock = files.File.Open(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		AttachmentReceipt receipt = Read(path);
		if (receipt.Workspace != workspace) { throw new InvalidOperationException("Receipt belongs to a different local workspace."); }
		if (receipt.Detached) { logger.WriteLine("Workspace is already detached."); return; }
		IRuntimeAttachmentProvider provider = Provider(receipt.Provider);
		provider.CheckDependencies();
		process.Run("mutagen", ["daemon", "start"]);
		bool hasSession = HasSession(receipt);
		if (hasSession) {
			process.Run("mutagen", ["sync", "pause", receipt.Session]);
			logger.WriteLine("Synchronization stopped. Any pending or conflicting changes remain in their respective workspace copies.");
		}
		provider.Release(receipt);
		if (hasSession) { process.Run("mutagen", ["sync", "terminate", receipt.Session]); }
		receipt.Detached = true;
		Save(path, receipt);
		logger.WriteLine("Detached. Runtime, database and both copies of workspace files are preserved.");
	}
}
