using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Project;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;
using Newtonsoft.Json;

namespace Clio.Command;

[Verb("add-item", Aliases = ["create"], HelpText = "Create item in project")]
internal class AddItemOptions : EnvironmentOptions{
	#region Properties: Protected

	internal override bool RequiredEnvironment => false;

	#endregion

	#region Properties: Public

	[Value(0, MetaName = "Item type", Required = true, HelpText = "Item type")]
	public string ItemType { get; set; }

	[Value(1, MetaName = "Item name", Required = false, HelpText = "Item name")]
	public string ItemName { get; set; }

	[Option('d', "DestinationPath", Required = false, HelpText = "Path to source directory.", Default = null)]
	public string DestinationPath { get; set; }

	[Option('n', "Namespace", Required = false, HelpText = "Name space for service classes.", Default = null)]
	public string Namespace { get; set; }

	[Option('f', "Fields", Required = false, HelpText = "Required fields for model class", Default = null)]
	public string Fields { get; set; }

	[Option('a', "All", Required = false, HelpText = "Create all models", Default = true)]
	public bool CreateAll { get; set; }

	[Option('x', "Culture", Required = false, HelpText = "Description culture", Default = "en-US")]
	public string Culture { get; set; }

	#endregion
}

internal class AddItemOptionsValidator : AbstractValidator<AddItemOptions>{
	#region Constructors: Public

	public AddItemOptionsValidator() {
		RuleFor(x => x.ItemType).NotEmpty().WithMessage("Item type is required.");
		RuleFor(x => x.Namespace)
			.NotEmpty()
			.When(x => string.Equals(x.ItemType, "model", StringComparison.OrdinalIgnoreCase))
			.WithMessage("Namespace is required for model generation.");
		RuleFor(x => x.ItemName)
			.NotEmpty()
			.When(x => !string.Equals(x.ItemType, "model", StringComparison.OrdinalIgnoreCase)
					   || (string.Equals(x.ItemType, "model", StringComparison.OrdinalIgnoreCase) && !x.CreateAll))
			.WithMessage("Item name is required.");
	}

	#endregion
}

internal class AddItemCommand : Command<AddItemOptions>{
	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly ILogger _logger;
	private readonly IValidator<AddItemOptions> _optionsValidator;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IVsProjectFactory _vsProjectFactory;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;

	#endregion

	#region Constructors: Public

	public AddItemCommand(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder,
		IWorkingDirectoriesProvider workingDirectoriesProvider, IValidator<AddItemOptions> optionsValidator,
		IVsProjectFactory vsProjectFactory, ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_optionsValidator = optionsValidator;
		_vsProjectFactory = vsProjectFactory;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private static string CorrectJson(string body) {
		body = body.Replace("\\\\r\\\\n", Environment.NewLine);
		body = body.Replace("\\r\\n", Environment.NewLine);
		body = body.Replace("\\\\n", Environment.NewLine);
		body = body.Replace("\\n", Environment.NewLine);
		body = body.Replace("\\\\t", Convert.ToChar(9).ToString());
		body = body.Replace("\\\"", "\"");
		body = body.Replace("\\\\", "\\");
		body = body.Trim('"');
		return body;
	}

	private int AddItemFromTemplate(AddItemOptions options) {
		IVsProject project = _vsProjectFactory.Create(options.DestinationPath, options.Namespace);
		CreatioEnvironment creatioEnv = new();
		string tplPath = $"tpl{Path.DirectorySeparatorChar}{options.ItemType}-template.tpl";
		if (!File.Exists(tplPath)) {
			string envPath = creatioEnv.GetAssemblyFolderPath();
			if (!string.IsNullOrEmpty(envPath)) {
				tplPath = Path.Combine(envPath, tplPath);
			}
		}

		string templateBody = File.ReadAllText(tplPath);
		project.AddFile(options.ItemName, templateBody.Replace("<Name>", options.ItemName));
		project.Reload();
		return 0;
	}

	private int AddModels(AddItemOptions opts) {
		if (opts.CreateAll) {
			_logger.WriteInfo("Generating models...");
			ModelBuilder.ModelBuilder modelBuilder =
				new(_applicationClient, opts, _workingDirectoriesProvider, _serviceUrlBuilder);
			modelBuilder.GetModels();
			return 0;
		}

		Dictionary<string, string> models = GetClassModels(opts.ItemName, opts.Fields);
		IVsProject project = _vsProjectFactory.Create(opts.DestinationPath, opts.Namespace);
		foreach (KeyValuePair<string, string> model in models) {
			project.AddFile(model.Key, model.Value);
		}

		project.Reload();
		_logger.WriteInfo("Done");
		return 0;
	}

	private Dictionary<string, string> GetClassModels(string entitySchemaName, string fields) {
		string url = _serviceUrlBuilder.Build(
			$"/rest/CreatioApiGateway/GetEntitySchemaModels/{entitySchemaName}/{fields}");
		string responseFromServer = _applicationClient.ExecuteGetRequest(url);
		string result = CorrectJson(responseFromServer);
		return JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
	}

	#endregion

	#region Methods: Public

	public override int Execute(AddItemOptions options) {
		ValidationResult validationResult = _optionsValidator.Validate(options);
		if (!validationResult.IsValid) {
			foreach (ValidationFailure error in validationResult.Errors) {
				_logger.WriteError(error.ErrorMessage);
			}

			return 1;
		}

		try {
			if (options.ItemType.Equals("model", StringComparison.OrdinalIgnoreCase)) {
				return AddModels(options);
			}

			return AddItemFromTemplate(options);
		}
		catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion
}
