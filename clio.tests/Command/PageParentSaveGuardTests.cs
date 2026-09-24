using System;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture, Category("Unit"), Property("Module", "Command")]
public class PageParentSaveGuardTests {
    private const string Uid="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private ServiceProvider _provider;
    private IApplicationClient _client;
    private IPageDesignerHierarchyClient _hierarchy;
    private PageUpdateCommand _command;
    internal static string Body(string diff) => "define('UsrProof',/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {return {viewConfigDiff:/**SCHEMA_VIEW_CONFIG_DIFF*/"+diff+"/**SCHEMA_VIEW_CONFIG_DIFF*/,viewModelConfigDiff:/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,modelConfigDiff:/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,handlers:/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,converters:/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,validators:/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/};});";
    [SetUp]
    public void SetUp() {
        _client=Substitute.For<IApplicationClient>();
        _hierarchy=Substitute.For<IPageDesignerHierarchyClient>();
        var urls=Substitute.For<IServiceUrlBuilder>();
        urls.Build(Arg.Any<string>()).Returns(x=>x.Arg<string>());
        _client.ExecutePostRequest(Arg.Is<string>(x=>x.EndsWith("SelectQuery")),Arg.Any<string>(),Arg.Any<int>(),Arg.Any<int>(),Arg.Any<int>()).Returns("{\"success\":true,\"rows\":[{\"UId\":\""+Uid+"\"}]}");
        _client.ExecutePostRequest(Arg.Is<string>(x=>x.EndsWith("GetSchema")),Arg.Any<string>(),Arg.Any<int>(),Arg.Any<int>(),Arg.Any<int>()).Returns(JsonConvert.SerializeObject(new {success=true,schema=new {name="UsrProof",body=Body("[]")}}));
        _client.ExecutePostRequest(Arg.Is<string>(x=>x.EndsWith("SaveSchema")),Arg.Any<string>(),Arg.Any<int>(),Arg.Any<int>(),Arg.Any<int>()).Returns("{\"success\":true}");
        _hierarchy.GetDesignPackageUId(Arg.Any<string>()).Returns("pkg");
        _hierarchy.GetParentSchemas(Arg.Any<string>(),"pkg").Returns([
            new PageDesignerHierarchySchema { UId=Uid,Name="UsrProof",PackageUId="pkg",SchemaType=9 },
            new PageDesignerHierarchySchema { UId="base",Name="Base",Body=Body("[{operation:'insert',name:'MainContainer',values:{items:[]}}]") }
        ]);
        var services=new ServiceCollection();
        services.AddSingleton(_client).AddSingleton(urls).AddSingleton(_hierarchy)
            .AddSingleton(Substitute.For<ILogger>()).AddSingleton(Substitute.For<IPageBaselineGuard>())
            .AddSingleton(Substitute.For<IPersistedResourceKeyReader>()).AddTransient<IJsonDiffApplier,JsonDiffApplier>()
            .AddTransient<Func<IJsonDiffApplier>>(sp=>()=>sp.GetRequiredService<IJsonDiffApplier>()).AddTransient<PageUpdateCommand>();
        _provider=services.BuildServiceProvider(); _command=_provider.GetRequiredService<PageUpdateCommand>();
    }
    [TearDown] public void TearDown()=>_provider.Dispose();
    [TestCase("replace",true)] [TestCase("replace",false)] [TestCase("append",true)] [TestCase("append",false)]
    [Description("No write path may bypass unresolved parent rejection using validate=false.")]
    public void TryUpdatePage_ShouldRejectUnresolvedParent_WhenValidationIsDisabled(string mode,bool dryRun) {
        // Arrange
        var options=new PageUpdateOptions {SchemaName="UsrProof",Validate=false,Mode=mode,DryRun=dryRun,Body=Body("[{operation:'insert',name:'Child',parentName:'MainContaner',propertyName:'items',values:{}}]")};
        // Act
        bool result=_command.TryUpdatePage(options,out var response);
        // Assert
        result.Should().BeFalse(because:"unresolved parents are a structural failure");
        response.Error.Should().Contain("MainContaner").And.Contain("MainContainer",because:"the response identifies the typo and nearest actual parent");
        _client.DidNotReceive().ExecutePostRequest(Arg.Is<string>(x=>x.EndsWith("SaveSchema")),Arg.Any<string>(),Arg.Any<int>(),Arg.Any<int>(),Arg.Any<int>());
    }
    [TestCase(false)] [TestCase(true)]
    [Description("Valid template parents are resolved for both named and explicit schema targets.")]
    public void TryUpdatePage_ShouldAcceptInheritedParent_WhenTargetIsResolved(bool explicitTarget) {
        // Arrange
        var options=new PageUpdateOptions {SchemaName="UsrProof",TargetSchemaUId=explicitTarget?"{"+Uid+"}":null,Validate=false,Body=Body("[{operation:'insert',name:'Child',parentName:'MainContainer',propertyName:'items',values:{}}]")};
        // Act
        bool result=_command.TryUpdatePage(options,out var response);
        // Assert
        result.Should().BeTrue(because:"the parent is supplied by the actual inherited hierarchy: "+response.Error);
        _hierarchy.Received(1).GetParentSchemas(Arg.Any<string>(), "pkg");
        _client.Received().ExecutePostRequest(Arg.Is<string>(x=>x.EndsWith("SaveSchema")),Arg.Any<string>(),Arg.Any<int>(),Arg.Any<int>(),Arg.Any<int>());
    }
    [TestCase("[{operation:'remove',name:'MainContainer'},{operation:'move',name:'MainContainer',parentName:'Missing',propertyName:'items'}]")]
    [TestCase("[{operation:'move',name:'Absent',parentName:'Missing',propertyName:'items'}]")]
    [Description("Moves ignored by Creatio cannot introduce an orphan and must not block saving.")]
    public void TryUpdatePage_ShouldAcceptIgnoredMoves(string diff) {
        // Arrange
        var options = new PageUpdateOptions { SchemaName = "UsrProof", Validate = false, Body = Body(diff) };
        // Act
        bool result = _command.TryUpdatePage(options, out var response);
        // Assert
        result.Should().BeTrue(because: "the platform ignores the move: " + response.Error);
    }

