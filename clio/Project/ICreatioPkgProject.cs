namespace Clio.Project;

public interface ICreatioPkgProject
{

    #region Methods: Public

    CreatioPkgProject RefToBin();

    CreatioPkgProject RefToCoreSrc();

    CreatioPkgProject RefToCustomPath(string path);

    CreatioPkgProject RefToUnitBin();

    CreatioPkgProject RefToUnitCoreSrc();

    void SaveChanges();

    #endregion

}
