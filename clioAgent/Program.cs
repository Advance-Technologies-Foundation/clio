using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Reflection;
using System.Security.Claims;
using clioAgent.AuthPolicies;
using clioAgent.ChainOfResponsibility;
using clioAgent.DbOperations;
using clioAgent.EndpointDefinitions;
using clioAgent.Handlers;
using clioAgent.Handlers.ChainLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace clioAgent;

public class TraceabaleAttribute : Attribute {
	public string? Name { get; set; }
	public string? Description { get; set; }
}

public static class Program {

	#region Constants: Private

	private const string DefaultServiceName = "clioAgent";
	private const int DefaultWaitBeforeShutdown = 1_000;
	private const string DefaultWorkingDirectoryPath = @"C:\ClioAgentArtifacts";

	#endregion

	#region Fields: Private

	private static readonly CancellationTokenSource CancellationTokenSource = new();

	#endregion

	#region Methods: Private

	private static void AddTelemetry(IHostApplicationBuilder builder, Settings? settings){
		
		
		string[] traceableTypes = typeof(Program).Assembly.GetTypes()
												.Where(t=> 
													t.CustomAttributes.Any(a=> a.AttributeType == typeof(TraceabaleAttribute)))
												.Select(t=> t.Name).ToArray();
			
		
		builder.Services.AddOpenTelemetry()
				.WithTracing(tracing => tracing
										.SetResourceBuilder(ResourceBuilder.CreateDefault()
																			.AddService(settings.ServiceName))
										.AddAspNetCoreInstrumentation()
										// .AddSource(nameof(RestoreDbHandler))
										//.AddSource(nameof(CopyFileLink))
										.AddSource(traceableTypes)
										.AddNpgsql()
										.AddOtlpExporter(options => {
											options.Endpoint = settings!.TraceServer!.CollectorUrl!;
											options.Protocol = OtlpExportProtocol.Grpc;
										}));
	}

	private static void ConfigureAuth(IHostApplicationBuilder builder){
		builder.Services.AddAuthorizationBuilder()
				.AddPolicy("AdminPolicy", policy =>
					policy.Requirements.Add(new AuthorizationRequirement(ClaimTypes.Role, Roles.Admin)))
				.AddPolicy("ReadPolicy", policy =>
					policy.Requirements.Add(new AuthorizationRequirement(ClaimTypes.Role, Roles.Read)))
				.AddFallbackPolicy("fallBackPolicy",
					new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

		builder.Services.AddAuthentication(options => {
					options.DefaultAuthenticateScheme = ApiKeyAuthenticationHandler.SchemeName;
					options.DefaultChallengeScheme = ApiKeyAuthenticationHandler.SchemeName;
				})
				.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
					ApiKeyAuthenticationHandler.SchemeName, options => {
						builder.Configuration.Bind("ApiKeySettings", options);
						options.TimeProvider = TimeProvider.System;
					});
	}

	private static void ConfigureDi(IHostApplicationBuilder builder){
		
		builder.Services.AddChainOfResponsibility();
		builder.Services.AddChainLink<CopyFileLink, RequestContext, ResponseContext>();
		builder.Services.AddChainLink<UnzipLink, RequestContext, ResponseContext>();
		builder.Services.AddChainLink<CreatePgDbPgLink, RequestContext, ResponseContext>();
		builder.Services.AddChainLink<ResorePgLink, RequestContext, ResponseContext>();
		builder.Services.AddChainLink<ResoreMsLink, RequestContext, ResponseContext>();
		builder.Services.AddChainLink<CreateDbFromTemplateLink, RequestContext, ResponseContext>();
		
		builder.Services.AddChain<RequestContext, ResponseContext>((sp, chainBuilder) => {
			// Get links from the service provider
			chainBuilder
				.AddLink(sp.GetRequiredService<CopyFileLink>())
				.AddLink(sp.GetRequiredService<UnzipLink>())
				.AddLink(sp.GetRequiredService<CreatePgDbPgLink>())
				.AddLink(sp.GetRequiredService<ResorePgLink>())
				.AddLink(sp.GetRequiredService<CreateDbFromTemplateLink>());
		});
		
		builder.Services.AddSingleton<IFileSystem>(new FileSystem());
		builder.Services.AddSingleton<IPostgres, Postgres>();
		builder.Services.AddSingleton(new ConcurrentQueue<BaseJob<IHandler>>());
		builder.Services.AddSingleton(new ConcurrentDictionary<Guid, JobStatus>());
		builder.Services.AddSingleton(new ConcurrentBag<JobStatus>());
		builder.Services.AddSingleton<Worker>();
		builder.Services.AddTransient<RestoreDbHandler>();
		builder.Services.AddTransient<DeployIISHandler>();
		builder.Services.AddSingleton<IAuthorizationHandler, CustomAuthorizationHandler>();
		builder.Services.AddSingleton<ConnectionStringsFileHandler>();
		builder.Services
				.AddSingleton<IAuthorizationMiddlewareResultHandler, CustomAuthorizationMiddlewareResultHandler>();
	}