    [Test]
    [Description("Removing an element property does not suppress a subsequent move with a bad parent.")]
    public void TryUpdatePage_ShouldRejectMovedParent_WhenOnlyPropertiesWereRemoved() {
        // Arrange
        var options = new PageUpdateOptions { SchemaName = "UsrProof", Validate = false,
            Body = Body("[{operation:'remove',name:'MainContainer',properties:['caption']},{operation:'move',name:'MainContainer',parentName:'Missing',propertyName:'items'}]") };
        // Act
        bool result = _command.TryUpdatePage(options, out var response);
        // Assert
        result.Should().BeFalse(because: "property removal does not suppress the move");
        response.Error.Should().Contain("Missing", because: "the move's parent is unresolved");
    }

    [TestCase("move", true)]
    [TestCase("remove", false)]
    [Description("The structural guard follows the interpreter's inherited alias exclusions.")]
    public void TryUpdatePage_ShouldHonorInheritedAliasExclusions(string excluded, bool expectedSuccess) {
        // Arrange
        _hierarchy.GetParentSchemas(Uid, "pkg").Returns([
            new PageDesignerHierarchySchema { UId = Uid, Name = "UsrProof", PackageUId = "pkg", SchemaType = 9 },
            new PageDesignerHierarchySchema { UId = "base", Name = "Base", Body = Body(
                "[{operation:'insert',name:'MainContainer',values:{items:[]}},{operation:'insert',name:'Current',parentName:'MainContainer',propertyName:'items',alias:{name:'Old',excludeOperations:['" + excluded + "']},values:{items:[]}}]") }
        ]);
        string remove = excluded == "remove" ? "{operation:'remove',name:'Old'}," : "";
        var options = new PageUpdateOptions { SchemaName = "UsrProof", Validate = false,
            Body = Body("[" + remove + "{operation:'move',name:'Old',parentName:'Missing',propertyName:'items'}]") };
        // Act
        bool result = _command.TryUpdatePage(options, out var response);
        // Assert
        result.Should().Be(expectedSuccess, because: "only effective operations can create orphans: " + response.Error);
    }

    [TestCase("parentName", "MainContainer", true)]
    [TestCase("parentName", "Missing", false)]
    [TestCase("nameTo", "MainContainer", true)]
    [TestCase("nameTo", "Missing", false)]
    [TestCase("nameTo", "Child", false)]
    [Description("Set relocations must resolve their destination before a page can be saved.")]
    public void TryUpdatePage_ShouldValidateSetDestination(string property, string destination, bool expected) {
        // Arrange
        _hierarchy.GetParentSchemas(Uid, "pkg").Returns([
            new PageDesignerHierarchySchema { UId = Uid, Name = "UsrProof", PackageUId = "pkg", SchemaType = 9 },
            new PageDesignerHierarchySchema { UId = "base", Name = "Base", Body = Body(
                "[{operation:'insert',name:'MainContainer',values:{items:[]}},{operation:'insert',name:'Child',parentName:'MainContainer',propertyName:'items',values:{}}]") }
        ]);
        var options = new PageUpdateOptions { SchemaName = "UsrProof", Validate = false,
            Body = Body("[{operation:'set',name:'Child'," + property + ":'" + destination + "',values:{}}]") };
        // Act
        bool result = _command.TryUpdatePage(options, out var response);
        // Assert
        result.Should().Be(expected, because: "set uses the same destination guard as insert and move: " + response.Error);
        if (!expected) {
            response.Error.Should().Contain(destination, because: "the diagnostic must identify the unresolved destination");
            _client.DidNotReceive().ExecutePostRequest(Arg.Is<string>(x => x.EndsWith("SaveSchema")), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
        }
    }
}
