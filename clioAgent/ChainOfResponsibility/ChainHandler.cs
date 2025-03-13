namespace clioAgent.ChainOfResponsibility;

/// <summary>
///  Default implementation of the chain handler.
/// </summary>
/// <typeparam name="TRequest">The type of request being processed.</typeparam>
/// <typeparam name="TResponse">The type of response returned after processing.</typeparam>
internal class ChainHandler<TRequest, TResponse> : IChainHandler<TRequest, TResponse> {

	#region Class: Private

	private class NextChainLink : IChainLink<TRequest, TResponse> {

		#region Fields: Private

		private readonly ChainHandler<TRequest, TResponse> _handler;
		private readonly int _index;

		#endregion

		#region Constructors: Public

		public NextChainLink(ChainHandler<TRequest, TResponse> handler, int index){
			_handler = handler;
			_index = index;
		}

		#endregion

		#region Methods: Public

		public Task<TResponse> ExecuteAsync(TRequest request, IChainLink<TRequest, TResponse> next){
			return _handler.ExecuteChainAsync(_index, request);
		}

		#endregion

	}

	#endregion

	#region Fields: Private

	private readonly IList<IChainLink<TRequest, TResponse>> _links;

	#endregion

	#region Constructors: Public

	public ChainHandler(IList<IChainLink<TRequest, TResponse>> links){
		_links = links;
	}

	#endregion

	#region Methods: Private

	private Task<TResponse> ExecuteChainAsync(int index, TRequest request){
		if (index >= _links.Count) {
			return Task.FromResult(default(TResponse));
		}

		IChainLink<TRequest, TResponse> currentLink = _links[index];
		IChainLink<TRequest, TResponse> nextLink = null;

		if (index < _links.Count - 1) {
			nextLink = new NextChainLink(this, index + 1);
		}

		return currentLink.ExecuteAsync(request, nextLink);
	}

	#endregion

	#region Methods: Public

	/// <summary>
	///  Handles the request by executing the chain.
	/// </summary>
	/// <param name="request">The request to process.</param>
	/// <returns>The response after processing.</returns>
	public Task<TResponse> HandleAsync(TRequest request){
		if (_links.Count == 0) {
			return Task.FromResult(default(TResponse));
		}
		return ExecuteChainAsync(0, request);
	}

	#endregion

}
