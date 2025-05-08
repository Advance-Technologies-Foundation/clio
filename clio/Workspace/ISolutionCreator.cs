using System.Collections.Generic;

namespace Clio.Workspaces;

public interface ISolutionCreator
{
    void Create(string solutionPath, IEnumerable<SolutionProject> solutionProjects);
}
