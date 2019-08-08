namespace bpmcligate.Files.cs.Feature
{
	using System.ServiceModel;
	using System.ServiceModel.Activation;
	using System.ServiceModel.Web;
	using Terrasoft.Web.Common;

	/// <summary>
	/// Provides web-service for work with features.
	/// </summary>
	[ServiceContract]
	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
	public class FeatureStateService : BaseService
	{
		/// <summary>
		/// Sets feature state for current user.
		/// </summary>
		/// <param name="code">Feature code.</param>
		/// <param name="state">New feature state.</param>
		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "SetFeatureState", BodyStyle = WebMessageBodyStyle.Wrapped,
			RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public void SetFeatureState(string code, int state) {
			var featureRepo = new FeatureRepository(UserConnection);
			featureRepo.SetFeatureState(code, state);
		}
	}
}
