namespace Clio.Command;

public class WindowsFeature
{

    #region Properties: Public

    public string Caption { get; set; }

    public bool Installed { get; set; }

    public string Name { get; set; }

    public string State
    {
        get { return Installed ? "OK" : "Not installed"; }
    }

    #endregion

    #region Methods: Public

    public override string ToString()
    {
        return $"{State} : {Name}";
    }

    #endregion

}
