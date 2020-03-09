using System;
using Newtonsoft.Json;

namespace Clio.Common
{

	#region Class: JsonConverter
	
	public class JsonConverter : IJsonConverter
	{

		#region Methods: Public

		public string CorrectJson(string body) {
			body = body.Replace("\\\\r\\\\n", Environment.NewLine);
			body = body.Replace("\\\\n", Environment.NewLine);
			body = body.Replace("\\r\\n", Environment.NewLine);
			body = body.Replace("\\n", Environment.NewLine);
			body = body.Replace("\\\\t", Convert.ToChar(9).ToString());
			body = body.Replace("\\\"", "\"");
			body = body.Replace("\\\\", "\\");
			body = body.Trim(new Char[] { '\"' });
			return body;
		}

		public T DeserializeObject<T>(string value) {
			return JsonConvert.DeserializeObject<T>(value);
		}

		#endregion

	}

	#endregion

}