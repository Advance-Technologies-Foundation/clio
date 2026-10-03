using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common.RuntimeAttachment;
using Newtonsoft.Json.Linq;

namespace Clio.Common.Studio;

/// <summary>Creates local source worktrees independently of Kubernetes and deployment.</summary>
public interface IStudioCheckout {
	/// <summary>Checks out the profile's full source inventory and returns exact resulting SHAs.</summary>
	JObject Checkout(JObject profile, string directory);
}

/// <inheritdoc/>
public class StudioCheckout(IAttachmentProcess process) : IStudioCheckout {
	/// <inheritdoc/>
	public JObject Checkout(JObject profile, string directory) {
		if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Checkout requires --directory.");
		IReadOnlyList<StudioSource> sources = StudioProfile.Sources(profile);
		string root = Path.GetFullPath(directory);
		string cacheRoot = Path.Combine(root, ".studio-repositories");
		RejectLinks(root);
		RejectLinks(cacheRoot);
		foreach (StudioSource source in sources) {
			RejectLinks(Path.Combine(root, source.Path));
			RejectLinks(Path.Combine(cacheRoot, source.Name + ".git"));
			Git(["check-ref-format", "refs/heads/" + source.Branch]);
		}
		Directory.CreateDirectory(cacheRoot);
		JArray result = [];
		foreach (StudioSource source in sources) {
			string cache = Path.Combine(cacheRoot, source.Name + ".git");
			string worktree = Path.Combine(root, source.Path);
			// A preceding parent checkout may have introduced a tracked symlink.
			RejectLinks(cache);
			RejectLinks(worktree);
			if (!Directory.Exists(cache)) {
				if (File.Exists(worktree) || (Directory.Exists(worktree) && Directory.EnumerateFileSystemEntries(worktree).Any())) throw new InvalidOperationException($"Checkout target for {source.Name} already exists without this installation's Git cache.");
				Git(["clone", "--bare", "--filter=blob:none", "--", source.Url, cache]);
			} else {
				if (Git(["--git-dir", cache, "rev-parse", "--is-bare-repository"]) != "true" ||
					Git(["--git-dir", cache, "remote", "get-url", "origin"]) != source.Url) {
					throw new InvalidOperationException($"Git cache for {source.Name} belongs to a different repository.");
				}
			}
			Git(["--git-dir", cache, "fetch", "origin", "+refs/heads/" + source.Branch + ":refs/remotes/origin/" + source.Branch]);
			Git(["--git-dir", cache, "merge-base", "--is-ancestor", source.Commit, "refs/remotes/origin/" + source.Branch]);
			if (Directory.Exists(worktree) && Directory.EnumerateFileSystemEntries(worktree).Any()) {
				string actualCommon = Path.GetFullPath(Git(["-C", worktree, "rev-parse", "--path-format=absolute", "--git-common-dir"]));
				if (!string.Equals(actualCommon, cache, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
					Git(["-C", worktree, "rev-parse", "HEAD"]) != source.Commit) {
					throw new InvalidOperationException($"Existing worktree for {source.Name} does not match the handoff. It was left unchanged.");
				}
			} else {
				RejectLinks(worktree);
				Directory.CreateDirectory(Path.GetDirectoryName(worktree));
				Git(["--git-dir", cache, "worktree", "add", "--detach", "--", worktree, source.Commit]);
			}
			result.Add(new JObject { ["name"] = source.Name, ["path"] = worktree, ["commit"] = Git(["-C", worktree, "rev-parse", "HEAD"]) });
		}
		return new JObject { ["state"] = "Ready", ["sources"] = result };
	}

	private string Git(IReadOnlyList<string> arguments) {
		try {
			return process.Run("git", new[] { "-c", "core.hooksPath=" + (OperatingSystem.IsWindows() ? "NUL" : "/dev/null"), "-c", "credential.interactive=false" }.Concat(arguments).ToArray());
		} catch (InvalidOperationException) {
			throw new InvalidOperationException("Git checkout failed. Check repository access, branch/commit reachability and local worktree ownership. Existing worktrees were not reset.");
		}
	}

	private static void RejectLinks(string path) {
		for (string current = path; current is not null; current = Path.GetDirectoryName(current)) {
			if ((Directory.Exists(current) || File.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) {
				throw new ArgumentException("Source workspace and cache paths must not traverse symbolic links or junctions.");
			}
		}
	}
}
