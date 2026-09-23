using System.Linq;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture, Category("Unit"), Property("Module", "Command")]
public class PageParentNameValidationTests {
    private static string Body(string diff) => "define('Test',[],function(){return {viewConfigDiff:/**SCHEMA_VIEW_CONFIG_DIFF*/" + diff + "/**SCHEMA_VIEW_CONFIG_DIFF*/};});";
    [Test, Description("A typo fails when inherited context is supplied and names the nearest parent.")]
    public void Validate_ShouldRejectTypo_WhenContainersAreKnown() {
        // Arrange
        string body=Body("[{operation:'insert',name:'Child',parentName:'MainContaner',values:{}}]");
        // Act
        var result=PageParentNameValidation.Validate(body, ["MainContainer"]);
        // Assert
        result.IsValid.Should().BeFalse(because:"the parent does not exist");
        result.Errors.Single().Should().Contain("Child").And.Contain("MainContaner").And.Contain("MainContainer", because:"the diagnostic identifies the fix");
    }
    [Test, Description("A body alone cannot distinguish a typo from an inherited template parent.")]
    public void Validate_ShouldWarn_WhenInheritedContextIsUnknown() {
        // Arrange
        string body=Body("[{operation:'insert',name:'Child',parentName:'Template',values:{}}]");
        // Act
        var result=PageParentNameValidation.Validate(body);
        // Assert
        result.IsValid.Should().BeTrue(because:"the unseen template may define the parent");
        result.Warnings.Single().Should().Contain("Template", because:"missing context must not silently pass");
    }
    [TestCase("[{operation:'insert',name:'Child',parentName:'Parent',values:{}},{operation:'insert',name:'Parent',values:{}}]")]
    [TestCase("[{operation:'insert',name:'Parent',values:{items:[{name:'Nested'}]}},{operation:'insert',name:'Child',parentName:'Nested',values:{}}]")]
    [TestCase("[{operation:'insert',name:'Root',values:{}}]")]
    [Description("New, nested, and root elements remain valid without inherited containers.")]
    public void Validate_ShouldAcceptLocalParents_WhenDeclaredInDiff(string diff) {
        // Arrange
        string body=Body(diff);
        // Act
        var result=PageParentNameValidation.Validate(body, []);
        // Assert
        result.IsValid.Should().BeTrue(because:"the parent is local or the insertion targets the root");
    }
    [Test, Description("Removed inherited parent names cannot authorize child inserts.")]
    public void Validate_ShouldRejectParent_WhenRemovedByDiff() {
        // Arrange
        string body=Body("[{operation:'remove',name:'Parent'},{operation:'insert',name:'Child',parentName:'Parent',values:{}}]");
        // Act
        var result=PageParentNameValidation.Validate(body, ["Parent"]);
        // Assert
        result.IsValid.Should().BeFalse(because:"the removal happens before insertion");
    }
    [TestCase("[{operation:'move',name:'Absent',parentName:'Missing'}]", true)]
    [TestCase("[{operation:'move',name:'Parent',parentName:'Missing'}]", false)]
    [TestCase("[{operation:'insert',name:'Actual',values:{name:'Phantom'}},{operation:'insert',name:'Child',parentName:'Phantom',values:{}}]", false)]
    [TestCase("[{operation:'insert',name:'Actual',values:{name:'Phantom',items:[{name:'Nested'}]}},{operation:'insert',name:'Child',parentName:'Nested',values:{}}]", true)]
    [Description("Static validation ignores absent moves and uses effective insert names while retaining nested names.")]
    public void Validate_ShouldUseEffectiveElementNames(string diff, bool expected) {
        // Arrange
        string body = Body(diff);
        // Act
        var result = PageParentNameValidation.Validate(body, ["Parent"]);
        // Assert
        result.IsValid.Should().Be(expected, because: "only names used by the interpreter can establish source and destination elements");
    }
}
