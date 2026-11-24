using System;
using System.Linq;
using System.Reflection;
using cliogate.Files.cs;
using FluentAssertions;
using NUnit.Framework;

namespace cliogate.tests
{
	/// <summary>
	/// Covers how the gateway binds its arguments to the platform schema importer, which it calls late-bound
	/// because the importer's shape differs between Creatio cores.
	/// </summary>
	/// <remarks>
	/// The two interface shapes below mirror the platform: <c>ImportSchema(string, Guid)</c> before core
	/// 10.3.1146, and <c>ImportSchema(string, Guid, ExistingSchemaPackageAction)</c> from ENG-106408 on. The
	/// gateway supplies one argument list for both, so the binding has to drop the third argument for the old
	/// shape and convert it to the core's own enum for the new one. When it did neither, every import against a
	/// new core failed with "exposes neither a compatible static ... overload nor ISchemaImporter.ImportSchema".
	/// </remarks>
	[TestFixture]
	[System.ComponentModel.Category("UnitTest")]
	public class SchemaImporterArgumentBindingTests
	{

		#region Enum: Fakes

		/// <summary>Stands in for the platform's <c>ExistingSchemaPackageAction</c>.</summary>
		public enum FakeExistingSchemaPackageAction
		{
			None = 0,
			MoveToImportPackage = 1,
			ImportIntoExistingPackage = 2
		}

		/// <summary>An enum that has no <c>None</c> member.</summary>
		public enum FakeActionWithoutNone
		{
			MoveToImportPackage = 1
		}

		#endregion

		#region Interface: Fakes

		/// <summary>The importer contract of cores before 10.3.1146.</summary>
		public interface ITwoParameterImporter
		{
			string ImportSchema(string schemaData, Guid packageUId);
		}

		/// <summary>The importer contract of cores from 10.3.1146 on.</summary>
		public interface IThreeParameterImporter
		{
			string ImportSchema(string schemaData, Guid packageUId, FakeExistingSchemaPackageAction action);
		}

		/// <summary>A third parameter whose enum lacks the member the gateway asks for.</summary>
		public interface IImporterWithoutNone
		{
			string ImportSchema(string schemaData, Guid packageUId, FakeActionWithoutNone action);
		}

		/// <summary>A core that keeps the two-parameter importer next to the three-parameter one.</summary>
		public interface IImporterWithBothShapes
		{
			string ImportSchema(string schemaData, Guid packageUId);

			string ImportSchema(string schemaData, Guid packageUId, FakeExistingSchemaPackageAction action);
		}

		/// <summary>A third parameter that is not an enum at all.</summary>
		public interface IImporterWithStringThirdParameter
		{
			string ImportSchema(string schemaData, Guid packageUId, string action);
		}

		#endregion

		#region Constants: Private

		private const string SchemaData = "{\"Name\":\"UsrProbeSchema\"}";

		#endregion

		#region Fields: Private

		private static readonly Guid PackageUId = new Guid("0e9a4b49-3c4c-4b63-9f0c-6a4f1c0d2a11");

		#endregion

		#region Methods: Private

		private static MethodInfo ImportSchemaOf(Type contract) => contract.GetMethod("ImportSchema");

		private static object[] GatewayImportArguments() =>
			CreatioApiGateway.BuildImportInterfaceArguments(SchemaData, PackageUId);

		#endregion

		#region Methods: Public

		[Test]
		[Description("Drops the action argument for the two-parameter importer of cores before 10.3.1146")]
		public void MatchArguments_Should_Drop_The_Action_For_The_Two_Parameter_Importer(){
			// Arrange
			MethodInfo importSchema = ImportSchemaOf(typeof(ITwoParameterImporter));

			// Act
			object[] callArguments = CreatioApiGateway.MatchArguments(importSchema, GatewayImportArguments());

			// Assert
			callArguments.Should().Equal(new object[] {SchemaData, PackageUId},
				because: "an older core declares no third parameter, so the action is an undeclared trailing argument");
		}

		[Test]
		[Description("Passes the core's own None member to the three-parameter importer of cores from 10.3.1146 on")]
		public void MatchArguments_Should_Pass_The_Cores_None_Member_To_The_Three_Parameter_Importer(){
			// Arrange
			MethodInfo importSchema = ImportSchemaOf(typeof(IThreeParameterImporter));

			// Act
			object[] callArguments = CreatioApiGateway.MatchArguments(importSchema, GatewayImportArguments());

			// Assert
			callArguments.Should().NotBeNull(
				because: "rejecting this shape is the defect that made every import fail on a new core");
			callArguments.Should().HaveCount(3, because: "the new contract declares three parameters");
			callArguments[2].Should().BeOfType<FakeExistingSchemaPackageAction>(
				because: "MethodInfo.Invoke needs a value of the parameter's own enum type, not the placeholder");
			callArguments[2].Should().Be(FakeExistingSchemaPackageAction.None,
				because: "None makes the platform refuse a cross-package import instead of moving the schema");
		}

