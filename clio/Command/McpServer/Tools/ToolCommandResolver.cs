using System;
using Clio;
using Clio.UserEnvironment;
using Microsoft.Extensions.DependencyInjection;

namespace Clio.Command.McpServer.Tools;

public interface IToolCommandResolver {
	TCommand Resolve<TCommand>(EnvironmentOptions options);
}

public class ToolCommandResolver(ISettingsRepository settingsRepository) : IToolCommandResolver {
	public TCommand Resolve<TCommand>(EnvironmentOptions options) {
		EnvironmentSettings settings = settingsRepository.GetEnvironment(options);
		IServiceProvider container = new BindingsModule().Register(settings);
		return container.GetRequiredService<TCommand>();
	}
}
