using System.IO;
using Clio.Common.McpWorker;
using Clio.UserEnvironment;

namespace Clio.Common.Skills;

/// <summary>
/// Starts the scheduled toolkit auto-update as a detached <c>clio update-toolkit</c> process, so the command
/// the user asked for never waits for it.
/// </summary>
public interface IToolkitAutoUpdateLauncher {
	/// <summary>
	/// Launches the detached update process and returns immediately.
	/// </summary>
	/// <returns><c>true</c> when the process started; otherwise <c>false</c>.</returns>
	/// <remarks>
	/// The child's output is discarded: it outlives the command that launched it, and anything it wrote
	/// would land in the terminal after the prompt returned, or in an MCP host's JSON-RPC stream. The child
	/// does not run its own startup update check, because <c>update-toolkit</c> is exempt from it.
	/// </remarks>
	bool Launch();
}

/// <inheritdoc />
public sealed class ToolkitAutoUpdateLauncher(
	IClioExecutablePathProvider executablePathProvider,
	IProcessExecutor processExecutor) : IToolkitAutoUpdateLauncher {
	/// <summary>The command the detached child runs.</summary>
	internal const string UpdateCommand = "update-toolkit";

	/// <inheritdoc />
	public bool Launch() {
		// Through the path provider, not "clio" on PATH: under `dotnet clio.dll` the running build is not the
		// one on PATH, and the muxer needs the assembly as its first argument.
		ClioWorkerLaunchDescriptor descriptor = executablePathProvider.Resolve(UpdateCommand);
		ProcessLaunchResult result = processExecutor.FireAndForgetAsync(new ProcessExecutionOptions(descriptor.Executable, string.Empty) {
			ArgumentList = descriptor.Arguments,
			// The settings folder, like the clio self-update: never the tool's install folder, which a later
			// `dotnet tool update` has to replace.
			WorkingDirectory = Path.GetFullPath(SettingsRepository.AppSettingsFolderPath),
			DiscardOutput = true
		}).GetAwaiter().GetResult();
		return result.Started;
	}
}
