public class WindowsFeature
{
	public string Name { get; set; }

	public string State { get {
			return Installed ? "OK" : "Not installed";
		}
	}

	public bool Installed { get; set; }

	public override string ToString() {
		return $"{State} : {Name}";
	}
}