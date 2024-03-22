using Clio.Common;
using Clio.Package;
using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Command
{
	[Verb("apply-manifest", Aliases = new string[] { "applym", "apply-environment-manifest" }, HelpText = "Apply manifest to environment")]

	internal class ApplyEnvironmentManifestOptions : EnvironmentOptions
	{
		public string ManifestFilePath { get; set; }
	}

	internal class ApplyEnvironmentManifestCommand : Command<ApplyEnvironmentManifestOptions>
	{
		private EnvironmentManager _environmentManager;
		private IApplicationInstaller _applicationInstaller;

		public ApplyEnvironmentManifestCommand(EnvironmentManager environmentManager, IApplicationInstaller applicationInstaller) {
			this._environmentManager = environmentManager;
			_applicationInstaller = applicationInstaller;
		}

		public override int Execute(ApplyEnvironmentManifestOptions options) {
			var apps = _environmentManager.FindApllicationsInAppHub(options.ManifestFilePath);
			var environment = _environmentManager.GetEnvironmentFromManifest(options.ManifestFilePath);
			foreach (var app in apps) {
				_applicationInstaller.Install(app.ZipFileName, environment);
			}
			return _environmentManager.ApplyManifest(options.ManifestFilePath);
		}
	}


}
