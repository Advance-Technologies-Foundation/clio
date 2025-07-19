# C# Coding Style Guidelines (Creatio)

This document outlines the accepted coding style for C# development at Creatio, intended for both human and AI agents to maintain consistency and readability.

---

## 1. **Indentation**
- Use **tabs** for indentation
- Do not mix tabs and spaces
- Configure your IDE to show tabs as 4 spaces

```csharp
public class Example{
	public void Method(){
		if (condition){
			DoSomething();
		}
	}
}
```

---

## 2. **File and Member Organization**
- All class members must be inside named `#region` blocks.
- Regions should appear in this exact order:
  1. Enums
  2. Delegates
  3. Interfaces
  4. Structs
  5. Classes
  6. Constants
  7. Fields
  8. Constructors
  9. Properties
  10. Events
  11. Methods
- Regions grouped by access modifier (private → protected → internal → protected internal → public)
- Inside regions: static → abstract → override → new → regular (including virtual)
- Backing fields must be placed immediately before the related property.

---

## 3. **Naming Conventions**
| Element Type                   | Access Modifier           | Naming Conventionv |
|--------------------------------|---------------------------|--------------------|
| Local Variable                 | Any                       | `lowerCamelCase`   |
| Field                          | Private                   | `_lowerCamelCase`  |
| Field                          | Protected/Internal/Public | `UpperCamelCase`   |
| Property/Method/Type/Namespace | Any                       | `UpperCamelCase`   |

- **Method names** should start with verbs: `Get`, `Set`, `Apply`, `Update`, etc.
  - ❌ `IsInitialized()` → ✅ `GetIsInitialized()`
- **Boolean names** must use: `Is`, `Has`, `Can`, `Was`, `Use`, `Need`, etc.

---

## 4. **Approved Abbreviations**
| Full Term         | Abbreviation | Example               |
|-------------------|--------------|-----------------------|
| Application       | `app`        | `appSettings`         |
| DataReader        | `dr`         | `drCustomers`         |
| StringBuilder     | `sb`         | `sbMessage`           |
| Exception         | `ex`         | `catch(Exception ex)` |
| System Under Test | `sut`        | For unit tests only   |

---

## 5. **Use of `var`**
Use `var` only when the type is clear from the context:
- ✅ `var items = new List<int>();`
- ❌ `var result = DoSomethingUnclear(); // Specify type explicitly`

Use explicit types when:
- Context doesn’t clarify the type
- Variable is reused later in unclear ways

---

## 6. **Curly Braces `{}` Formatting**
- Open brace on **same line** as statement
- Close brace on **new line**, aligned with start
```csharp
if (condition) {
    DoSomething();
} else {
    HandleElse();
}
```
- **Always use braces**, even for one-liners

---

## 7. **Special Cases**
- **Switch/case** blocks do **not** use additional braces
- **Collection initializers** should fit on one line. If not possible, rewrite or avoid initializer.
```csharp
var items = new List<int> { 1, 2, 3 }; // OK
```
- **Auto-properties**:
```csharp
public int Value { get; set; } // OK
```

---

## 8. **Namespaces and Types**
```csharp
namespace MyNamespace
{
    public class MyClass
    {
        // Class members
    }
}
```
- Opening braces on **new line**

---

## 9. **Query Formatting**
Match formatting style with SQL syntax:
```csharp
var select = new Select(userConnection)
    .Top(1)
    .Column("Country", "Name")
    .From("City")
    .InnerJoin("Country").On("Country", "Id").IsEqual("City", "CountryId") as Select;
```
- Methods like `From`, `Where`, `Join`, `Column` → start from a new line
- Indent sub-methods one tab deeper
- `Execute()` should be called outside the query definition

---

## 10. **Localization**
- All user-facing text **must** be localized
- Use `LocalizableString` for messages
- Exceptions: Unit tests, code introspection (e.g. class names)
```csharp
_log.Error(string.Format(new LocalizableString("Namespace", "Key"), arg1, arg2));
```

---

## 11. **Comments and TODOs**
- Comments must **only** clarify intent or mark pending changes
- Format: `// TODO #[ticket] [message]`
```csharp
// TODO #IP-132 Replace with optimized version
```
- XML comments follow standard XML doc formatting (no TODO rule)

## 12. **Unit Tests**
- Use `NUnit` for unit tests
- Every test should have [Description] attribute, clearly explaining the test purpose
- Assertions should be clear and descriptive, use because to explain the seertion
```csharp
expectedResult.Should().Be(actualResult, "because the method should return the expected result");
``

End of file.
