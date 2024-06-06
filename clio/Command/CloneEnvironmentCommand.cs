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
using Autofac;
using Clio.UserEnvironment;
using DocumentFormat.OpenXml.Drawing;
using k8s.Models;
using YamlDotNet.Serialization;

namespace Clio.Command
{
	[Verb("show-diff", Aliases = new[] { "diff", "compare" },
		HelpText = "Show difference in settings for two Creatio intances")]
	internal class CloneEnvironmentOptions : ShowDiffEnvironmentsOptions
	{

		[Option("source", Required = true, HelpText = "Source environment name")]
		public string Source { get; internal set; }

		[Option("target", Required = true, HelpText = "Target environment name")]
		public string Target { get; internal set; }

		[Option("file", Required = false, HelpText = "Diff file name")]
		public string FileName { get; internal set; }

		[Option("overwrite", Required = false, HelpText = "Overwrite existing file", Default = true)]
		public bool Overwrite { get; internal set; }

	}

	internal class CloneEnvironmentCommand : BaseDataContextCommand<CloneEnvironmentOptions>
	{
		private readonly ShowDiffEnvironmentsCommand showDiffEnvironmentsCommand;
		private readonly ApplyEnvironmentManifestCommand applyEnvironmentManifestCommand;

		public CloneEnvironmentCommand(ShowDiffEnvironmentsCommand showDiffEnvironmentsCommand,
			ApplyEnvironmentManifestCommand applyEnvironmentManifestCommand, ILogger logger,
			IDataProvider provider)
			: base(provider, logger) {
			this.showDiffEnvironmentsCommand = showDiffEnvironmentsCommand;
			this.applyEnvironmentManifestCommand = applyEnvironmentManifestCommand;
		}


		public override int Execute(CloneEnvironmentOptions options) {
			
			showDiffEnvironmentsCommand.Execute(options);
			var applyEnvironmentManifestOptions = new ApplyEnvironmentManifestOptions() {
				Environment = options.Target,
				ManifestFilePath = options.FileName
			};
			applyEnvironmentManifestCommand.Execute(applyEnvironmentManifestOptions);
			_logger.WriteInfo("Done");
			return 0;
		}

	}
}