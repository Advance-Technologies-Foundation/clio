using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command
{ }

namespace Clio.Command
{
    #region Class: LockPackageOptions

    [Verb("lock-package", Aliases = new[]
    {
        "lp"
    }, HelpText = "Lock package")]
    public class LockPackageOptions : EnvironmentOptions
    {

        #region Properties: Public

        [Value(0, MetaName = "Name", Required = false, HelpText = "Package name")]
        public string Name { get; set; }

        #endregion

    }

    #endregion

    #region Class: LockPackageCommand

    public class LockPackageCommand : Command<LockPackageOptions>
    {

        #region Fields: Private

        private readonly IPackageLockManager _packageLockManager;
        private readonly ILogger _logger;

        #endregion

        #region Constructors: Public

        public LockPackageCommand(IPackageLockManager packageLockManager, ILogger logger)
        {
            _packageLockManager = packageLockManager;
            _logger = logger;
        }

        #endregion

        #region Methods: Public

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

        public IEnumerable<string> GetPackagesNames(LockPackageOptions options) =>
            string.IsNullOrWhiteSpace(options.Name)
                ? Enumerable.Empty<string>()
                : new[]
                {
                    options.Name
                };

        #endregion

    }

    #endregion
}
