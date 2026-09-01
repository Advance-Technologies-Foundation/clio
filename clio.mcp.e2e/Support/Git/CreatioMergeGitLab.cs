using System.Diagnostics;
using System.Security.Cryptography;

namespace Clio.Mcp.E2E.Support.Git;

internal sealed class CreatioMergeGitLab {
	private static readonly string GitExecutable = ResolveGitExecutable();
	internal const string MetadataRelativePath =
		"packages/UsrMergeProof/Schemas/UsrMergeProofEntity/metadata.json";
	internal const string DescriptorRelativePath =
		"packages/UsrMergeProof/Schemas/UsrMergeProofEntity/descriptor.json";

	private CreatioMergeGitLab(
		string rootPath,
		string mainWorkspace,
		string developerAWorkspace,
		string developerBWorkspace,
		string baseCommit,
		string developerACommit,
		string developerBCommit,
		GitStageFiles metadataStages) {
		RootPath = rootPath;
		MainWorkspace = mainWorkspace;
		DeveloperAWorkspace = developerAWorkspace;
		DeveloperBWorkspace = developerBWorkspace;
		BaseCommit = baseCommit;
		DeveloperACommit = developerACommit;
		DeveloperBCommit = developerBCommit;
		MetadataStages = metadataStages;
	}

	internal string RootPath { get; }
	internal string MainWorkspace { get; }
	internal string DeveloperAWorkspace { get; }
	internal string DeveloperBWorkspace { get; }
	internal string BaseCommit { get; }
	internal string DeveloperACommit { get; }
	internal string DeveloperBCommit { get; }
	internal GitStageFiles MetadataStages { get; }
	internal string DescriptorPath =>
		Path.Combine(MainWorkspace, DescriptorRelativePath.Replace('/', Path.DirectorySeparatorChar));

