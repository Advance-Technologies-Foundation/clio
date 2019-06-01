using System;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using Terrasoft.Core;
using Terrasoft.Web.Common;

namespace <Namespace>
{
	[ServiceContract]
	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
	public class <Name>: BaseService
	{

		[OperationContract]
		[WebInvoke(Method = "POST", BodyStyle = WebMessageBodyStyle.Wrapped,
			RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public string ExecutePost(string script) {
			return UserConnection.CurrentUser.Name;
		}

		[OperationContract]
		[WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json,
			BodyStyle = WebMessageBodyStyle.Wrapped, ResponseFormat = WebMessageFormat.Json)]
		public string ExecuteGet() {
			return UserConnection.CurrentUser.Name;
		}

	}
}