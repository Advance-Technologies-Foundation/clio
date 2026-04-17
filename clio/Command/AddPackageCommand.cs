using System;
using System.Collections.Generic;
using Clio.Command.ChainItems;
using Clio.Common;
using Clio.Package;
using CommandLine;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;

namespace Clio.Command;

#region Class: AddPackageOptions

[Verb("add-package", Aliases = ["ap"], HelpText = "Add package to workspace or local folder")]
public class AddPackageOptions : EnvironmentOptions{
	/// <summary>
	/// Package name to create.
	/// </summary>

	[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
	public string Name { get; set; }

	/// <summary>
	/// Creates an application descriptor for the package when requested.
	/// </summary>
	[Option('a', "asApp", Required = false,
		HelpText = "Create application in package", Default = false)]
	public bool AsApp { get; set; }
	
	/// <summary>
	/// Path to a Creatio build archive or extracted directory used by the follow-up download flow.
	/// </summary>
	[Option('b', "build", Required = false, 
		HelpText = "Path to Creatio zip file or extracted directory to get configuration from")]
	public string BuildZipPath { get; set; }

	/// <summary>
	/// Explicit workspace root path supplied by MCP callers.
	/// </summary>
	internal string WorkspacePath { get; set; }

	internal override bool RequiredEnvironment => false;
}

#endregion

#region Class: AddPackageCommand

public class AddPackageCommand(IPackageCreator packageCreator, ILogger logger, IFollowUpChain chain, 
	[FromKeyedServices(nameof(DconfChainItem))] IFollowupUpChainItem dconfChainItem) : Command<AddPackageOptions>{

	#region Methods: Public

	public override int Execute(AddPackageOptions options) {
		string originalCurrentDirectory = Environment.CurrentDirectory;
		try {
			ApplyWorkspacePath(options);
			packageCreator.Create(options.Name, options.AsApp);
			logger.WriteInfo("Done");
			return FollowUp(options);
		}
		finally {
			Environment.CurrentDirectory = originalCurrentDirectory;
		}
	}

	private int FollowUp(AddPackageOptions options) {

		IDictionary<string, object> ctx = chain.CreateContextFromOptions(options);
		ErrorOr<int> result = chain
							  .With(dconfChainItem)
							  .Execute(ctx);

		if (!result.IsError) {
			return result.Value;
		}
		foreach (ErrorOr.Error error in result.Errors) {
			logger.WriteError($"{error.Code} - {error.Description}");
		}
		return 1;
	}

	private static void ApplyWorkspacePath(AddPackageOptions options) {
		if (string.IsNullOrWhiteSpace(options.WorkspacePath)) {
			return;
		}
		Environment.CurrentDirectory = options.WorkspacePath;
	}

	#endregion
}

#endregion
