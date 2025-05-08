using System;
using System.Collections.Generic;

namespace Clio.Workspaces;

#region Class: WorkspaceSettings

public class WorkspaceSettings
{

    #region Properties: Public

    public Version ApplicationVersion { get; set; }

    public IList<string> Packages { get; set; } = new List<string>();

    #endregion

}

#endregion
