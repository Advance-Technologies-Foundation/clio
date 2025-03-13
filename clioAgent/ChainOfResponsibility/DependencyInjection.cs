namespace clioAgent.ChainOfResponsibility;

/// <summary>
///  Extension methods for setting up chain of responsibility services in an IServiceCollection.
/// </summary>
public static class ServiceCollectionExtensions {

	#region Methods: Public

	/// <summary>
	///  Registers a pre-built chain handler.
	/// </summary>
	/// <typeparam name="TRequest">The type of request being processed.</typeparam>
	/// <typeparam name="TResponse">The type of response returned after processing.</typeparam>
	/// <param name="services">The IServiceCollection to add services to.</param>
	/// <param name="configureChain">A callback to configure the chain.</param>
	/// <param name="lifetime">The service lifetime (defaults to Scoped).</param>
	/// <returns>The IServiceCollection so that additional calls can be chained.</returns>
	public static IServiceCollection AddChain<TRequest, TResponse>(
		this IServiceCollection services,
		Action<IServiceProvider, IChainBuilder<TRequest, TResponse>> configureChain,
		ServiceLifetime lifetime = ServiceLifetime.Transient){
		services.Add(new ServiceDescriptor(typeof(IChainHandler<TRequest, TResponse>),
			sp => {
				IChainBuilder<TRequest, TResponse>
					builder = sp.GetRequiredService<IChainBuilder<TRequest, TResponse>>();
				configureChain(sp, builder);
				return builder.Build();
			},
			lifetime));

		return services;
	}

	/// <summary>
	///  Adds a chain link to the specified IServiceCollection.
	/// </summary>
	/// <typeparam name="TLink">The type of the chain link to add.</typeparam>
	/// <typeparam name="TRequest">The type of request being processed.</typeparam>
	/// <typeparam name="TResponse">The type of response returned after processing.</typeparam>
	/// <param name="services">The IServiceCollection to add services to.</param>
	/// <param name="lifetime">The service lifetime (defaults to Scoped).</param>
	/// <returns>The IServiceCollection so that additional calls can be chained.</returns>
	public static IServiceCollection AddChainLink<TLink, TRequest, TResponse>(
		this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Transient)
		where TLink : class, IChainLink<TRequest, TResponse>{
		
		
		services.Add(new ServiceDescriptor(typeof(IChainLink<TRequest, TResponse>), typeof(TLink), lifetime));

		services.Add(new ServiceDescriptor(typeof(TLink), typeof(TLink), lifetime));

		return services;
	}

	/// <summary>
	///  Adds chain of responsibility services to the specified IServiceCollection.
	/// </summary>
	/// <param name="services">The IServiceCollection to add services to.</param>
	/// <returns>The IServiceCollection so that additional calls can be chained.</returns>
	public static IServiceCollection AddChainOfResponsibility(this IServiceCollection services){
		services.AddTransient(typeof(IChainBuilder<,>), typeof(ChainBuilder<,>));
		return services;
	}

	#endregion

}
