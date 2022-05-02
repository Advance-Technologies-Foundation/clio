using System;
using System.Collections.Generic;
using System.Text;
using CommandLine;

namespace Clio.Command.PackageCommand
{


	[Verb("pull-pkg", Aliases = new string[] { "download" }, HelpText = "Download package from a web application")]
	internal class PullPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name {
			get; set;
		}

		[Option('d', "DestinationPath", Required = false,
			HelpText = "Path to the directory where Zip created.", Default = null)]
		public string DestPath {
			get; set;
		}

		public bool Unzip {
			get; set;
		}

	}
}
