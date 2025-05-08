namespace Clio.Common;

public interface IJsonConverter
{

    #region Methods: Public

    string CorrectJson(string body);

    T DeserializeObject<T>(string value);

    T DeserializeObjectFromFile<T>(string jsonPath);

    string SerializeObject<T>(T value);

    void SerializeObjectToFile<T>(T value, string jsonPath);

    #endregion

}
