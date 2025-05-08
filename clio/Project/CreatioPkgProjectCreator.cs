namespace Clio.Project;

public class CreatioPkgProjectCreator : ICreatioPkgProjectCreator
{
    public ICreatioPkgProject CreateFromFile(string path) => CreatioPkgProject.LoadFromFile(path);
}
