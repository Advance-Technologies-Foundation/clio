namespace Clio.Command {
	using System;
	using Clio.Common;
	using Clio.Package;
	using CommandLine;

	#region class: InstallOptions

	/// <summary>
	/// Shared install options for application and package installation commands.
	/// </summary>
	public class InstallOptions : EnvironmentOptions {
		/// <summary>
		/// Gets or sets the application package path or name.
		/// </summary>
		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name { get; set; }

		/// <summary>
		/// Gets or sets the optional report log path.
		/// </summary>
		[Option('r', "ReportPath", Required = false, HelpText = "Log file path")]
		public string ReportPath { get; set; }
	}

	#endregion

	#region Class: InstallApplicationOptions

	/// <summary>
	/// Command-line options for installing an application package into Creatio.
	/// </summary>
	[Verb("install-application", Aliases = ["push-app", "install-app"],
		HelpText = "Install application on a web application")]
	public class InstallApplicationOptions : InstallOptions {
		/// <summary>
		/// Gets or sets whether compilation errors should be checked during installation.
		/// </summary>
		[Option("check-compilation-errors", Required = false, HelpText = "Check compilation errors", Hidden = false)]
		public bool? CheckCompilationErrors { get; set; } = null;
	}

	#endregion

	#region Class: InstallApplicationCommand

	/// <summary>
	/// Installs an application package into a Creatio environment.
	/// </summary>
	public class InstallApplicationCommand : Command<InstallApplicationOptions> {
		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationInstaller _applicationInstaller;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		/// <summary>
		/// Initializes a new instance of the <see cref="InstallApplicationCommand"/> class.
		/// </summary>
		/// <param name="environmentSettings">Resolved target environment settings.</param>
		/// <param name="applicationInstaller">Installer used to push the application package.</param>
		/// <param name="logger">Logger used for CLI and MCP-visible execution output.</param>
		public InstallApplicationCommand(
			EnvironmentSettings environmentSettings,
			IApplicationInstaller applicationInstaller,
			ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationInstaller.CheckArgumentNull(nameof(applicationInstaller));
			logger.CheckArgumentNull(nameof(logger));
			_environmentSettings = environmentSettings;
			_applicationInstaller = applicationInstaller;
			_logger = logger;
		}

		#endregion

		#region Methods: Public

		/// <inheritdoc />
		public override int Execute(InstallApplicationOptions options) {
			try {
				bool success = _applicationInstaller.Install(
					options.Name,
					_environmentSettings,
					options.ReportPath,
					options.CheckCompilationErrors);

				if (success) {
					_logger.WriteInfo("Done");
					return 0;
				}

				_logger.WriteError("Error");
				return 1;
			}
			catch (Exception exception) {
				_logger.WriteError(exception.ToString());
				return 1;
			}
		}

		#endregion
	}

	#endregion
}
