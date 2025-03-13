namespace clioAgent.ChainOfResponsibility;

/// <summary>
///  Default implementation of the chain builder.
/// </summary>
/// <typeparam name="TRequest">The type of request being processed.</typeparam>
/// <typeparam name="TResponse">The type of response returned after processing.</typeparam>
public class ChainBuilder<TRequest, TResponse> : IChainBuilder<TRequest, TResponse> {

	#region Fields: Private

	private readonly List<IChainLink<TRequest, TResponse>> _links = new();

	#endregion

	#region Methods: Public

	/// <summary>
	///  Adds a link to the chain.
	/// </summary>
	/// <param name="link">The link to add.</param>
	/// <returns>The builder for method chaining.</returns>
	public IChainBuilder<TRequest, TResponse> AddLink(IChainLink<TRequest, TResponse> link){
		_links.Add(link);
		return this;
	}

	/// <summary>
	///  Builds the chain.
	/// </summary>
	/// <returns>The chain handler that executes the chain.</returns>
	public IChainHandler<TRequest, TResponse> Build(){
		return new ChainHandler<TRequest, TResponse>(_links);
	}

	#endregion

}