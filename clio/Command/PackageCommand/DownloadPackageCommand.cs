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

		[Option('d', "destination-path", Required = false,
			HelpText = "Path to the directory where Zip created.", Default = null)]
		public string DestPath {
			get; set;
		}

		[Option("DestinationPath", Required = false, Hidden = true, HelpText = "Alias for --destination-path")]
		public string DestPathAlias {
			get => DestPath;
			set { if (!string.IsNullOrEmpty(value)) DestPath = value; }
		}

		[Option('r', "unzip", Required = false,
			HelpText = "Unzip archive file.", Default = null)]
		public bool Unzip {
			get; set;
		}

		[Option("UnZip", Required = false, Hidden = true, HelpText = "Alias for --unzip")]
		public bool UnzipAlias {
			get => Unzip;
			set { if (value) Unzip = value; }
		}

		[Option('a', "async", Required = false,
			HelpText = "Async download file.", Default = false)]
		public bool Async {
			get; set;
		}

		[Option("Async", Required = false, Hidden = true, HelpText = "Alias for --async")]
		public bool AsyncAlias {
			get => Async;
			set { if (value) Async = value; }
		}

	}

	internal class PullPkgCommand : Command<PullPkgOptions> {

		public override int Execute(PullPkgOptions options) {
			return Program.DownloadZipPackages(options);
		}
	}
}
