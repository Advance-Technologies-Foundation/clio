namespace Clio.Common
{
	using System;
	using System.IO;
	using Newtonsoft.Json;

	#region Class: JsonConverter
	
	public class JsonConverter : IJsonConverter
	{

		private readonly IFileSystem _fileSystem;

		public JsonConverter(IFileSystem fileSystem){
			_fileSystem = fileSystem;
		}
		public JsonConverter(){
			
		}
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
			if (!_fileSystem.ExistsFile(jsonPath)) {
				throw new FileNotFoundException($"Json file not found by path: '{jsonPath}'");
			}
			string json = _fileSystem.ReadAllText(jsonPath);
			return DeserializeObject<T>(json);
		}

		public void SerializeObjectToFile<T>(T value, string jsonPath) {
			if (!_fileSystem.ExistsFile(jsonPath)) {
				
				var fileStream = _fileSystem.CreateFile(jsonPath);
				fileStream.Close();
			}
			_fileSystem.WriteAllTextToFile(jsonPath, JsonConvert.SerializeObject(value, Formatting.Indented));
		}

		public string SerializeObject<T>(T value) => JsonConvert.SerializeObject(value, Formatting.Indented);

		#endregion

	}

	#endregion

}