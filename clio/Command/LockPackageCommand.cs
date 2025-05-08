using System;
using System.Collections.Generic;
using System.Linq;

using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command
{
}

namespace Clio.Command
{
    [Verb("lock-package", Aliases = new string[] { "lp" }, HelpText = "Lock package")]
    public class LockPackageOptions : EnvironmentOptions
    {
        [Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
        public string Name { get; set; }
    }

    public class LockPackageCommand(IPackageLockManager packageLockManager, ILogger logger): Command<LockPackageOptions>
    {
        private readonly IPackageLockManager _packageLockManager = packageLockManager;
        private readonly ILogger _logger = logger;

        public IEnumerable<string> GetPackagesNames(LockPackageOptions options) =>
            string.IsNullOrWhiteSpace(options.Name)
                ? Enumerable.Empty<string>()
                : new[] { options.Name };

        public override int Execute(LockPackageOptions options)
        {
            try
            {
                _packageLockManager.Lock(GetPackagesNames(options));
                _logger.WriteLine();
                _logger.WriteInfo("Done");
                return 0;
            }
            catch (Exception e)
            {
                _logger.WriteError(e.Message);
                return 1;
            }
        }
    }
}
