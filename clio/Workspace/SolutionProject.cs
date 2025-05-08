using System;

namespace Clio.Workspaces;

#region Class: SolutionProject

public class SolutionProject
{

    #region Constructors: Public

    public SolutionProject(string name, string path, Guid id, Guid uId)
    {
        Name = name;
        Path = path;
        Id = id;
        UId = uId;
    }

    public SolutionProject(string name, string path)
        : this(name, path, Guid.NewGuid(), Guid.NewGuid())
    { }

    #endregion

    #region Properties: Public

    public Guid Id { get; }

    public string Name { get; }

    public string Path { get; }

    public Guid UId { get; }

    #endregion

}

#endregion
