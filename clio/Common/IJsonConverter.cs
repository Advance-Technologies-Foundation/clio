namespace Clio.Common
{
	public interface IJsonConverter
	{
		string CorrectJson(string body);
		T DeserializeObject<T>(string value);
		T DeserializeObjectFromFile<T>(string jsonPath);
		void SerializeObjectToFile<T>(T value, string jsonPath);
		string SerializeObject<T>(T value);
	}
}