	internal static async Task<CreatioMergeGitLab> CreateAsync(
		string rootPath,
		string fixtureRoot,
		CancellationToken cancellationToken,
		CreatioMergeGitLabScenario? scenario = null) {
		scenario ??= CreatioMergeGitLabScenario.ColumnTypeConflict;
		string originPath = Path.Combine(rootPath, "origin.git");
		string mainWorkspace = Path.Combine(rootPath, "main");
		string developerAWorkspace = Path.Combine(rootPath, "developer-a");
		string developerBWorkspace = Path.Combine(rootPath, "developer-b");
		string stageRoot = Path.Combine(rootPath, "stages");
		Directory.CreateDirectory(stageRoot);

		await RunGitAsync(rootPath, ["init", "--bare", originPath], cancellationToken);
		await RunGitAsync(rootPath, ["init", "--initial-branch=main", mainWorkspace], cancellationToken);
		await ConfigureIdentityAsync(mainWorkspace, "merge-agent", cancellationToken);
		CopyFixture(fixtureRoot, "base-metadata.json", mainWorkspace, MetadataRelativePath);
		CopyFixture(fixtureRoot, "base-descriptor.json", mainWorkspace, DescriptorRelativePath);
		await RunGitAsync(mainWorkspace, ["add", "--all"], cancellationToken);
		await RunGitAsync(mainWorkspace, ["commit", "-m", "base: common EntitySchema"], cancellationToken);
		string baseCommit = await ReadGitAsync(mainWorkspace, ["rev-parse", "HEAD"], cancellationToken);
		await RunGitAsync(mainWorkspace, ["remote", "add", "origin", originPath], cancellationToken);
		await RunGitAsync(mainWorkspace, ["push", "-u", "origin", "main"], cancellationToken);
		await RunGitAsync(originPath, ["symbolic-ref", "HEAD", "refs/heads/main"], cancellationToken);

		await RunGitAsync(rootPath, ["clone", originPath, developerAWorkspace], cancellationToken);
		await ConfigureIdentityAsync(developerAWorkspace, "developer-a", cancellationToken);
		await RunGitAsync(developerAWorkspace, ["checkout", "-b", "developer-a"], cancellationToken);
		CopyFixture(fixtureRoot, scenario.DeveloperAMetadataFixture, developerAWorkspace, MetadataRelativePath);
		await RunGitAsync(developerAWorkspace, ["add", "--all"], cancellationToken);
		await RunGitAsync(developerAWorkspace, ["commit", "-m", scenario.DeveloperACommitMessage], cancellationToken);
		string developerACommit = await ReadGitAsync(developerAWorkspace, ["rev-parse", "HEAD"], cancellationToken);
		await RunGitAsync(developerAWorkspace, ["push", "-u", "origin", "developer-a"], cancellationToken);

		await RunGitAsync(rootPath, ["clone", originPath, developerBWorkspace], cancellationToken);
		await ConfigureIdentityAsync(developerBWorkspace, "developer-b", cancellationToken);
		await RunGitAsync(developerBWorkspace, ["checkout", "-b", "developer-b"], cancellationToken);
		CopyFixture(fixtureRoot, scenario.DeveloperBMetadataFixture, developerBWorkspace, MetadataRelativePath);
		await RunGitAsync(developerBWorkspace, ["add", "--all"], cancellationToken);
		await RunGitAsync(developerBWorkspace, ["commit", "-m", scenario.DeveloperBCommitMessage], cancellationToken);
		string developerBCommit = await ReadGitAsync(developerBWorkspace, ["rev-parse", "HEAD"], cancellationToken);
		await RunGitAsync(developerBWorkspace, ["push", "-u", "origin", "developer-b"], cancellationToken);

		await RunGitAsync(mainWorkspace, ["fetch", "origin"], cancellationToken);
		await RunGitAsync(mainWorkspace, ["merge", "--ff-only", "origin/developer-a"], cancellationToken);
		GitCommandResult mergeResult = await ExecuteGitAsync(
			mainWorkspace,
			["merge", "--no-commit", "--no-ff", "origin/developer-b"],
			cancellationToken);
		if (mergeResult.ExitCode == 0) {
			throw new InvalidOperationException("The deterministic Creatio merge lab did not produce a Git conflict.");
		}

		GitStageFiles metadataStages = await ExtractStagesAsync(
			mainWorkspace,
			MetadataRelativePath,
			stageRoot,
			"metadata",
			cancellationToken);
		return new CreatioMergeGitLab(
			rootPath,
			mainWorkspace,
			developerAWorkspace,
			developerBWorkspace,
			baseCommit,
			developerACommit,
			developerBCommit,
			metadataStages);
	}

	internal async Task<GitStateSnapshot> CaptureStateAsync(CancellationToken cancellationToken) {
		string metadataPath = Path.Combine(MainWorkspace, MetadataRelativePath.Replace('/', Path.DirectorySeparatorChar));
		string descriptorPath = Path.Combine(MainWorkspace, DescriptorRelativePath.Replace('/', Path.DirectorySeparatorChar));
		return new GitStateSnapshot(
			await ReadGitAsync(MainWorkspace, ["rev-parse", "HEAD"], cancellationToken),
			await ReadGitAsync(MainWorkspace, ["status", "--porcelain=v1"], cancellationToken),
			await ReadGitAsync(MainWorkspace, ["ls-files", "-u"], cancellationToken),
			ComputeSha256(metadataPath),
			ComputeSha256(descriptorPath));
	}

	internal Task<string> ReadGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
		ReadGitAsync(MainWorkspace, arguments, cancellationToken);

	internal Task RunGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
		RunGitAsync(MainWorkspace, arguments, cancellationToken);

	private static async Task ConfigureIdentityAsync(
		string workspace,
		string role,
		CancellationToken cancellationToken) {
		await RunGitAsync(workspace, ["config", "core.autocrlf", "false"], cancellationToken);
		await RunGitAsync(workspace, ["config", "user.name", $"clio {role}"], cancellationToken);
		await RunGitAsync(workspace, ["config", "user.email", $"{role}@example.invalid"], cancellationToken);
	}

