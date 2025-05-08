using System;

namespace Clio.Workspaces;

#region Interface: IEnvironmentScriptCreator

public interface IEnvironmentScriptCreator
{

    #region Methods: Public

    void Create(Version nugetCreatioSdkVersion);

    #endregion

}

#endregion
