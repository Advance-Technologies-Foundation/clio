using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.ModelBuilder;
using Clio.Project;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;
using Newtonsoft.Json;
using IFileSystem = System.IO.Abstractions.IFileSystem;

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

	[Option('d', "destination-path", Required = false, HelpText = "Path to source directory.", Default = null)]
	public string DestinationPath { get; set; }

	[Option("DestinationPath", Required = false, Hidden = true, HelpText = "Alias for --destination-path")]
	public string DestinationPathAlias {
		get => DestinationPath;
		set { if (!string.IsNullOrEmpty(value)) DestinationPath = value; }
	}

	[Option('n', "namespace", Required = false, HelpText = "Name space for service classes.", Default = null)]
	public string Namespace { get; set; }

	[Option("Namespace", Required = false, Hidden = true, HelpText = "Alias for --namespace")]
	public string NamespaceAlias {
		get => Namespace;
		set { if (!string.IsNullOrEmpty(value)) Namespace = value; }
	}

	[Option('f', "fields", Required = false, HelpText = "Required fields for model class", Default = null)]
	public string Fields { get; set; }

	[Option("Fields", Required = false, Hidden = true, HelpText = "Alias for --fields")]
	public string FieldsAlias {
		get => Fields;
		set { if (!string.IsNullOrEmpty(value)) Fields = value; }
	}

	[Option('a', "all", Required = false, HelpText = "Create all models", Default = true)]
	public bool CreateAll { get; set; }

	[Option("All", Required = false, Hidden = true, HelpText = "Alias for --all")]
	public bool CreateAllAlias {
		get => CreateAll;
		set { if (value) CreateAll = value; }
	}

	[Option('x', "culture", Required = false, HelpText = "Description culture")]
	public string Culture { get; set; } = "en-US";

	[Option("Culture", Required = false, Hidden = true, HelpText = "Alias for --culture")]
	public string CultureAlias {
		get => Culture;
		set { if (!string.IsNullOrEmpty(value)) Culture = value; }
	}

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

internal class AddItemCommand(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IValidator<AddItemOptions> optionsValidator,
	IVsProjectFactory vsProjectFactory,
	ILogger logger,
	IFileSystem fileSystem,
	IModelBuilder modelBuilder,
	ICreatioEnvironment creatioEnvironment)
	: Command<AddItemOptions>{

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
		IVsProject project = vsProjectFactory.Create(options.DestinationPath, options.Namespace);
		string tplPath = $"tpl{Path.DirectorySeparatorChar}{options.ItemType}-template.tpl";
		if (!fileSystem.File.Exists(tplPath)) {
			string envPath = creatioEnvironment.GetAssemblyFolderPath();
			if (!string.IsNullOrEmpty(envPath)) {
				tplPath = Path.Combine(envPath, tplPath);
			}
		}

		string templateBody = fileSystem.File.ReadAllText(tplPath);
		project.AddFile(options.ItemName, templateBody.Replace("<Name>", options.ItemName));
		project.Reload();
		return 0;
	}

	private int AddModels(AddItemOptions opts) {
		if (opts.CreateAll) {
			logger.WriteInfo("Generating models...");
			modelBuilder.GetModels(opts);
			return 0;
		}

		Dictionary<string, string> models = GetClassModels(opts.ItemName, opts.Fields);
		IVsProject project = vsProjectFactory.Create(opts.DestinationPath, opts.Namespace);
		foreach (KeyValuePair<string, string> model in models) {
			project.AddFile(model.Key, model.Value);
		}

		project.Reload();
		logger.WriteInfo("Done");
		return 0;
	}

	private Dictionary<string, string> GetClassModels(string entitySchemaName, string fields) {
		string url = serviceUrlBuilder.Build(
			$"/rest/CreatioApiGateway/GetEntitySchemaModels/{entitySchemaName}/{fields}");
		string responseFromServer = applicationClient.ExecuteGetRequest(url);
		string result = CorrectJson(responseFromServer);
		return JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
	}

	#endregion

	#region Methods: Public

	public override int Execute(AddItemOptions options) {
		ValidationResult validationResult = optionsValidator.Validate(options);
		if (!validationResult.IsValid) {
			foreach (ValidationFailure error in validationResult.Errors) {
				logger.WriteError(error.ErrorMessage);
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
			logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion
}
