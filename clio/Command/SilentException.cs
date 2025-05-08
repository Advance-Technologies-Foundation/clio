using System;
using System.Runtime.Serialization;

namespace Clio.Command;

[Serializable]
internal class SilentException : Exception
{

    #region Constructors: Protected

    protected SilentException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    { }

    #endregion

    #region Constructors: Public

    public SilentException()
    { }

    public SilentException(string message)
        : base(message)
    { }

    public SilentException(string message, Exception innerException)
        : base(message, innerException)
    { }

    #endregion

}
