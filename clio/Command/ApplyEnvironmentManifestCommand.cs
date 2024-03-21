using Clio.Common;
using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Command
{
	[Verb("apply-manifest", Aliases = new string[] { "applym", "apply-environment-manifest" }, HelpText = "Apply manifest to environment")]

	internal class ApplyEnvironmentManifestOptions
	{
		public object ManifestFilePath { get; internal set; }
	}

	internal class ApplyEnvironmentManifestCommand : Command<ApplyEnvironmentManifestOptions>
	{
		private EnvironmentManager _environmentManager;

		public ApplyEnvironmentManifestCommand(EnvironmentManager environmentManager) {
			this._environmentManager = environmentManager;
		}

		public override int Execute(ApplyEnvironmentManifestOptions options) {
			return _environmentManager.ApplyManifest(options.ManifestFilePath);
		}
	}


}
