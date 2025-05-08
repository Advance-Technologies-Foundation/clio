using System;

namespace Clio.Workspaces;

public interface IEnvironmentScriptCreator
{
    void Create(Version nugetCreatioSdkVersion);
}
