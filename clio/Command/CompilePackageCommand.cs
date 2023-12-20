using System;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("compile-package", HelpText = "Build package command")]
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

	#endregion

	#region Constructors: Public

	public CompilePackageCommand(IPackageBuilder packageBuilder) {
		_packageBuilder = packageBuilder;
	}

	#endregion

	#region Methods: Private
		
	#endregion

	#region Methods: Public

	public override int Execute(CompilePackageOptions options) {
		try {
			_packageBuilder.Rebuild(options.PackageNames);
			Console.WriteLine("Done");
			return 0;
		} catch (Exception e) {
			Console.WriteLine(e.Message);
			return 1;
		}
	}

	#endregion

}