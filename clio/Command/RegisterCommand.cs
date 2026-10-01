using System;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Clio;
using Clio.Common;
using Clio.Common.McpWorker;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>Options for registering Windows Explorer commands.</summary>
[Verb("register", HelpText = "Register clio commands in context menu ")]
public class RegisterOptions{
	#region Properties: Public

	/// <summary>Gets or sets the legacy registration target option.</summary>
	[Option('t', "Target", Default = "u", HelpText = "Target environment location. Could be user location or" +
													 " machine location. Use 'u' for set user location and 'm' to set machine location.")]
	public string Target { get; set; }

	/// <summary>Gets or sets the legacy installation path option.</summary>
	[Option('p', "Path", HelpText = "Path where clio is stored.")]
	public string Path { get; set; }

	#endregion
}

/// <summary>Registers Explorer commands using the current clio launch descriptor.</summary>
public class RegisterCommand : Command<RegisterOptions>{
	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly IOperationSystem _operationSystem;
	private readonly IProcessExecutor _processExecutor;
	private readonly IClioExecutablePathProvider _clioExecutablePathProvider;

	#endregion

	#region Constructors: Public

	/// <summary>
	///     Initializes a new instance of the <see cref="RegisterCommand" /> class.
	/// </summary>
	/// <param name="logger">Logger used for command output.</param>
	/// <param name="processExecutor">Process executor used to run system commands.</param>
	/// <param name="fileSystem">Filesystem abstraction used for path and file operations.</param>
	/// <param name="operationSystem">Operating system abstraction used for OS and privilege checks.</param>
	/// <param name="clioExecutablePathProvider">Resolves this clio build for direct Explorer launches.</param>
	public RegisterCommand(ILogger logger, IProcessExecutor processExecutor, IFileSystem fileSystem,
		IOperationSystem operationSystem, IClioExecutablePathProvider clioExecutablePathProvider) {
		_logger = logger;
		_processExecutor = processExecutor;
		_fileSystem = fileSystem;
		_operationSystem = operationSystem;
		_clioExecutablePathProvider = clioExecutablePathProvider;
	}

	#endregion

	#region Methods: Private

	/// <summary>
	///     Installs VS Code Extension with force
	/// </summary>
	/// <remarks>
	///     See
	///     <see href="https://code.visualstudio.com/docs/editor/command-line#_working-with-extensions">working with extensions</see>
	///     vscode cli documentation
	/// </remarks>
	private void InstallVsCodeExtension() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			//Check if extension is installed
			string extensions
				= _processExecutor.Execute("cmd.exe", "/c code --list-extensions", true, suppressErrors: true);
			if (extensions.Contains("AdvanceTechnologiesFoundation.clio-explorer",
					StringComparison.OrdinalIgnoreCase)) {
				_logger.WriteLine("clio-explorer is already installed");
				return;
			}

			// install extension
			_processExecutor.Execute("cmd.exe",
				"/c code --install-extension AdvanceTechnologiesFoundation.clio-explorer --force",
				true, suppressErrors: true);
		}
	}

	private bool TryExecuteProcess(string program, string arguments, string operationDescription) {
		ProcessExecutionResult result = _processExecutor.ExecuteAndCaptureAsync(
			new ProcessExecutionOptions(program, arguments) {
				SuppressErrors = true
			}).GetAwaiter().GetResult();

		if (!result.Started) {
			_logger.WriteError($"Failed to {operationDescription}: process was not started. {result.StandardError}");
			return false;
		}

		if (result.ExitCode is not 0) {
			_logger.WriteError(
				$"Failed to {operationDescription}: process exited with code {result.ExitCode}. {result.StandardError}");
			return false;
		}

		return true;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public override int Execute(RegisterOptions options) {
		try {
			if (_operationSystem.IsWindows) {
				if (!_operationSystem.HasAdminRights()) {
					_logger.WriteLine("Clio register command need admin rights.");
					return 1;
				}

				string folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
				string appDataClioFolderPath = _fileSystem.Path.Combine(folder, "clio");
				_fileSystem.Directory.CreateDirectory(appDataClioFolderPath);
				string assemblyFolderPath = AppContext.BaseDirectory;
				string clioIconPath = _fileSystem.Path.Combine(assemblyFolderPath, "img");
				IDirectoryInfo imgFolder = _fileSystem.DirectoryInfo.New(clioIconPath);
				IFileInfo[] allImgFiles = imgFolder.GetFiles();
				foreach (IFileInfo imgFile in allImgFiles) {
					string destImgFilePath = _fileSystem.Path.Combine(appDataClioFolderPath, imgFile.Name);
					imgFile.CopyTo(destImgFilePath, true);
				}

				string unregFileName = _fileSystem.Path.Combine(assemblyFolderPath, "reg",
					"unreg_clio_context_menu_win.reg");
				string regFileName = _fileSystem.Path.Combine(assemblyFolderPath, "reg",
					"clio_context_menu_win.reg");
				ClioWorkerLaunchDescriptor launch = _clioExecutablePathProvider.Resolve();
				if (!_fileSystem.Path.IsPathFullyQualified(launch.Executable)
					|| !_fileSystem.File.Exists(launch.Executable)) {
					_logger.WriteError("Cannot register Explorer deployment: clio executable must be an existing absolute path.");
					return 1;
				}

				string command = string.Join(" ", new[] { launch.Executable }.Concat(launch.Arguments)
					.Select(argument => $"\"{argument}\""));
				string registration = _fileSystem.File.ReadAllText(regFileName).Replace("__CLIO_DEPLOY_LAUNCH__",
					command.Replace("\\", "\\\\").Replace("\"", "\\\""), StringComparison.Ordinal);
				string generatedRegFileName = _fileSystem.Path.Combine(appDataClioFolderPath,
					"clio_context_menu_win.reg");
				_fileSystem.File.WriteAllText(generatedRegFileName, registration, Encoding.Unicode);
				if (!TryExecuteProcess("reg.exe", $"import \"{unregFileName}\"",
						"import unregister context menu registry file")) {
					return 1;
				}

				if (!TryExecuteProcess("reg.exe", $"import \"{generatedRegFileName}\"",
						"import context menu registry file")) {
					return 1;
				}

				_logger.WriteLine("Clio context menu successfully registered.");
				return 0;
			}

			_logger.WriteLine("Clio register command is only supported on: 'windows'.");
			return 1;
		}
		catch (Exception e) {
			_logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			return 1;
		}
	}

	#endregion
}
