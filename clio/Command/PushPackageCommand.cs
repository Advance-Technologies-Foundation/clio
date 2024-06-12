namespace Clio.Command
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using Clio.Common;
	using Clio.Package;
	using Clio.WebApplication;
	using CommandLine;

	#region Class: PushPkgOptions

	[Verb("push-pkg", Aliases = new string[] { "install", "push" }, HelpText = "Install package on a web application")]
	public class PushPkgOptions : InstallOptions
	{
		#region Properties: Public

		[Option("InstallSqlScript", Required = false, HelpText = "Install sql script")]
		public bool? InstallSqlScript { get; set; }

		[Option("InstallPackageData", Required = false, HelpText = "Install package data")]
		public bool? InstallPackageData { get; set; }

		[Option("ContinueIfError", Required = false, HelpText = "Continue if error")]
		public bool? ContinueIfError { get; set; }

		[Option("SkipConstraints", Required = false, HelpText = "Skip constraints")]
		public bool? SkipConstraints { get; set; }

		[Option("SkipValidateActions", Required = false, HelpText = "Skip validate actions")]
		public bool? SkipValidateActions { get; set; }

		[Option("ExecuteValidateActions", Required = false, HelpText = "Execute validate actions")]
		public bool? ExecuteValidateActions { get; set; }

		[Option("IsForceUpdateAllColumns", Required = false, HelpText = "Is force update all columns")]
		public bool? IsForceUpdateAllColumns { get; set; }

		[Option("id", Required = false, HelpText = "Marketplace application id")]
		public IEnumerable<int> MarketplaceIds { get; set; }

		#endregion

	}

	#endregion

	#region Class: PushPackageCommand

	public class PushPackageCommand : Command<PushPkgOptions>
	{
		#region Fields: Private
		private readonly EnvironmentSettings _environmentSettings;
		private readonly IPackageInstaller _packageInstaller;
		private readonly IMarketplace _marketplace;
		private readonly PackageInstallOptions _packageInstallOptionsDefault = new PackageInstallOptions();
		#endregion

		#region Constructors: Public

		public PushPackageCommand() { } // for tests

		public PushPackageCommand(EnvironmentSettings environmentSettings, IPackageInstaller packageInstaller, IMarketplace marketplace)
		{
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			packageInstaller.CheckArgumentNull(nameof(packageInstaller));
			_environmentSettings = environmentSettings;
			_packageInstaller = packageInstaller;
			_marketplace = marketplace;
		}

		#endregion

		#region Methods: Private
		private PackageInstallOptions ExtractPackageInstallOptions(PushPkgOptions options)
		{
			var packageInstallOptions = new PackageInstallOptions {
				InstallSqlScript = options.InstallSqlScript ?? true,
				InstallPackageData = options.InstallPackageData ?? true,
				ContinueIfError = options.ContinueIfError ?? true,
				SkipConstraints = options.SkipConstraints ?? false,
				SkipValidateActions = options.SkipValidateActions ?? false,
				ExecuteValidateActions = options.ExecuteValidateActions ?? false,
				IsForceUpdateAllColumns = options.IsForceUpdateAllColumns ?? false
			};
			return packageInstallOptions == _packageInstallOptionsDefault
				? null
				: packageInstallOptions;
		}
		#endregion

		#region Methods: Public

		public override int Execute(PushPkgOptions options)
		{
			PackageInstallOptions packageInstallOptions = ExtractPackageInstallOptions(options);
			bool success = false;
			try
			{
				if (options.MarketplaceIds != null && options.MarketplaceIds.Any())
				{
					foreach (int MarketplaceId in options.MarketplaceIds)
					{
						string fullPath = string.Empty;
						Task.Run(async () =>
						{
							fullPath = await _marketplace.GetFileByIdAsync(MarketplaceId);
						}).Wait();
						
						bool _loopSuccess = _packageInstaller.Install(fullPath, _environmentSettings,
							packageInstallOptions, options.ReportPath);
						Console.WriteLine(_loopSuccess ? $"Done installing app by id: {MarketplaceId}" : $"Error installing app by id: {MarketplaceId}");
					}
					success = true;
				}
				else
				{
					success = _packageInstaller.Install(options.Name, _environmentSettings,
						packageInstallOptions, options.ReportPath);
				}
				Console.WriteLine(success ? "Done" : "Error");
				return success ? 0 : 1;
			}
			catch (Exception e)
			{
				Console.WriteLine(e.StackTrace);
				return 1;
			}
		}
		#endregion
	}

	#endregion

	#region Class: InstallGatePkgCommand

	public class InstallGatePkgCommand : PushPackageCommand
	{
		private IApplication _application;
		private ILogger _logger;

		#region Constructors: Public
		public InstallGatePkgCommand(EnvironmentSettings environmentSettings, IPackageInstaller packageInstaller,
			IMarketplace marketplace, IApplication applicatom, ILogger logger)
			: base(environmentSettings, packageInstaller, marketplace) {
			_application = applicatom;
			_logger = logger;
		}
		
		#endregion

		#region Methods: Public

		public override int Execute(PushPkgOptions options) {
			int result = base.Execute(options);
			if (result == 0) {
				_application.Restart();
			}
			return result;
		}

		#endregion
	}

	#endregion

}