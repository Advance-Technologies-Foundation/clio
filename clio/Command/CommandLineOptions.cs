using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

namespace Clio
{
	public class EnvironmentOptions
	{
		[Option('u', "uri", Required = false, HelpText = "Application uri")]
		public string Uri { get; set; }

		[Option('p', "Password", Required = false, HelpText = "User password")]
		public string Password { get; set; }

		[Option('l', "Login", Required = false, HelpText = "User login (administrator permission required)")]
		public string Login { get; set; }

		[Option('i', "IsNetCore", Required = false, HelpText = "Use NetCore application)", Default = null)]
		public bool? IsNetCore { get; set; }

		[Option('e', "Environment", Required = false, HelpText = "Environment name")]
		public string Environment { get; set; }

		[Option('m', "Maintainer", Required = false, HelpText = "Maintainer name")]
		public string Maintainer { get; set; }
	}


	[Verb("register", HelpText = "Register clio in global environment", Hidden = true)]
	internal class RegisterOptions
	{
		[Option('t', "Target", Default = "u", HelpText = "Target environment location. Could be user location or" +
			" machine location. Use 'u' for set user location and 'm' to set machine location.")]
		public string Target { get; set; }

		[Option('p', "Path", HelpText = "Path where clio is stored.")]
		public string Path { get; set; }

	}

	[Verb("generate-pkg-zip", Aliases = new string[] { "compress" }, HelpText = "Prepare an archive of creatio package")]
	internal class GeneratePkgZipOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Name of the compressed package")]
		public string Name { get; set; }

		[Option('d', "DestinationPath", Required = false, HelpText = "Full destination path for gz file")]
		public string DestinationPath { get; set; }

		[Option('p', "Packages", Required = false)]
		public string Packages { get; set; }

		[Option('s', "SkipPdb", Required = false, Default = false)]
		public bool SkipPdb { get; set; }
	}

	[Verb("push-pkg", Aliases = new string[] { "install" }, HelpText = "Install package on a web application")]
	internal class PushPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name { get; set; }

		[Option('r', "ReportPath", Required = false, HelpText = "Log file path")]
		public string ReportPath { get; set; }
	}

	[Verb("convert", HelpText = "Convert package to project", Hidden = true)]
	internal class ConvertOptions
	{
		[Option('p', "Path", Required = false,
			HelpText = "Path to package directory", Default = null)]
		public string Path { get; set; }

		[Value(0, MetaName = "<package names>", Required = false,
			HelpText = "Name of the convert instance (or comma separated names)")]
		public string Name { get; set; }

		[Usage(ApplicationAlias = "clio")]
		public static IEnumerable<Example> Examples =>
			new List<Example> {
				new Example("Convert existing packages",
					new ConvertOptions { Path = "C:\\Pkg\\" , Name = "MyApp,MyIntegration"}
				),
				new Example("Convert all packages in folder",
					new ConvertOptions { Path = "C:\\Pkg\\"}
				)
			};
	}

	[Verb("pull-pkg", Aliases = new string[] { "download" }, HelpText = "Download package from a web application")]
	internal class PullPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }

		[Option('d', "DestinationPath", Required = false,
			HelpText = "Path to the directory where Zip created.", Default = null)]
		public string DestPath { get; set; }
	}

	[Verb("install-gate", Aliases = new string[] { "update-gate", "gate" }, HelpText = "Install clio api gateway to application")]
	internal class InstallGateOptions : EnvironmentOptions
	{
	}

	[Verb("add-item", Aliases = new string[] { "create" }, HelpText = "Create item in project")]
	internal class ItemOptions: EnvironmentOptions
	{
		[Value(0, MetaName = "Item type", Required = true, HelpText = "Item type")]
		public string ItemType { get; set; }

		[Value(1, MetaName = "Item name", Required = true, HelpText = "Item name")]
		public string ItemName { get; set; }


		[Option('d', "DestinationPath", Required = false, HelpText = "Path to source directory.", Default = null)]
		public string DestinationPath { get; set; }

		[Option('n', "Namespace", Required = false, HelpText = "Name space for service classes.", Default = null)]
		public string Namespace { get; set; }
	}

	[Verb("set-dev-mode", Aliases = new string[] { "dev", "unlock" }, HelpText = "Activate developer mode for selected environment")]
	internal class DeveloperModeOptions: EnvironmentOptions
	{
	}

}
