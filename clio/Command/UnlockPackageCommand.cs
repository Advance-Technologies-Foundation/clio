using System;
using System.Collections.Generic;
using System.Linq;

using CommandLine;
using Package;

namespace Clio.Command;

[Verb("unlock-package", Aliases = new string[] { "up" }, HelpText = "Unlock package")]
public class UnlockPackageOptions : EnvironmentOptions
{
    [Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
    public string Name { get; set; }
}

public class UnlockPackageCommand(IPackageLockManager packageLockManager): Command<UnlockPackageOptions>
{
    private readonly IPackageLockManager _packageLockManager = packageLockManager;

    public IEnumerable<string> GetPackagesNames(UnlockPackageOptions options) =>
        string.IsNullOrWhiteSpace(options.Name)
            ? Enumerable.Empty<string>()
            : new[] { options.Name };

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
}
