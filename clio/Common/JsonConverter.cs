namespace Clio.Common
{
	using System;
	using System.IO;
	using Newtonsoft.Json;

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

		public T DeserializeObjectFromFile<T>(string jsonPath) {
			if (!File.Exists(jsonPath)) {
				throw new FileNotFoundException($"Json file not found by path: '{jsonPath}'");
			}
			string json = File.ReadAllText(jsonPath);
			return DeserializeObject<T>(json);
		}

		public void SerializeObjectToFile<T>(T value, string jsonPath) {
			if (!File.Exists(jsonPath)) {
				FileStream fileStream = File.Create(jsonPath);
				fileStream.Close();
			}
			File.WriteAllText(jsonPath, JsonConvert.SerializeObject(value, Formatting.Indented));
		}

		public string SerializeObject<T>(T value) => JsonConvert.SerializeObject(value, Formatting.Indented);

		#endregion

	}

	#endregion

}