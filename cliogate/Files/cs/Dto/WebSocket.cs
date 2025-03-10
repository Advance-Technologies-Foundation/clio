using System;
using Newtonsoft.Json;
using Terrasoft.Common.Json;

namespace Cliogate.Dto
{
	internal class WebSocket
	{

		#region Constructors: Public
		public WebSocket(string commandName, string message) {
			CommandName = commandName;
			Message = message;
		}

		#endregion

		#region Properties: Public

		[JsonProperty("commandName")]
		public string CommandName { get; private set; }

		[JsonProperty("message")]
		public string Message { get; private set; }
		
		#endregion

		#region Methods: Public

		public override string ToString(){
			return Json.FormatJsonString(Json.Serialize(this), Formatting.Indented);
		} 
		

		#endregion
	}
}