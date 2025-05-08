using System;

namespace Clio.Workspaces;

public class SolutionProject(string name, string path, Guid id, Guid uId)
{
    public SolutionProject(string name, string path)
        : this(name, path, Guid.NewGuid(), Guid.NewGuid())
    {
    }

    public string Name { get; } = name;

    public string Path { get; } = path;

    public Guid Id { get; } = id;

    public Guid UId { get; } = uId;
}
