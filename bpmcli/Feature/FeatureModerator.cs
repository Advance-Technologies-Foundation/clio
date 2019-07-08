namespace bpmcli.Feature
{
	using System;
	using Bpmonline.Client;

	public class FeatureModerator
	{
		private const string FeatureServiceName = "FeatureService";
		private const string CreateMethodName = "CreateFeature";
		private const string SetFeatureStateMethodName = "SetFeatureState";
		

		protected BpmonlineClient BpmonlineClient;

		public FeatureModerator(BpmonlineClient client) {
			BpmonlineClient = client;
		}

		public void SwitchFeatureOn(string code) {
			Feature feature = Feature.CreateFeatureWithCode(code).TurnOn();
			CallCreateFeature(feature).CallChangeFeatureState(feature);
		}

		public void SwitchFeatureOff(string code) {
			Feature feature = Feature.CreateFeatureWithCode(code).TurnOff();
			CallCreateFeature(feature).CallChangeFeatureState(feature);
		}

		private FeatureModerator CallCreateFeature(Feature feature) {
			string requestData = "{" +
				$"\"code\":\"{feature.Code}\",\"name\":\"{feature.Name}\",\"description\":\"{feature.Descriptor}\"" + "}";
			BpmonlineClient.CallConfigurationService(FeatureServiceName, CreateMethodName, requestData);
			return this;
		}

		private FeatureModerator CallChangeFeatureState(Feature feature) {
			string requestData = "{" +
				$"\"code\":\"{feature.Code}\",\"state\":\"{(int)feature.State}\"" + "}";
			BpmonlineClient.CallConfigurationService(FeatureServiceName, SetFeatureStateMethodName, requestData);
			return this;
		}

	}
}
