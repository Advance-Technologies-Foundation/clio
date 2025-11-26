using System.Collections.Generic;
using Clio.Command.ChainItems;
using Clio.Common;
using Clio.Package;
using CommandLine;
using ErrorOr;

namespace Clio.Command;

#region Class: AddPackageOptions

[Verb("add-package", Aliases = ["ap"], HelpText = "Add package to workspace or local folder")]
public class AddPackageOptions : EnvironmentOptions{

	[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
	public string Name { get; set; }

	[Option('a', "asApp", Required = false,
		HelpText = "Create application in package", Default = false)]
	public bool AsApp { get; set; }
	
	[Option('b', "build", Required = false, 
		HelpText = "Path to Creatio zip file or extracted directory to get configuration from")]
	public string BuildZipPath { get; set; }
}

#endregion

#region Class: AddPackageCommand

public class AddPackageCommand(IPackageCreator packageCreator, ILogger logger, IFollowUpChain chain, DconfChainItem dconfChainItem) : Command<AddPackageOptions>{

	#region Methods: Public

	public override int Execute(AddPackageOptions options) {
		packageCreator.Create(options.Name, options.AsApp);
		logger.WriteInfo("Done");
		return FollowUp(options);
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

	#endregion
}

#endregion
