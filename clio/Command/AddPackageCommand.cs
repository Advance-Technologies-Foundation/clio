namespace Clio.Command
{
	using System;
	using CommandLine;
	using Clio.Package;
    using Clio.Common;

    #region Class: AddPackageOptions

    [Verb("add-package", Aliases = new string[] { "ap" }, HelpText = "Add package to workspace or local folder")]
	public class AddPackageOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
		public string Name { get; set; }

        [Option('a', "asApp", Required = false,
            HelpText = "Create application in package", Default = false)]
        public bool asApp {
		 get; set; }

		#endregion

	}

	#endregion

	#region Class: AddPackageCommand

	public class AddPackageCommand : Command<AddPackageOptions>
	{

		#region Fields: Private

		private readonly IPackageCreator _packageCreator;
        private readonly ILogger _logger;

        #endregion

        #region Constructors: Public

        public AddPackageCommand(IPackageCreator packageCreator, ILogger logger) {
			_packageCreator = packageCreator;
            this._logger = logger;
        }

		#endregion

		#region Methods: Public

		public override int Execute(AddPackageOptions options) {
			_packageCreator.Create(options.Name, options.asApp);
            _logger.WriteInfo("Done");
			return 0;
		}

		#endregion

	}

	#endregion

}
