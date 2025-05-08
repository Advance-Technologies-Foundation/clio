using System;
using System.Collections.Specialized;
using System.Web;

namespace Clio.Requests;

internal class BaseExternalLinkHandler
{
    /// <summary>
    ///     Request Uri.
    /// </summary>
    protected Uri _clioUri;

    /// <summary>
    ///     Gets collection of Query parameters.
    /// </summary>
    protected NameValueCollection ClioParams => HttpUtility.ParseQueryString(_clioUri.Query);
}