	[UnconditionalSuppressMessage("Trimming", "IL2026",
		Justification = "Data annotations validation is safe for this scenario")]
	private static void ConfigureObjectValidators(IHostApplicationBuilder builder){
		builder.Services.AddOptions<DeploySiteHandlerArgs>().ValidateDataAnnotations();
		builder.Services.AddSingleton<IValidateOptions<DeploySiteHandlerArgs>, DeploySiteHandlerArgsValidator>();
		builder.Services.AddOptions<BaseJobValidator<IHandler>>().ValidateDataAnnotations();
		builder.Services.AddSingleton<IValidateOptions<BaseJob<IHandler>>, BaseJobValidator<IHandler>>();
		builder.Services.AddOptions<ConnectionStringsFileHandlerArgs>().ValidateDataAnnotations();
		builder.Services
				.AddSingleton<IValidateOptions<ConnectionStringsFileHandlerArgs>,
					ConnectionStringsFileHandlerArgsValidator>();
	}

	private static Settings? ConfigureSettings(IHostApplicationBuilder builder){
		string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
		Console.WriteLine($"Environment: {environment}");
		Console.WriteLine($"BaseDirectory: {AppContext.BaseDirectory}");
		Console.WriteLine($"CurrentDirectory: {Directory.GetCurrentDirectory()}");

		IConfigurationRoot configuration = new ConfigurationBuilder()
											// .SetBasePath(Directory.GetCurrentDirectory())
											.SetBasePath(AppContext.BaseDirectory)
											.AddJsonFile("appsettings.json", false, true)
											.AddJsonFile($"appsettings.{environment}.json", true, true)
											.Build();

		builder.Configuration.AddConfiguration(configuration);

		CreatioProducts[]? creatioProducts = configuration.GetSection("CreatioProducts").Get<CreatioProducts[]>();
		Db[]? db = configuration.GetSection("Db").Get<Db[]>();
		TraceServer? traceServer = configuration.GetSection("TraceServer").Get<TraceServer>();

		string workingDirectoryPath = configuration.GetSection("WorkingDirectoryPath").Get<string?>() ??
			DefaultWorkingDirectoryPath;
		if (!Directory.Exists(workingDirectoryPath)) {
			Console.WriteLine("Directory {0} created", workingDirectoryPath);
			Directory.CreateDirectory(workingDirectoryPath);
		}

		string serviceName = configuration.GetSection("ServiceName").Get<string?>() ?? DefaultServiceName;
		int waitBeforeShutDown
			= configuration.GetSection("WaitBeforeShutDown").Get<int?>() ?? DefaultWaitBeforeShutdown;

		Settings settings = new(creatioProducts,
			db,
			workingDirectoryPath,
			traceServer,
			serviceName,
			waitBeforeShutDown);
		builder.Services.AddSingleton(settings);
		return settings;
	}

	private static void ConfigureSwagger(IHostApplicationBuilder builder){
		//https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-8.0&tabs=visual-studio%2Cminimal-apis
		builder.Services.Configure<RouteOptions>(options =>
			options.SetParameterPolicy<RegexInlineRouteConstraint>("regex"));
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen(options => {
			options.SwaggerDoc("v1", new OpenApiInfo {Title = "clioAgent API", Version = "v1"});
			options.AddSecurityDefinition(ApiKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme {
				Description = ApiKeyAuthenticationHandler.SchemeName,
				Name = ApiKeyAuthenticationHandler.SchemeName,
				In = ParameterLocation.Header,
				Type = SecuritySchemeType.ApiKey
			});
			
			options.AddSecurityRequirement(new OpenApiSecurityRequirement {
				{
					new OpenApiSecurityScheme {
						Name = ApiKeyAuthenticationHandler.SchemeName,
						Type = SecuritySchemeType.ApiKey,
						In = ParameterLocation.Header,
						Reference = new OpenApiReference {
							Type = ReferenceType.SecurityScheme,
							Id = ApiKeyAuthenticationHandler.SchemeName
						}
					},
					[]
				}
			});
		});
	}

	private static void StartWorkerThread(IHost app){
		int coreCount = Environment.ProcessorCount;
		for (int i = 0; i < coreCount; i++) {
			new Thread(() => {
				Worker worker = app.Services.GetRequiredService<Worker>();
				worker.Run(CancellationTokenSource.Token);
			}).Start();
		}
	}

	private static void UseSwagger(IApplicationBuilder app){
		app.UseSwagger();
		app.UseSwaggerUI();
	}

	#endregion

	#region Methods: Public

	public static async Task Main(string[] args){
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
		builder.Host.UseWindowsService(s => { });

		builder.Services.ConfigureHttpJsonOptions(options => {
			options.SerializerOptions.TypeInfoResolverChain.Insert(0, AgentJsonSerializerContext.Default);
		});
		Settings? settings = ConfigureSettings(builder);

		if (settings == null) {
			await Console.Error.WriteLineAsync("No configuration found.");
			throw new Exception("No configuration setting found.");
		}
		if (settings.TraceServer?.Enabled == true && settings.TraceServer.CollectorUrl != null) {
			AddTelemetry(builder, settings);
		}

		ConfigureDi(builder);
		ConfigureAuth(builder);
		ConfigureObjectValidators(builder);

		builder.Services.AddEndpointDefinitions(typeof(Program));

		ConfigureSwagger(builder);

		WebApplication app = builder.Build();
		UseSwagger(app);
		StartWorkerThread(app);
		app.UseAuthentication();
		app.UseAuthorization();
		app.UseEndpointDefinitions();

		app.MapGet("/turn-off", (IHostApplicationLifetime appLifetime) => {
			CancellationTokenSource.Cancel();
			appLifetime.StopApplication();
			Thread.Sleep(settings.WaitBeforeShutDown);
			return Results.Ok("Shutting down");
		});

		await app.RunAsync();
	}

	#endregion

}

public record StepStatus(Guid JobId, string? Message, DateTime Date, string CurrentStatus, Guid? StepId);
