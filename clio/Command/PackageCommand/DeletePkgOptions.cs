using CommandLine;
using System;
using System.Collections.Generic;
using System.Text;

namespace Clio.Command.PackageCommand
{
	[Verb("delete-pkg-remote", Aliases = new string[] { "delete" }, HelpText = "Delete package from a web application")]
	public class DeletePkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }
	}
}
