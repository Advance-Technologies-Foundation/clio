namespace Clio.Command.PackageCommand
{
	using CommandLine;

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

		[Option('r', "UnZip", Required = false,
			HelpText = "Unzip archive file.", Default = null)]
		public bool Unzip {
			get; set;
		}

		[Option('a', "Async", Required = false,
			HelpText = "Async download file.", Default = false)]
		public bool Async
		{
			get; set;
		}

	}
}
