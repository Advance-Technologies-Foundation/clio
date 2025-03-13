namespace clioAgent.ChainOfResponsibility;

/// <summary>
///  Represents a link in the chain of responsibility.
/// </summary>
/// <typeparam name="TRequest">The type of request being processed.</typeparam>
/// <typeparam name="TResponse">The type of response returned after processing.</typeparam>
public interface IChainLink<TRequest, TResponse> {

	#region Methods: Public

    /// <summary>
    ///  Executes this link's processing logic on the request.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <param name="next">The next link in the chain, or null if this is the last link.</param>
    /// <returns>The response after processing.</returns>
    Task<TResponse> ExecuteAsync(TRequest request, IChainLink<TRequest, TResponse> next);

	#endregion

}

/// <summary>
///  Chain of responsibility builder interface.
/// </summary>
/// <typeparam name="TRequest">The type of request being processed.</typeparam>
/// <typeparam name="TResponse">The type of response returned after processing.</typeparam>
public interface IChainBuilder<TRequest, TResponse> {

	#region Methods: Public

    /// <summary>
    ///  Adds a link to the chain.
    /// </summary>
    /// <param name="link">The link to add.</param>
    /// <returns>The builder for method chaining.</returns>
    IChainBuilder<TRequest, TResponse> AddLink(IChainLink<TRequest, TResponse> link);

    /// <summary>
    ///  Builds the chain.
    /// </summary>
    /// <returns>The first link in the chain.</returns>
    IChainHandler<TRequest, TResponse> Build();

	#endregion

}

/// <summary>
///  Interface for the chain handler that executes the chain.
/// </summary>
/// <typeparam name="TRequest">The type of request being processed.</typeparam>
/// <typeparam name="TResponse">The type of response returned after processing.</typeparam>
public interface IChainHandler<TRequest, TResponse> {

	#region Methods: Public

    /// <summary>
    ///  Handles the request by executing the chain.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <returns>The response after processing.</returns>
    Task<TResponse> HandleAsync(TRequest request);

	#endregion

}
