namespace Clio.Common
{
	public interface IJsonConverter
	{
		string CorrectJson(string body);
		T DeserializeObject<T>(string value);
	}
}