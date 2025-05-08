using System;

using CreatioModel;

namespace Clio.Package;

public interface IApplicationInstaller
{
    bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
        string reportPath = null);

    bool UnInstall(SysInstalledApp appInfo, EnvironmentSettings environmentSettings = null,
        string reportPath = null);
}
