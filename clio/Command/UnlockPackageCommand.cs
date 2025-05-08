using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

#region Class: UnlockPackageOptions

[Verb("unlock-package", Aliases = new[]
{
    "up"
}, HelpText = "Unlock package")]
public class UnlockPackageOptions : EnvironmentOptions
{

    #region Properties: Public

    [Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
    public string Name { get; set; }

    #endregion

}

#endregion

#region Class: UnlockPackageCommand

public class UnlockPackageCommand : Command<UnlockPackageOptions>
{

    #region Fields: Private

    private readonly IPackageLockManager _packageLockManager;

    #endregion

    #region Constructors: Public

    public UnlockPackageCommand(IPackageLockManager packageLockManager)
    {
        _packageLockManager = packageLockManager;
    }

    #endregion

    #region Methods: Public

    public override int Execute(UnlockPackageOptions options)
    {
        try
        {
            _packageLockManager.Unlock(GetPackagesNames(options));
            Console.WriteLine();
            Console.WriteLine("Done");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
    }

    public IEnumerable<string> GetPackagesNames(UnlockPackageOptions options) =>
        string.IsNullOrWhiteSpace(options.Name)
            ? Enumerable.Empty<string>()
            : new[]
            {
                options.Name
            };

    #endregion

}

#endregion
