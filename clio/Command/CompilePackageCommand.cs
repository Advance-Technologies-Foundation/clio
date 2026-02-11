using System;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("compile-package", Aliases = ["comp-pkg"], HelpText = "Build package command")]
public class CompilePackageOptions : EnvironmentNameOptions
{

	#region Properties: Public


	[Value(0, MetaName = "PackageName", Required = true, HelpText = "Specified package name")]
	public string PackageName
	{
		get; set;
	}

	public string[] PackageNames => PackageName.Split(',');

	#endregion

}

public class CompilePackageCommand : Command<CompilePackageOptions>
{

	#region Fields: Private

	IPackageBuilder _packageBuilder;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public CompilePackageCommand(IPackageBuilder packageBuilder, ILogger logger) {
		_packageBuilder = packageBuilder;
		_logger = logger;
	}

	#endregion

	#region Methods: Private
		
	#endregion

	#region Methods: Public

	public override int Execute(CompilePackageOptions options) {
		try {
			_packageBuilder.Rebuild(options.PackageNames);
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion

}
