using CommandLine;
using CommandLine.Text;
using System.Collections.Generic;

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

		[Option('c', "dev", Required = false, HelpText = "Developer mode state for environment")]
		public string DevMode { get; set; }

		public bool? DeveloperModeEnabled {
			get {
				if (!string.IsNullOrEmpty(DevMode))
				{
					if (bool.TryParse(DevMode, out bool result))
					{
						return result;
					}
				}
				return null;
			}
			set {
				DevMode = value.ToString();
			}
		}

		[Option('s', "Safe", Required = false, HelpText = "Safe action in this environment")]
		public string Safe { get; set; }

		[Option("clientId", Required = false, HelpText = "OAuth client id")]
		public string ClientId { get; set; }

		[Option("clientSecret", Required = false, HelpText = "OAuth client secret")]
		public string ClientSecret { get; set; }

		[Option("authAppUri", Required = false, HelpText = "OAuth app URI")]
		public string AuthAppUri { get; set; }


		[Option("silent", Required = false, HelpText = "Use default behavior without user interaction")]
		public bool IsSilent { get; set; }


		public bool? SafeValue {
			get {
				if (!string.IsNullOrEmpty(Safe))
				{
					if (bool.TryParse(Safe, out bool result))
					{
						return result;
					}
				}
				return null;
			}
		}

		[Option("restartEnvironment", Required = false, HelpText = "Restart environment after execute command")]
		public bool RestartEnvironment { get; set; }

		public static bool IsNullOrEmpty(EnvironmentOptions options) {
			if (options == null) {
				return true;
			}
			if (string.IsNullOrEmpty(options.Uri) &&
					string.IsNullOrEmpty(options.Login) &&
					string.IsNullOrEmpty(options.Password) &&
					string.IsNullOrEmpty(options.ClientId) &&
					string.IsNullOrEmpty(options.ClientSecret) &&
					string.IsNullOrEmpty(options.AuthAppUri) &&
					string.IsNullOrEmpty(options.Maintainer)) {
				return true;
			}
			return false;
		}

	}

	public class EnvironmentNameOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "EnvironmentName", Required = false, HelpText = "Application name")]
		public string EnvironmentName { get => Environment; set { Environment = value; } }
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

		[Option('c', "ConvertSourceCode", Required = false, HelpText = "Convert source code schema to files", Default = false)]
		public bool ConvertSourceCode { get; set; }


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

	[Verb("install-gate", Aliases = new string[] { "update-gate", "gate", "installgate" }, HelpText = "Install clio api gateway to application")]
	internal class InstallGateOptions : EnvironmentNameOptions
	{
	}

	[Verb("add-item", Aliases = new string[] { "create" }, HelpText = "Create item in project")]
	internal class ItemOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Item type", Required = true, HelpText = "Item type")]
		public string ItemType { get; set; }

		[Value(1, MetaName = "Item name", Required = true, HelpText = "Item name")]
		public string ItemName { get; set; }


		[Option('d', "DestinationPath", Required = false, HelpText = "Path to source directory.", Default = null)]
		public string DestinationPath { get; set; }

		[Option('n', "Namespace", Required = false, HelpText = "Name space for service classes.", Default = null)]
		public string Namespace { get; set; }

		[Option('f', "Fields", Required = false, HelpText = "Required fields for model class", Default = null)]
		public string Fields { get; set; }

		[Option('a', "All", Required = false, HelpText = "Create all models", Default = true)]
		public bool CreateAll { get; set; }

		[Option('x', "Culture", Required = false, HelpText = "Description custure", Default = "en-US")]
		public string Culture { get; set; }
	}

	[Verb("set-dev-mode", Aliases = new string[] { "dev", "unlock" }, HelpText = "Activate developer mode for selected environment")]
	internal class DeveloperModeOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get => Environment; set { Environment = value; } }
	}

}
