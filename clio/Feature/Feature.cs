namespace clio.Feature
{

	public enum FeatureState
	{
		Disabled = 0,
		Enabled = 1,
	}

	public class Feature
	{
		private Feature(string name, string code) {
			Name = name;
			Code = code;
			State = FeatureState.Enabled;
			Descriptor = $"Feature with code {Code}";
		}

		public string Name { get; set; }
		public string Code { get; set; }
		public FeatureState State { get; set; }
		public string Descriptor { get; set; }

		public static Feature CreateFeatureWithCode(string code) {
			return new Feature(code, code);
		}

		public Feature TurnOn() {
			State = FeatureState.Enabled;
			return this;
		}

		public Feature TurnOff() {
			State = FeatureState.Disabled;
			return this;
		}
	}
}
