using System;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using Clio;
using Clio.Common;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

[Verb("register", HelpText = "Register clio commands in context menu ")]
public class RegisterOptions{
	#region Properties: Public

	[Option('t', "Target", Default = "u", HelpText = "Target environment location. Could be user location or" +
													 " machine location. Use 'u' for set user location and 'm' to set machine location.")]
	public string Target { get; set; }

	[Option('p', "Path", HelpText = "Path where clio is stored.")]
	public string Path { get; set; }

	#endregion
}

public class RegisterCommand : Command<RegisterOptions>{
	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly IMacOsFinderIntegration _macOsFinderIntegration;
	private readonly IMacOsMenuBarIntegration _macOsMenuBarIntegration;
	private readonly IOperationSystem _operationSystem;
	private readonly IProcessExecutor _processExecutor;

	#endregion

	#region Constructors: Public

	/// <summary>
	///     Initializes a new instance of the <see cref="RegisterCommand" /> class.
	/// </summary>
	/// <param name="logger">Logger used for command output.</param>
	/// <param name="processExecutor">Process executor used to run system commands.</param>
	/// <param name="fileSystem">Filesystem abstraction used for path and file operations.</param>
	/// <param name="operationSystem">Operating system abstraction used for OS and privilege checks.</param>
	/// <param name="macOsFinderIntegration">Installer for the macOS Finder "Deploy Creatio" Quick Action.</param>
	/// <param name="macOsMenuBarIntegration">Installer for the macOS clio menu bar app.</param>
	public RegisterCommand(ILogger logger, IProcessExecutor processExecutor, IFileSystem fileSystem,
		IOperationSystem operationSystem, IMacOsFinderIntegration macOsFinderIntegration,
		IMacOsMenuBarIntegration macOsMenuBarIntegration) {
		_logger = logger;
		_processExecutor = processExecutor;
		_fileSystem = fileSystem;
		_operationSystem = operationSystem;
		_macOsFinderIntegration = macOsFinderIntegration;
		_macOsMenuBarIntegration = macOsMenuBarIntegration;
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
				if (!TryExecuteProcess("cmd", $"/c reg import \"{unregFileName}\"",
						"import unregister context menu registry file")) {
					return 1;
				}

				if (!TryExecuteProcess("cmd", $"/c reg import \"{regFileName}\"",
						"import context menu registry file")) {
					return 1;
				}

				_logger.WriteLine("Clio context menu successfully registered.");
				return 0;
			}

			if (_operationSystem.IsMacOS) {
				_macOsFinderIntegration.InstallAsync().GetAwaiter().GetResult();
				_macOsMenuBarIntegration.InstallAsync().GetAwaiter().GetResult();
				_logger.WriteLine(
					"Clio Finder 'Deploy Creatio' quick action and menu bar app registered.");
				_logger.WriteLine(
					"Opening System Settings so you can enable the quick action under Services > Files and Folders.");
				_processExecutor.Execute("open",
					"\"x-apple.systempreferences:com.apple.preference.keyboard?Shortcuts\"",
					waitForExit: false, suppressErrors: true);
				return 0;
			}

			_logger.WriteLine("Clio register command is only supported on: 'windows' and 'macos'.");
			return 1;
		}
		catch (Exception e) {
			_logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			return 1;
		}
	}

	#endregion
}
