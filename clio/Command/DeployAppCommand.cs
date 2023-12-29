

using System;
using ATF.Repository.Providers;
using Clio.Command.PackageCommand;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{

	[Verb("deploy-application", Aliases = new[] { "deploy-app" }, HelpText = "Deploy app from current environment to destination environment")]
	internal class DeployAppOptions : BaseAppCommandOptions
	{

		public string SourceEnvironment {
			get {
				return Environment;
			}
			set {
				Environment = value;
			}
		}

		[Option('d', "DestinationEnvironment", Required = true, HelpText = "Destination environment")]
		public string DestinationEnvironment {
			get;
			set;
		}
		
	}

	internal class DeployAppCommand : BaseAppCommand<DeployAppOptions>
	{
		private ApplicationManager _applicationManager;

		public DeployAppCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings,
				ILogger logger, IDataProvider dataProvider, ApplicationManager applicationManager) : base(applicationClient, environmentSettings, logger, dataProvider, applicationManager) {
			_applicationManager = applicationManager;
		}

		protected override void ExecuteRemoteCommand(DeployAppOptions options) {
			_logger.WriteInfo("Start deploy application");
			_applicationManager.Deploy(options.Name, options.SourceEnvironment, options.DestinationEnvironment);

		}

	}
}
