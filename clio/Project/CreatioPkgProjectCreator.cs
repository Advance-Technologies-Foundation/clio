namespace Clio.Project;

public class CreatioPkgProjectCreator : ICreatioPkgProjectCreator
{

    #region Methods: Public

    public ICreatioPkgProject CreateFromFile(string path)
    {
        return CreatioPkgProject.LoadFromFile(path);
    }

    #endregion

}