		[Test]
		[Description("Rejects an enum parameter that has no member of the requested name")]
		public void MatchArguments_Should_Reject_An_Enum_Without_The_Requested_Member(){
			// Arrange
			MethodInfo importSchema = ImportSchemaOf(typeof(IImporterWithoutNone));

			// Act
			object[] callArguments = CreatioApiGateway.MatchArguments(importSchema, GatewayImportArguments());

			// Assert
			callArguments.Should().BeNull(
				because: "an enum without None gives no safe choice, so the call must not guess another member");
		}

		[Test]
		[Description("Rejects a named enum argument for a parameter that is not an enum")]
		public void MatchArguments_Should_Reject_A_Named_Enum_For_A_Parameter_That_Is_Not_An_Enum(){
			// Arrange
			MethodInfo importSchema = ImportSchemaOf(typeof(IImporterWithStringThirdParameter));

			// Act
			object[] callArguments = CreatioApiGateway.MatchArguments(importSchema, GatewayImportArguments());

			// Assert
			callArguments.Should().BeNull(
				because: "the placeholder names an enum member and means nothing for a parameter of another type");
		}

		[Test]
		[Description("Does not bind an enum parameter from a number")]
		public void MatchArguments_Should_Not_Bind_An_Enum_Parameter_From_A_Number(){
			// Arrange
			MethodInfo importSchema = ImportSchemaOf(typeof(IThreeParameterImporter));
			object[] arguments = {SchemaData, PackageUId, 0};

			// Act
			object[] callArguments = CreatioApiGateway.MatchArguments(importSchema, arguments);

			// Assert
			callArguments.Should().BeNull(
				because: "enum members are bound by name, so a renumbered enum cannot change the import behaviour");
		}

		[Test]
		[Description("Rejects a method that declares more parameters than the caller supplies")]
		public void MatchArguments_Should_Reject_A_Method_With_More_Parameters_Than_Arguments(){
			// Arrange
			MethodInfo importSchema = ImportSchemaOf(typeof(IThreeParameterImporter));
			object[] arguments = {SchemaData, PackageUId};

			// Act
			object[] callArguments = CreatioApiGateway.MatchArguments(importSchema, arguments);

			// Assert
			callArguments.Should().BeNull(because: "there is no value to pass for the undeclared third argument");
		}

		[Test]
		[Description("The arguments the gateway offers ImportSchema end with the None action")]
		public void BuildImportInterfaceArguments_Should_Offer_The_Payload_The_Package_And_None(){
			// Arrange
			Guid packageUId = PackageUId;

			// Act
			object[] arguments = CreatioApiGateway.BuildImportInterfaceArguments(SchemaData, packageUId);

			// Assert
			arguments.Should().Equal(
				new object[] {SchemaData, packageUId, CreatioApiGateway.ExistingSchemaPackageActionNone},
				because: "without the action argument no three-parameter importer can bind, and every import on "
					+ "Creatio 10.3.1146+ fails");
		}

		[Test]
		[Description("Selects the three-parameter importer when a core declares both shapes")]
		public void SelectCandidate_Should_Prefer_The_Overload_With_The_Most_Parameters(){
			// Arrange
			MethodInfo[] candidates = typeof(IImporterWithBothShapes).GetMethods()
				.OrderBy(method => method.GetParameters().Length)
				.ToArray();

			// Act
			MethodInfo selected = CreatioApiGateway.SelectCandidate(candidates, GatewayImportArguments(),
				out object[] callArguments);

			// Assert
			selected.GetParameters().Should().HaveCount(3,
				because: "the shorter overload also matches by dropping the action, and calling it would discard None");
			callArguments[2].Should().Be(FakeExistingSchemaPackageAction.None,
				because: "the selected overload must receive the core's own None member");
		}

		[Test]
		[Description("Returns no overload when none of them binds the arguments")]
		public void SelectCandidate_Should_Return_Null_When_No_Overload_Binds(){
			// Arrange
			MethodInfo[] candidates = typeof(IImporterWithoutNone).GetMethods();

			// Act
			MethodInfo selected = CreatioApiGateway.SelectCandidate(candidates, GatewayImportArguments(),
				out object[] callArguments);

			// Assert
			selected.Should().BeNull(because: "the only overload's enum has no None member");
			callArguments.Should().BeNull(because: "there is no overload to call with them");
		}

		[Test]
		[Description("The gateway asks the platform for the None action")]
		public void ExistingSchemaPackageActionNone_Should_Name_The_None_Member(){
			// Arrange
			CreatioApiGateway.NamedEnumArgument action = CreatioApiGateway.ExistingSchemaPackageActionNone;

			// Act
			string memberName = action.MemberName;

			// Assert
			memberName.Should().Be("None",
				because: "the other members move the schema or update it in another package, which clio never requests");
		}

		#endregion

	}
}
