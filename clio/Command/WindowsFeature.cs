namespace Clio.Command;

public class WindowsFeature
{
    public string Name { get; set; }

    public string Caption { get; set; }

    public string State => Installed ? "OK" : "Not installed";

    public bool Installed { get; set; }

    public override string ToString() => $"{State} : {Name}";
}
