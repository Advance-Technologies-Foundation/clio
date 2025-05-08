using System;
using System.Collections.Specialized;
using System.Web;

namespace Clio.Requests;

internal class BaseExternalLinkHandler
{

    #region Fields: Protected

    /// <summary>
    ///     Request Uri
    /// </summary>
    protected Uri _clioUri;

    #endregion

    #region Properties: Protected

    /// <summary>
    ///     Collection of Query parameters
    /// </summary>
    protected NameValueCollection ClioParams => HttpUtility.ParseQueryString(_clioUri.Query);

    #endregion

}