	private static void CopyFixture(
		string fixtureRoot,
		string fixtureName,
		string workspace,
		string relativePath) {
		string destinationPath = Path.Combine(workspace, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
		File.Copy(Path.Combine(fixtureRoot, fixtureName), destinationPath, overwrite: true);
	}

	private static async Task<GitStageFiles> ExtractStagesAsync(
		string workspace,
		string relativePath,
		string stageRoot,
		string prefix,
		CancellationToken cancellationToken) {
		string basePath = Path.Combine(stageRoot, $"{prefix}-base.txt");
		string oursPath = Path.Combine(stageRoot, $"{prefix}-ours.txt");
		string theirsPath = Path.Combine(stageRoot, $"{prefix}-theirs.txt");
		await File.WriteAllTextAsync(basePath, await ReadStageAsync(workspace, relativePath, 1, cancellationToken), cancellationToken);
		await File.WriteAllTextAsync(oursPath, await ReadStageAsync(workspace, relativePath, 2, cancellationToken), cancellationToken);
		await File.WriteAllTextAsync(theirsPath, await ReadStageAsync(workspace, relativePath, 3, cancellationToken), cancellationToken);
		return new GitStageFiles(basePath, oursPath, theirsPath);
	}

	private static Task<string> ReadStageAsync(
		string workspace,
		string relativePath,
		int stage,
		CancellationToken cancellationToken) =>
		ReadGitAsync(workspace, ["show", $":{stage}:{relativePath}"], cancellationToken, trimOutput: false);

	private static string ComputeSha256(string path) =>
		Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

	private static async Task RunGitAsync(
		string workingDirectory,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken) {
		GitCommandResult result = await ExecuteGitAsync(workingDirectory, arguments, cancellationToken);
		if (result.ExitCode != 0) {
			throw new InvalidOperationException(
				$"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode}: {result.StandardError}");
		}
	}

	private static async Task<string> ReadGitAsync(
		string workingDirectory,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken,
		bool trimOutput = true) {
		GitCommandResult result = await ExecuteGitAsync(workingDirectory, arguments, cancellationToken);
		if (result.ExitCode != 0) {
			throw new InvalidOperationException(
				$"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode}: {result.StandardError}");
		}
		return trimOutput ? result.StandardOutput.Trim() : result.StandardOutput;
	}

	private static async Task<GitCommandResult> ExecuteGitAsync(
		string workingDirectory,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken) {
		ProcessStartInfo startInfo = new() {
			FileName = GitExecutable,
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		foreach (string argument in arguments) {
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = new() { StartInfo = startInfo };
		process.Start();
		Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
		Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
		try {
			await process.WaitForExitAsync(cancellationToken);
		} catch (OperationCanceledException) {
			if (!process.HasExited) {
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync(CancellationToken.None);
			}
			throw;
		}
		return new GitCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
	}

	private static string ResolveGitExecutable() {
		string executableName = OperatingSystem.IsWindows() ? "git.exe" : "git";
		foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			string candidate = Path.Combine(directory, executableName);
			if (File.Exists(candidate)) {
				return Path.GetFullPath(candidate);
			}
		}
		throw new InvalidOperationException($"Could not find {executableName} on PATH for the Git merge E2E lab.");
	}
}

internal sealed record GitStageFiles(string BasePath, string OursPath, string TheirsPath);

internal sealed record CreatioMergeGitLabScenario(
	string DeveloperAMetadataFixture,
	string DeveloperBMetadataFixture,
	string DeveloperACommitMessage,
	string DeveloperBCommitMessage) {
	internal static CreatioMergeGitLabScenario ColumnTypeConflict { get; } = new(
		"number-metadata.json",
		"date-time-metadata.json",
		"developer A: change column type to Number",
		"developer B: change column type to Date Time");
}

internal sealed record GitStateSnapshot(
	string Head,
	string Status,
	string UnmergedEntries,
	string MetadataSha256,
	string DescriptorSha256);

internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
