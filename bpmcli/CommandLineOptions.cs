using System;
using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

namespace bpmcli
{
	internal class EnvironmentOptions
	{
		[Option('u', "uri", Required = false, HelpText = "Application uri")]
		public string Uri { get; set; }

		[Option('p', "Password", Required = false, HelpText = "User password")]
		public string Password { get; set; }

		[Option('l', "Login", Required = false, HelpText = "User login (administrator permission required)")]
		public string Login { get; set; }

		[Option('i', "IsNetCore", Required = false, HelpText = "Use NetCore application)")]
		public bool IsNetCore { get; set; }

		[Option('e', "Environment", Required = false, HelpText = "Environment name")]
		public string Environment { get; set; }

		[Option('m', "Maintainer", Required = false, HelpText = "Maintainer name")]
		public string Maintainer { get; set; }
	}

	[Verb("execute-assembly-code", Aliases = new string[] { "exec" }, HelpText = "Execute an assembly code which implements the IExecutor interface")]
	internal class ExecuteAssemblyOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Path to executed assembly")]
		public string Name { get; set; }

		[Option('t', "ExecutorType", Required = true, HelpText = "Assembly type name for proceed")]
		public string ExecutorType { get; set; }
	}

	[Verb("restart-web-app", Aliases = new string[] { "restart" }, HelpText = "Restart a web application")]
	internal class RestartOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get; set; }
	}

	[Verb("clear-redis-db", Aliases = new string[] { "flushdb" }, HelpText = "Clear redis database")]
	internal class ClearRedisOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get; set; }
	}

	[Verb("register", HelpText = "Register bpmcli in global environment", Hidden = true)]
	internal class RegisterOptions
	{
		[Option('t', "Target", Default = "u", HelpText = "Target environment location. Could be user location or" +
			" machine location. Use 'u' for set user location and 'm' to set machine location.")]
		public string Target { get; set; }

		[Option('p', "Path", HelpText = "Path where bpmcli is stored.")]
		public string Path { get; set; }

	}

	[Verb("fetch", HelpText = "Download assembly", Hidden = true)]
	internal class FetchOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Path to downloaded assembly")]
		public string Name { get; set; }

		[Option('o', "Operation", Required = false, HelpText = "Operation: load - from file system to app, download - from app to file system)")]
		public string Operation { get; set; }
	}

	[Verb("generate-pkg-zip", Aliases = new string[] { "compress" }, HelpText = "Prepare an archive of bpm'online package")]
	internal class GeneratePkgZipOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Name of the compressed package")]
		public string Name { get; set; }

		[Option('d', "DestinationPath", Required = false)]
		public string DestinationPath { get; set; }

		[Option('p', "Packages", Required = false)]
		public string Packages { get; set; }
	}

	[Verb("reg-web-app", Aliases = new string[] { "reg", "cfg" }, HelpText = "Configure a web application settings")]
	internal class RegAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Name of configured application")]
		public string Name { get; set; }

		[Option('a', "ActiveEnvironment", Required = false, HelpText = "Set a web application by default")]
		public string ActiveEnvironment { get; set; }

		[Option('s', "Safe", Required = false, HelpText = "Safe action in this enviroment")]
		public string Safe { get; set; }

		public bool? SafeValue
		{
			get
			{
				if (!String.IsNullOrEmpty(Safe)) {
					bool result;
					if (bool.TryParse(Safe, out result)) {
						return result;
					}
				}
				return null;
			}
		}
	}

	[Verb("show-web-app-list", Aliases = new string[] { "apps", "show-web-app", "app" }, HelpText = "Show the list of web applications and their settings")]
	internal class AppListOptions
	{
		[Value(0, MetaName = "App name", Required = false, HelpText = "Name of application")]
		public string Name { get; set; }
	}

	[Verb("unreg-web-app", Aliases = new string[] { "unreg" }, HelpText = "Unregister application's settings from the list")]
	internal class UnregAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Application name")]
		public string Name { get; set; }
	}

	[Verb("push-pkg", Aliases = new string[] { "install" }, HelpText = "Install package on a web application")]
	internal class PushPkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
		public string Name { get; set; }

		[Option('r', "ReportPath", Required = false, HelpText = "Log file path")]
		public string ReportPath { get; set; }
	}

	[Verb("delete-pkg-remote", Aliases = new string[] { "delete" }, HelpText = "Delete package from a web application")]
	internal class DeletePkgOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }
	}

	[Verb("ref-to", HelpText = "Change bpm package project core paths", Hidden = true)]
	internal class ReferenceOptions
	{
		[Option('r', "ReferencePattern", Required = false, HelpText = "Pattern for reference path",
			Default = null)]
		public string RefPattern { get; set; }

		[Option('p', "Path", Required = false, HelpText = "Path to the project file",
			Default = null)]
		public string Path { get; set; }

		[Value(0, MetaName = "ReferenceType", Required = false, HelpText = "Indicates what the project will refer to." +
			" Can be 'bin' or 'src'", Default = "src")]
		public string ReferenceType { get; set; }

	}

	[Verb("new-pkg", Aliases = new string[] { "init" }, HelpText = "Create a new bpm'online package in local file system")]
	internal class NewPkgOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Name of the created instance")]
		public string Name { get; set; }

		[Option('r', "References", Required = false, HelpText = "Set references to local bin assemblies for development")]
		public string Rebase { get; set; }

		[Usage(ApplicationAlias = "bpmcli")]
		public static IEnumerable<Example> Examples =>
			new List<Example> {
				new Example("Create new package with name 'ATF'",
					new NewPkgOptions { Name = "ATF" }
				),
				new Example("Create new package with name 'ATF' and with links on local installation bpm'online with file design mode",
					new NewPkgOptions { Name = "ATF", Rebase = "bin"}
				)
			};
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

		[Usage(ApplicationAlias = "bpmcli")]
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

	[Verb("update-cli", HelpText = "Update bpmcli to new available version")]
	internal class UpdateCliOptions
	{
	}

	[Verb("execute-sql-script", Aliases = new string[] { "sql" }, HelpText = "Execute script on web application")]
	internal class ExecuteSqlScriptOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Script", Required = false, HelpText = "Sql script")]
		public string Script { get; set; }

		[Option('f', "File", Required = false,
			HelpText = "Path to the sql script file.", Default = null)]
		public string File { get; set; }

		[Option('v', "View", Required = false, HelpText = "View type.", Default = "table")]
		public string ViewType { get; set; }

		[Option('d', "DestinationPath", Required = false, HelpText = "Path to results file.", Default = null)]
		public string DestPath { get; set; }
	}

	[Verb("install-gate", Aliases = new string[] { "update-gate" }, HelpText = "Install bpmcli api gateway to application")]
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
	internal class DeveloperModeOptions : EnvironmentOptions
	{
	}

	[Verb("SysSettings", Aliases = new string[] { "ss" }, HelpText = "Changes settings value")]
	internal class SysSettingsOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Operation", Required = true, HelpText = "Operation")]
		public string Operation { get; set; }

		[Option('c', "Code", Required = false, HelpText = "Code")]
		public string Code { get; set; }

		[Option('n', "Name", Required = false, HelpText = "Name")]
		public string Name { get; set; }

		[Option('v', "Value", Required = false, HelpText = "Value")]
		public string Value { get; set; }

		[Option('t', "Type", Required = false, HelpText = "Type")]
		public string Type { get; set; }

	}

	[Verb("set-feature-state", Aliases = new[] { "feature" }, HelpText = "Changes feature value")]
	internal class FeatureOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Code", Required = true, HelpText = "Feature code")]
		public string Code { get; set; }

		[Value(1, MetaName = "State", Required = false, HelpText = "Feature state")]
		public int State { get; set; }

	}


}
