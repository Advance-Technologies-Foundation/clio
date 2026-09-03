using System;
using System.Collections.Generic;

namespace Clio.Common.McpWorker;

// Declared INSIDE the namespace on purpose: a compilation-unit alias would lose to
// Clio.Common.IFileSystem, which name lookup finds while walking up the enclosing namespaces.
using IFileSystem = System.IO.Abstractions.IFileSystem;

/// <summary>
/// A resolved way to run clio itself: an executable plus the argument vector that must precede the
/// command arguments.
/// </summary>
/// <param name="Executable">Absolute path of the process to start.</param>
/// <param name="Arguments">
/// Leading arguments. Empty for an apphost; a single element — the clio assembly path — when clio runs
/// through the dotnet muxer, because the muxer takes the assembly as its first argument.
/// </param>
/// <param name="WorkingDirectory">Directory the child should start in.</param>
public sealed record ClioWorkerLaunchDescriptor(
	string Executable,
	IReadOnlyList<string> Arguments,
	string WorkingDirectory);

/// <summary>
/// Resolves how to re-launch this clio build as a child process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a service and not an <see cref="Environment.ProcessPath"/> call.</b>
/// <see cref="Environment.ProcessPath"/> returns the DOTNET HOST whenever clio runs as
/// <c>dotnet clio.dll</c> — the ordinary development and CI shape — so a naive spawn would run
/// <c>dotnet mcp-server --worker</c>, with the muxer taking <c>mcp-server</c> for an assembly path,
/// and fail. The two shapes have to be distinguished, and the decision has to be substitutable in
/// tests, so it is an injected service.
/// </para>
/// <para>The logic is the one proven by the MCP end-to-end harness's own clio resolver.</para>
/// </remarks>
public interface IClioExecutablePathProvider {

	/// <summary>Resolves the launch descriptor for this clio build.</summary>
	/// <param name="commandArguments">
	/// Command arguments appended after the descriptor's leading arguments, for example
	/// <c>["mcp-server"]</c>.
	/// </param>
	/// <returns>The descriptor.</returns>
	ClioWorkerLaunchDescriptor Resolve(params string[] commandArguments);
}

/// <inheritdoc />
public sealed class ClioExecutablePathProvider : IClioExecutablePathProvider {

	private const string DotnetMuxerName = "dotnet";

	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="ClioExecutablePathProvider"/> class.
	/// </summary>
	/// <param name="fileSystem">
	/// File-system abstraction used to probe for the dotnet host. Injected rather than reached through
	/// static <c>System.IO</c> calls, per the repository file-system policy.
	/// </param>
	public ClioExecutablePathProvider(IFileSystem fileSystem) {
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	/// <inheritdoc />
	public ClioWorkerLaunchDescriptor Resolve(params string[] commandArguments) {
		IReadOnlyList<string> arguments = commandArguments ?? Array.Empty<string>();
		string processPath = Environment.ProcessPath;
		string assemblyPath = ResolveAssemblyPath();
		string workingDirectory = ResolveWorkingDirectory(assemblyPath, processPath);

		// A published clio is an apphost: the running executable IS clio, so it re-launches itself and
		// the command arguments stand alone. Detected by elimination of the muxer, because an apphost
		// can be renamed and its file name proves nothing.
		if (!string.IsNullOrEmpty(processPath) && !IsDotnetMuxer(processPath)) {
			return new ClioWorkerLaunchDescriptor(processPath, [.. arguments], workingDirectory);
		}

		// Development and CI shape: `dotnet clio.dll`. The child must be the dotnet host with the clio
		// assembly as its FIRST argument, or the muxer reads the command verb as an assembly path.
		if (!string.IsNullOrEmpty(assemblyPath)) {
			return new ClioWorkerLaunchDescriptor(ResolveDotnetHostPath(processPath),
				[assemblyPath, .. arguments], workingDirectory);
		}

		// Single-file publish: the assembly has no on-disk location, so the running executable is the
		// only thing that can be re-launched.
		if (!string.IsNullOrEmpty(processPath)) {
			return new ClioWorkerLaunchDescriptor(processPath, [.. arguments], workingDirectory);
		}

		throw new InvalidOperationException(
			"Unable to resolve how to re-launch clio: neither the process path nor the assembly location is available.");
	}

	private static string ResolveAssemblyPath() {
		// Assembly.Location is an EMPTY string (never null) in a single-file host, which is why this is
		// a string check and not a null check.
		string location = typeof(ClioExecutablePathProvider).Assembly.Location;
		return string.IsNullOrEmpty(location) ? null : location;
	}

	private string ResolveWorkingDirectory(string assemblyPath, string processPath) {
		string anchor = assemblyPath ?? processPath;
		if (string.IsNullOrEmpty(anchor)) {
			return Environment.CurrentDirectory;
		}
		string directory = _fileSystem.Path.GetDirectoryName(anchor);
		return string.IsNullOrEmpty(directory) ? Environment.CurrentDirectory : directory;
	}

	private bool IsDotnetMuxer(string processPath) {
		string fileName = _fileSystem.Path.GetFileNameWithoutExtension(processPath);
		return string.Equals(fileName, DotnetMuxerName, StringComparison.OrdinalIgnoreCase);
	}

	private string ResolveDotnetHostPath(string processPath) {
		// The muxer that is already running this process is by definition the right one.
		if (!string.IsNullOrEmpty(processPath) && IsDotnetMuxer(processPath)) {
			return processPath;
		}

		string hostFileName = OperatingSystem.IsWindows() ? "dotnet.exe" : DotnetMuxerName;
		string dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
		if (!string.IsNullOrWhiteSpace(dotnetRoot)) {
			string fromRoot = _fileSystem.Path.Combine(dotnetRoot, hostFileName);
			if (_fileSystem.File.Exists(fromRoot)) {
				return fromRoot;
			}
		}

		string userHost = _fileSystem.Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", hostFileName);
		if (_fileSystem.File.Exists(userHost)) {
			return userHost;
		}

		// Last resort: let PATH resolution happen at launch. The supervisor resolves a bare name to an
		// absolute file from rooted PATH entries before starting anything.
		return hostFileName;
	}
}
