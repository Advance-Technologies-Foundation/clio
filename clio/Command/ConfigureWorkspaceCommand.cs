using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using Clio.Utilities;
using Clio.Workspaces;
using CommandLine;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentValidation;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Clio.Command
{
	[Verb("cfg-worspace", Aliases = new string[] { "cfgw" }, HelpText = "Configure workspace settings")]
	public class ConfigureWorkspaceOptions : EnvironmentOptions
	{

		[Option("Packages", Required = false, HelpText = "Packages")]
		public string Packages
		{
			get; set;
		}

		public IEnumerable<string> PackageNames
		{
			get {
				if (string.IsNullOrEmpty(Packages)) {
					return Enumerable.Empty<string>();
				}
				return StringParser.ParseArray(Packages);
			}
		}
	}

	
	
	public class ConfigureWorkspaceCommand : Command<ConfigureWorkspaceOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public ConfigureWorkspaceCommand(IWorkspace workspace, ILogger logger) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
			_logger = logger;
		}

		#endregion

		public override int Execute(ConfigureWorkspaceOptions options) {
			try {
				foreach (var packageName in options.PackageNames) {
					_workspace.AddPackageIfNeeded(packageName);
				}
				_workspace.SaveWorkspaceEnvironment(options.Environment);
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}
	}


	

}

