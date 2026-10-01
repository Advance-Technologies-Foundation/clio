using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Clio.Command;
using Clio.Common.RuntimeAttachment;
using Newtonsoft.Json.Linq;

namespace Clio.Common.OperatorBootstrap;

/// <summary>Builds the operator's attachable image templates into Rancher's local image store.</summary>
public interface ILocalRuntimeImageBuilder {
	/// <summary>Prepares the ZIP with the versioned operator toolchain and builds base, database and Dev images.</summary>
	void Build(RuntimeOptions options);
}

/// <inheritdoc/>
public class LocalRuntimeImageBuilder(IRancherDesktopTarget target, IAttachmentProcess process,
	IProcessExecutor executor, ILogger logger) : ILocalRuntimeImageBuilder {
	private const string PrepImage = "registry.krylov.cloud/creatio-clio-prep@sha256:4594e27118d50e107d03809d6c56613f059cd6173ecc07c8dd6ab86966a73e57";
	private const string BaseImage = "creatio-base:8.0-v1";

	/// <inheritdoc/>
	public void Build(RuntimeOptions options) {
		if (!OperatingSystem.IsWindows()) {
			throw new InvalidOperationException("The local runtime build prototype currently supports Windows Rancher Desktop only.");
		}
		if (string.IsNullOrWhiteSpace(options.Source) || !File.Exists(options.Source)) {
			throw new ArgumentException("Build requires --from pointing to an existing Creatio ZIP.");
		}
		string source = Path.GetFullPath(options.Source);
		if (source.Length < 3 || source[1] != ':' || source.Contains(',')) {
			throw new ArgumentException("Use a local drive path without commas for the Creatio ZIP.");
		}
		string runtime = DetectRuntime(source);
		target.Verify(options.Context);
		string volume = "clio-runtime-build-" + Guid.NewGuid().ToString("N");
		string container = volume + "-prep";
		string vmDirectory = "/mnt/" + char.ToLowerInvariant(source[0]) + Path.GetDirectoryName(source)[2..].Replace('\\', '/');
		logger.WriteInfo($"Building Creatio .NET {runtime} into context={options.Context}; no image push.");
		process.Run("rdctl", ["shell", "docker", "volume", "create", volume]);
		try {
			Run(["run", "--rm", "--name", container,
				"--mount", $"type=bind,src={vmDirectory},dst=/input,readonly",
				"--mount", $"type=volume,src={volume},dst=/work", PrepImage,
				"build-docker-image", "--from", "/input/" + Path.GetFileName(source),
				"--template", "base,db,dev", "--base-image", BaseImage, "--prepare-context-only", "--output-path", "/work/context"]);
			string plan = process.Run("rdctl", ["shell", "docker", "run", "--rm", "--entrypoint", "cat",
				"--mount", $"type=volume,src={volume},dst=/work", PrepImage, "/work/context/plan.tsv"]);
			string mountpoint = process.Run("rdctl", ["shell", "docker", "volume", "inspect", volume, "--format", "{{.Mountpoint}}"]);
			foreach (string line in plan.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
				string[] columns = line.TrimEnd('\r').Split('\t');
				if (columns.Length != 4 || !columns[0].StartsWith("/work/context/", StringComparison.Ordinal) || columns[0].Contains("..")) {
					throw new InvalidOperationException("Unexpected image build plan returned by the preparation image.");
				}
				List<string> args = ["build", "--tag", columns[1]];
				foreach (string value in columns.Skip(2).SelectMany(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries))) {
					if (value == "-") { continue; }
					if (value.StartsWith("build-arg:", StringComparison.Ordinal)) { args.AddRange(["--build-arg", value[10..]]); }
					else if (value.StartsWith("label:", StringComparison.Ordinal)) { args.AddRange(["--label", value[6..]]); }
					else { throw new InvalidOperationException("Unexpected image build option from preparation image."); }
				}
				if (columns[1].StartsWith("creatio-dev:", StringComparison.Ordinal)) {
					args.AddRange(["--build-arg", "DOTNET_VERSION=" + runtime, "--label", "org.creatio.runtime.dotnet=" + runtime]);
				}
				args.Add(mountpoint + columns[0][5..]);
				logger.WriteInfo("Building " + columns[1]);
				Run(args);
				logger.WriteInfo("Available locally: " + columns[1]);
			}
		} finally {
			// Remove only this invocation's staging container/volume, never images or shared caches.
			try { process.Run("rdctl", ["shell", "docker", "rm", "-f", container]); }
			catch (InvalidOperationException) { /* --rm normally removed it already. */ }
			try { process.Run("rdctl", ["shell", "docker", "volume", "rm", volume]); }
			catch (InvalidOperationException) { logger.WriteWarning($"Build staging volume remains: {volume}. Inspect it before removal."); }
		}
	}

	internal static string DetectRuntime(string path) {
		using ZipArchive zip = ZipFile.OpenRead(path);
		ZipArchiveEntry[] entries = zip.Entries.Where(e => Path.GetFileName(e.FullName.Replace('\\', '/')) == "Terrasoft.WebHost.runtimeconfig.json").ToArray();
		if (entries.Length != 1 || entries[0].Length > 1024 * 1024) {
			throw new InvalidOperationException("ZIP must contain exactly one Terrasoft.WebHost.runtimeconfig.json of at most 1 MiB.");
		}
		using StreamReader reader = new(entries[0].Open());
		return JObject.Parse(reader.ReadToEnd())["runtimeOptions"]?.Value<string>("tfm") switch {
			"net8.0" => "8.0", "net10.0" => "10.0",
			_ => throw new InvalidOperationException("Supported Creatio runtimes: net8.0 and net10.0.")
		};
	}

	private void Run(IReadOnlyList<string> args) {
		ProcessExecutionResult result = executor.ExecuteAndCaptureAsync(new("rdctl", "") {
			ArgumentList = new[] { "shell", "docker" }.Concat(args).ToArray(),
			Timeout = TimeSpan.FromHours(1), MaximumCapturedOutputCharacters = 1024 * 1024,
			MirrorOutputToLogger = true, SuppressErrors = true
		}).GetAwaiter().GetResult();
		if (result.ExitCode != 0 || result.TimedOut || result.Canceled) {
			throw new InvalidOperationException("Local image build failed: " + result.StandardError);
		}
	}
}
