using System.Collections.Generic;

namespace Clio.Workspaces;

#region Interface: ISolutionCreator

public interface ISolutionCreator
{

    #region Methods: Public

    void Create(string solutionPath, IEnumerable<SolutionProject> solutionProjects);

    #endregion

}

#endregion
