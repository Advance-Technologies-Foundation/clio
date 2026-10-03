using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Clio.Common.RuntimeAttachment;
using Clio.Common.Studio;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture, Category("Integration"), Property("Module", "Common"), NonParallelizable]
public class StudioCheckoutIntegrationTests {
	private string _root;
	private string _remote;
	private string _commit;
	private StudioCheckout _checkout;
	private LocalGit _git;
	private sealed class LocalGit(string remote) : IAttachmentProcess {
		public string Run(string executable, IReadOnlyList<string> arguments, string input = null) {
			var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
			// Exercise real Git without a network dependency. Keep origin's recorded URL unchanged.
			if (arguments.Contains("clone") || arguments.Contains("fetch")) {
				start.ArgumentList.Add("-c"); start.ArgumentList.Add("url." + remote.Replace('\\', '/') + ".insteadOf=https://studio-test.invalid/repo");
			}
			foreach (string arg in arguments) start.ArgumentList.Add(arg);
			using var process = Process.Start(start);
			var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
			if (!process.WaitForExit(30000)) { process.Kill(true); throw new InvalidOperationException("Git test timeout."); }
			if (process.ExitCode != 0) throw new InvalidOperationException(stderr.GetAwaiter().GetResult());
			return stdout.GetAwaiter().GetResult().Trim();
		}
	}
	[SetUp]
	public void SetUp() {
		_root = Path.Combine(Path.GetTempPath(), "clio-studio-test-" + Guid.NewGuid().ToString("N"));
		_remote = Path.Combine(_root, "remote"); Directory.CreateDirectory(_remote);
		_git = new LocalGit(_remote); _checkout = new StudioCheckout(_git);
		Git("init", "--initial-branch=main", _remote);
		File.WriteAllText(Path.Combine(_remote, "source.txt"), "first");
		Git("-C", _remote, "add", ".");
		Git("-C", _remote, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-m", "first");
		_commit = Git("-C", _remote, "rev-parse", "HEAD");
	}
	[TearDown]
	public void TearDown() {
		if (!Directory.Exists(_root)) return;
		foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
		Directory.Delete(_root, true);
	}
	private string Git(params string[] args) => _git.Run("git", args);
	private JObject Profile() => new() { ["sources"] = new JArray(new JObject { ["name"] = "repo", ["url"] = "https://studio-test.invalid/repo", ["branch"] = "main", ["commit"] = _commit, ["path"] = "repo" }) };
	private string Workspace => Path.Combine(_root, "workspace");

	[Test, Description("Real Git creates a detached pinned worktree and preserves dirty files on retry without Kubernetes.")]
	public void Checkout_ShouldPinCommitAndPreserveDirtyRetry() {
		Directory.CreateDirectory(Path.Combine(Workspace, "repo"));
		_checkout.Checkout(Profile(), Workspace);
		string worktree = Path.Combine(Workspace, "repo");
		Git("-C", worktree, "rev-parse", "HEAD").Should().Be(_commit, "checkout must use the recorded commit");
		File.WriteAllText(Path.Combine(worktree, "source.txt"), "local edit");
		_checkout.Checkout(Profile(), Workspace);
		File.ReadAllText(Path.Combine(worktree, "source.txt")).Should().Be("local edit", "retry must preserve developer work");
	}

	[TestCase(true), TestCase(false), Description("A different cache origin or worktree HEAD is refused without resetting local work.")]
	public void Checkout_ShouldRejectMismatchedRepositoryOrHead(bool changeOrigin) {
		_checkout.Checkout(Profile(), Workspace);
		string worktree = Path.Combine(Workspace, "repo");
		if (changeOrigin) Git("--git-dir", Path.Combine(Workspace, ".studio-repositories", "repo.git"), "remote", "set-url", "origin", "https://other.invalid/repo");
		else {
			File.WriteAllText(Path.Combine(worktree, "source.txt"), "second"); Git("-C", worktree, "add", ".");
			Git("-C", worktree, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-m", "local");
		}
		string head = Git("-C", worktree, "rev-parse", "HEAD");
		Action act = () => _checkout.Checkout(Profile(), Workspace);
		act.Should().Throw<InvalidOperationException>("the handoff must not adopt or reset a mismatching checkout");
		Git("-C", worktree, "rev-parse", "HEAD").Should().Be(head, "failure must leave existing commits intact");
	}

	[Test, Description("A workspace symbolic link cannot redirect checkout outside the selected directory.")]
	public void Checkout_ShouldRejectLinkedWorkspace() {
		string outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
		Directory.CreateSymbolicLink(Workspace, outside);
		try {
			Action act = () => _checkout.Checkout(Profile(), Workspace);
			act.Should().Throw<ArgumentException>("workspace traversal must not follow symbolic links");
			Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty("the link target must remain untouched");
		} finally { Directory.Delete(Workspace); }
	}
}
