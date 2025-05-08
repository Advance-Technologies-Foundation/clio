using System;
using System.Collections.Generic;

namespace Clio.Workspaces;

public class WorkspaceSettings
{
    public IList<string> Packages { get; set; } = new List<string>();

    public Version ApplicationVersion { get; set; }
}
