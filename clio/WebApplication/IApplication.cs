namespace Clio.WebApplication;

public interface IApplication
{

    #region Methods: Public

    void LoadLicense(string filePath);

    void Restart();

    #endregion

}
