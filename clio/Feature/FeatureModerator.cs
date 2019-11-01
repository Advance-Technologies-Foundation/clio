namespace clio.Feature
{
	using Creatio.Client;

	public class FeatureModerator
	{

		private const string FeatureServiceName = "FeatureStateService";
		private const string SetFeatureStateMethodName = "SetFeatureState";


		protected CreatioClient BpmonlineClient;

		public FeatureModerator(CreatioClient client) {
			BpmonlineClient = client;
		}

		public void SwitchFeatureOn(string code) {
			Feature feature = Feature.CreateFeatureWithCode(code).TurnOn();
			CallChangeFeatureStateV2(feature);
		}

		public void SwitchFeatureOff(string code) {
			Feature feature = Feature.CreateFeatureWithCode(code).TurnOff();
			CallChangeFeatureStateV2(feature);
		}

		private FeatureModerator CallChangeFeatureStateV2(Feature feature) {
			string requestData = "{" +
				$"\"code\":\"{feature.Code}\",\"state\":\"{(int)feature.State}\"" + "}";
			BpmonlineClient.CallConfigurationService(FeatureServiceName, SetFeatureStateMethodName, requestData);
			return this;
		}

	}
}
