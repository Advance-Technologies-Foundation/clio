using System.IO.Abstractions.TestingHelpers;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class PageOutputDirectoryResolverTests {

	private static string Resolve(
		MockFileSystem fs, string cwd, string home, string fallback, string? explicitDir) =>
		PageOutputDirectoryResolver.ResolveAnchor(fs, cwd, home, fallback, explicitDir);

	[Test]
	[Description("An explicit output-directory wins over every fallback, even when cwd is the home directory.")]
	public void ResolveAnchor_WhenExplicitDirectoryGiven_HonorsItRegardlessOfCwd() {
		MockFileSystem fs = new();
		string baseDir = fs.Directory.GetCurrentDirectory();
		string home = fs.Path.Combine(baseDir, "home");
		string explicitDir = fs.Path.Combine(baseDir, "explicit-out");

		string anchor = Resolve(fs, cwd: home, home: home,
			fallback: fs.Path.Combine(baseDir, "cliohome"), explicitDir: explicitDir);

		anchor.Should().Be(fs.Path.GetFullPath(explicitDir),
			because: "an explicit output-directory must be honored regardless of the current directory");
	}

	[Test]
	[Description("Walks up from a nested cwd to the workspace root identified by .clio/workspaceSettings.json.")]
	public void ResolveAnchor_WhenWorkspaceMarkerInAncestor_ReturnsWorkspaceRoot() {
		MockFileSystem fs = new();
		string baseDir = fs.Directory.GetCurrentDirectory();
		string wsRoot = fs.Path.Combine(baseDir, "ws");
		fs.AddFile(fs.Path.Combine(wsRoot, ".clio", "workspaceSettings.json"), new MockFileData("{}"));
		string cwd = fs.Path.Combine(wsRoot, "packages", "UsrPkg");
		fs.AddDirectory(cwd);

		string anchor = Resolve(fs, cwd, home: fs.Path.Combine(baseDir, "home"),
			fallback: fs.Path.Combine(baseDir, "cliohome"), explicitDir: null);

		anchor.Should().Be(fs.Path.GetFullPath(wsRoot),
			because: "output must anchor at the workspace root, not the raw nested cwd");
	}

	[Test]
	[Description("A plain project directory (no workspace marker, not home) keeps the cwd-relative behavior.")]
	public void ResolveAnchor_WhenPlainProjectDir_ReturnsCwd() {
		MockFileSystem fs = new();
		string baseDir = fs.Directory.GetCurrentDirectory();
		string cwd = fs.Path.Combine(baseDir, "proj");
		fs.AddDirectory(cwd);

		string anchor = Resolve(fs, cwd, home: fs.Path.Combine(baseDir, "home"),
			fallback: fs.Path.Combine(baseDir, "cliohome"), explicitDir: null);

		anchor.Should().Be(cwd,
			because: "a non-home project directory with no workspace marker keeps the current cwd-relative behavior");
	}

	[Test]
	[Description("When cwd is the home directory and no workspace is found, anchors at the managed clio home fallback, never the bare $HOME.")]
	public void ResolveAnchor_WhenCwdIsHomeAndNoWorkspace_ReturnsFallbackNotHome() {
		MockFileSystem fs = new();
		string baseDir = fs.Directory.GetCurrentDirectory();
		string home = fs.Path.Combine(baseDir, "home");
		string fallback = fs.Path.Combine(baseDir, "cliohome");

		string anchor = Resolve(fs, cwd: home, home: home, fallback: fallback, explicitDir: null);

		anchor.Should().Be(fallback,
			because: "output must fall back to the managed home root and never litter the bare home directory");
		anchor.Should().NotBe(home);
	}

	[Test]
	[Description("An orphaned .clio directory without workspaceSettings.json (e.g. a pre-consolidation ~/.clio cache) above a project dir is NOT treated as a workspace root.")]
	public void ResolveAnchor_WhenOrphanedClioDirAboveProjectDir_DoesNotTreatAsWorkspace() {
		MockFileSystem fs = new();
		string baseDir = fs.Directory.GetCurrentDirectory();
		string outer = fs.Path.Combine(baseDir, "outer");
		// Orphaned .clio directory WITHOUT the workspaceSettings.json marker file.
		fs.AddDirectory(fs.Path.Combine(outer, ".clio"));
		string cwd = fs.Path.Combine(outer, "proj");
		fs.AddDirectory(cwd);

		string anchor = Resolve(fs, cwd, home: fs.Path.Combine(baseDir, "home"),
			fallback: fs.Path.Combine(baseDir, "cliohome"), explicitDir: null);

		anchor.Should().Be(cwd,
			because: "a bare .clio directory without workspaceSettings.json must not masquerade as a workspace root");
	}

	[Test]
	[Description("The workspace marker directly in the cwd anchors at the cwd.")]
	public void ResolveAnchor_WhenWorkspaceMarkerInCwd_ReturnsCwd() {
		MockFileSystem fs = new();
		string baseDir = fs.Directory.GetCurrentDirectory();
		string wsRoot = fs.Path.Combine(baseDir, "ws");
		fs.AddFile(fs.Path.Combine(wsRoot, ".clio", "workspaceSettings.json"), new MockFileData("{}"));

		string anchor = Resolve(fs, cwd: wsRoot, home: fs.Path.Combine(baseDir, "home"),
			fallback: fs.Path.Combine(baseDir, "cliohome"), explicitDir: null);

		anchor.Should().Be(fs.Path.GetFullPath(wsRoot),
			because: "a cwd that is itself the workspace root anchors there");
	}
}
