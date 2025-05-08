using System;

using Clio.Common.Responses;

namespace Clio.Common;

public interface IJsonResponseFormater
{
    string Format<T>(T value);

    string Format(Exception exception);
}

public class JsonResponseFormater : IJsonResponseFormater
{
    private readonly IJsonConverter _jsonConverter;

    public JsonResponseFormater(IJsonConverter jsonConverter)
    {
        jsonConverter.CheckArgumentNull(nameof(jsonConverter));
        _jsonConverter = jsonConverter;
    }

    public string Format<T>(T value)
    {
        ValueResponse<T> valueResponse = new () { Value = value, Success = true, ErrorInfo = null };
        return _jsonConverter.SerializeObject(valueResponse);
    }

    public string Format(Exception exception)
    {
        ValueResponse<object> valueResponse = new ()
        {
            Value = null,
            Success = false,
            ErrorInfo = new ErrorInfo
            {
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                ErrorCode = string.Empty
            }
        };
        return _jsonConverter.SerializeObject(valueResponse);
    }
}
