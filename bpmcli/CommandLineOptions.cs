using System;
using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

namespace bpmcli
{
	internal class BaseOptions
	{
		[Option('u', "uri", Required = false, HelpText = "bpm`online site uri")]
		public string Uri { get; set; }

		[Option('p', "Password", Required = false, HelpText = "User password")]
		public string Password { get; set; }

		[Option('l', "Login", Required = false, HelpText = "User login (administrator permisssion required)")]
		public string Login { get; set; }

		[Option('e', "Environment", Required = false, HelpText = "Environment name")]
		public string Environment { get; set; }

		[Option('m', "Maintainer", Required = false, HelpText = "Maintainer name")]
		public string Maintainer { get; set; }
	}

	[Verb("exec", HelpText = "Execute assembly")]
	internal class ExecuteOptions : BaseOptions
	{
		[Option('f', "FilePath", Required = true, HelpText = "Assembly file path")]
		public string FilePath { get; set; }

		[Option('t', "ExecutorType", Required = true, HelpText = "Assembly type name for proceed")]
		public string ExecutorType { get; set; }
	}

	[Verb("restart", HelpText = "Restart application")]
	internal class RestartOptions : BaseOptions
	{
	}

	[Verb("fetch", HelpText = "Download assembly")]
	internal class FetchOptions : BaseOptions
	{
		[Option('n', "Package names", Required = true, HelpText = "Package names")]
		public string PackageNames { get; set; }

		[Option('o', "Operation", Required = false, HelpText = "Operation: load - from file system to app, download - from app to file system)")]
		public string Operation { get; set; }
	}

	[Verb("compress", HelpText = "Compression project")]
	internal class CompressionOptions
	{
		[Option('s', "SourcePath", Required = true)]
		public string SourcePath { get; set; }
		[Option('d', "DestinationPath", Required = true)]
		public string DestinationPath { get; set; }
		[Option('p', "Packages", Required = false)]
		public string Packages  { get; set; }
	}

	[Verb("cfg", HelpText = "Configure environment settings")]
	internal class ConfigureOptions : BaseOptions
	{
		[Option('a', "ActiveEnvironment", Required = false, HelpText = "Set environment by default")]
		public string ActiveEnvironment { get; set; }
		[Option('m', "Mode", Required =false, HelpText = "Configuration mode (edit by default|view for display settings)")]
		public string Mode { get; set; }
	}

	[Verb("remove", HelpText = "Remove environment settings")]
	internal class RemoveOptions : BaseOptions
	{
		[Option('e', "ActiveEnvironment", Required = true, HelpText = "Environment name")]
		public new string Environment { get; set; }
	}

	[Verb("install", HelpText = "Install package")]
	internal class InstallOptions : BaseOptions
	{
		[Option('f', "FilePath", Required = true, HelpText = "Package file path")]
		public string FilePath { get; set; }
		[Option('r', "ReportPath", Required = false, HelpText = "Log file path")]
		public string ReportPath { get; set; }
	}

	[Verb("delete", HelpText = "Delete package")]
	internal class DeleteOptions : BaseOptions
	{
		[Option('c', "Code", Required = true, HelpText = "Package code")]
		public string Code { get; set; }
	}

	[Verb("rebase", HelpText = "Change bpm package project core pathes")]
	internal class RebaseOptions
	{
		[Option('f', "FilePath", Required = false, HelpText = "Path to the project file",
			Default = null)]
		public string FilePath { get; set; }

		[Option('t', "ProjectType", Required = false, HelpText = "Type of the bpm project file. Can be 'pkg' or 'sln'",
			Default = "pkg")]
		public string ProjectType { get; set; }

	}

	[Verb("new", HelpText = "Create new instance in bpmonline")]
	internal class NewOptions
	{
		[Value(0, MetaName = "<TEMPLATE NAME>", Required = true, HelpText = "Template of the created instance. Can be (pkg)")]
		public string Template { get; set; }
		[Option('n', Required = false, Default = "DefaultName", HelpText = "Name of the created instance")]
		public string Name { get; set; }
		[Option('d', "DestinationPath", Required = false,
			HelpText = "Path to the directory where new instance will be created", Default = null)]
		public string DestPath { get; set; }
		[Option('r', Required = false, Default = "true", HelpText = "Execute rebase command after create")]
		public string Rebase { get; set; }
		[Usage(ApplicationAlias = "bpmcli")]
		public static IEnumerable<Example> Examples => 
			new List<Example> {
				new Example("Create new package with name 'ContactPkg'",
					new NewOptions { Name = "ContactPkg" , Template = "pkg"}
					)
		};
	}
}
