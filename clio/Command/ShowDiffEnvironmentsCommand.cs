using ATF.Repository.Providers;
using ATF.Repository;
using Clio.Common;
using CommandLine;
using CreatioModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Clio.Command
{

	[Verb("show-diff", Aliases = new[] { "diff", "compare" }, HelpText = "Show difference in settings for two Creatio intances")]
	internal class ShowDiffEnvironmentsOptions : EnvironmentNameOptions
	{
		public string Source { get; internal set; }
		public string Origin { get; internal set; }
		public string FileName { get; internal set; }
		public bool Overwrite { get; internal set; }
	}


	internal class ShowDiffEnvironmentsCommand : BaseDataContextCommand<ShowDiffEnvironmentsOptions>
	{
		private IEnvironmentManager _environmentManager;

		public ShowDiffEnvironmentsCommand(IEnvironmentManager environmentManager, IDataProvider provider, ILogger logger) : base(provider, logger) {
			_environmentManager = environmentManager;
		}

		public ShowDiffEnvironmentsCommand(IEnvironmentManager environmentManager, IDataProvider provider, ILogger logger, IApplicationClient applicationClient, EnvironmentSettings environmentSettings) : base(provider, logger, applicationClient, environmentSettings) {
			_environmentManager = environmentManager;
		}

		public override int Execute(ShowDiffEnvironmentsOptions options) {
			_logger.WriteInfo($"Operating on environment: {options.Uri}");
			_logger.WriteInfo("Loading information about webservices");
			var manifestFileName = options.FileName ?? $"diff-{options.Source}-{options.Origin}.yaml";
			var diffManifest = new EnvironmentManifest();
			diffManifest.Packages = new List<CreatioManifestPackage>();
			_environmentManager.SaveManifestToFile(manifestFileName, diffManifest, options.Overwrite);
			_logger.WriteInfo("Done");
			return 0;
		}
	}

}
