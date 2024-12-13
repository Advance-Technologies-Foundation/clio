using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using Newtonsoft.Json;

namespace Clio.Common;

public interface IJsonConverter
{
	/// <summary>
	/// Corrects the JSON string by replacing escape sequences with their actual characters.
	/// </summary>
	/// <remarks>
	/// This method performs the following replacements:
	/// <list type="bullet">
	/// <item>- Replaces "\\\\r\\\\n" and "\\\\n" with the system's newline character.</item>
	/// <item>- Replaces "\\r\\n" and "\\n" with the system's newline character.</item>
	/// <item>- Replaces "\\\\t" with a tab character.</item>
	/// <item>- Replaces "\\\"" with a double quote.</item>
	/// <item>- Replaces "\\\\" with a single backslash.</item>
	/// <item>- Trims leading and trailing double quotes.</item>
	/// </list>
	/// </remarks>
	/// <param name="body">The JSON string to correct.</param>
	/// <returns>The corrected JSON string.</returns>
	string SanitizeJsonString(string body);
	
	/// <summary>
	/// Deserializes the JSON string to an object of type T.
	/// </summary>
	/// <typeparam name="T">The type of the object to deserialize to.</typeparam>
	/// <param name="value">The JSON string to deserialize.</param>
	/// <returns>The deserialized object of type T.</returns>
	T DeserializeObject<T>(string value);
	
	/// <summary>
	/// Deserializes the JSON content from a file to an object of type T.
	/// </summary>
	/// <typeparam name="T">The type of the object to deserialize to.</typeparam>
	/// <param name="jsonFilePath">The path to the JSON file.</param>
	/// <returns>The deserialized object of type T.</returns>
	/// <exception cref="FileNotFoundException">Thrown when the JSON file is not found at the specified path.</exception>
	/// <remarks>
	/// This method reads the JSON content from the specified file and deserializes it into an object of type T using Newtonsoft.Json.
	/// </remarks>
	T DeserializeJsonFromFile<T>(string jsonFilePath);
	
	
	void SerializeObjectToFile<T>(T value, string jsonFilePath);
	
	
	string SerializeObject<T>(T? value);
}
	
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
	
	public string SanitizeJsonString(string body) {
		
		if(string.IsNullOrEmpty(body)){
			return body;
		}
		return body
			.Replace(@"\\r\\n", Environment.NewLine)
			.Replace(@"\\n", Environment.NewLine)
			.Replace(@"\r\n", Environment.NewLine)
			.Replace(@"\n", Environment.NewLine)
			.Replace(@"\\t", "\t")
			.Replace(@"\""", "\"")
			.Replace(@"\\", "\\")
			.Trim('"');
	}
	
	/// <inheritdoc cref="JsonConvert.DeserializeObject{T}(string)"/>
	/// <remarks>Uses Newtonsoft.Json</remarks>
	[ExcludeFromCodeCoverage(Justification = "Method is a simple wrapper around JsonConvert.DeserializeObject " +
		"and does not contain any logic to test")]
	public T DeserializeObject<T>(string value) => JsonConvert.DeserializeObject<T>(value);
	
	public T DeserializeJsonFromFile<T>(string jsonFilePath) {
		if (!_fileSystem.ExistsFile(jsonFilePath)) {
			throw new FileNotFoundException($"Json file not found by path: '{jsonFilePath}'");
		}
		string json = _fileSystem.ReadAllText(jsonFilePath);
		return DeserializeObject<T>(json);
	}

	public void SerializeObjectToFile<T>(T value, string jsonFilePath) {
		using FileSystemStream fileSystemStream = _fileSystem.ExistsFile(jsonFilePath) switch {
			true => _fileSystem.FileOpenStream(jsonFilePath, FileMode.Open, FileAccess.Write, FileShare.Write),
			false => _fileSystem.CreateFile(jsonFilePath)
		};
		fileSystemStream.Position = 0;
		fileSystemStream.Seek(0, SeekOrigin.Begin);
		using StreamWriter writer = new (fileSystemStream, Encoding.UTF8);
		string payload = JsonConvert.SerializeObject(value, Formatting.Indented);
		writer.Write(payload);
		
	}

	/// <inheritdoc cref="JsonConvert.SerializeObject(object?, Formatting)"/>
	/// <remarks>Uses Newtonsoft.Json</remarks>
	public string SerializeObject<T>(T? value) => JsonConvert.SerializeObject(value, Formatting.Indented);

	#endregion

}

#endregion