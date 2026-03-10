---
name: creatio-config-webservice
description: Create, update, and validate custom Creatio Configuration Web Services and their tests. Use when users need to expose custom backend endpoints in Creatio, wire service contracts/implementations, return structured success/error results, or verify endpoints with integration-style and unit tests.
---

# Creatio Config Webservice

## Overview

Create deterministic steps for implementing a Creatio custom web service in a package and testing it before delivery. Follow this skill when requests involve "create endpoint", "add web service method", "test service", "mock user/session", or "verify HTTP contract" in Creatio.

## Required Location And Namespaces

Place service classes in:
- `packages/<PACKAGE_NAME>/src/cs/EntryPoints/WebServices`

If the repository stores C# files under `Files/src`, map the same logical location as:
- `packages/<PACKAGE_NAME>/Files/src/cs/EntryPoints/WebServices`

Use namespace pattern:
- `<PackageNamespace>.EntryPoints.WebServices`

Place tests in:
- `tests/<PACKAGE_NAME>/EntryPoints/WebServices`

## Workflow Decision Tree

- If the user needs a new endpoint, implement the full flow: contract, service implementation, registration/build, and tests.
- If the endpoint exists but behavior changes, update only contract/implementation parts impacted, then update tests first.
- If the request is only about testing, locate existing service and focus on test harness and assertions.
- If deployment details are unclear (package, auth model, URL), inspect project/package structure first, then continue.

## Implement Service

1. Identify target package and source location.
2. Create or update DTOs and response contracts first.
3. Implement service method with explicit input validation.
4. Return errors as values, not exceptions, following repository rule: use `ErrorOr`.
5. Keep endpoint behavior deterministic: validate, execute core logic, map result to response.
6. Keep transport concerns (request/response models) separate from business logic when possible.

Use `references/creatio-webservice-template.md` for a concrete structure and checklist.

### Response Status And Serialization Rules (Creatio-Specific)

When method signature is:
- `public <ResponseDto> <MethodName>(<RequestDto> request)`

Creatio uses default serializer/deserializer for request/response binding (JSON/XML to DTO and DTO to response) and commonly returns `200 OK` for the operation result flow.

If custom HTTP status behavior is required:
1. Set status code explicitly via `HttpContextAccessor.GetInstance().Response.StatusCode` (`NETSTANDARD2_0`) or legacy response context.
2. Prefer `void` method and write payload directly to `Response.OutputStream` when you need full control over status + body format.
3. Use custom serialization (manual JSON writing) for strict response contracts.

Self-contained example (DemoService-style pattern, no repository dependency):

```csharp
[OperationContract]
[WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare,
  ResponseFormat = WebMessageFormat.Json)]
public void GetExample() {
  HttpRequest request = HttpContextAccessor.GetInstance().Request;
#if NETSTANDARD2_0
  HttpResponse response = HttpContextAccessor.GetInstance().Response;
  response.StatusCode = 200;
#endif
  var payload = new { text = "Hello World" };
  string json = System.Text.Json.JsonSerializer.Serialize(payload);
  byte[] bytes = Encoding.UTF8.GetBytes(json);
  response.OutputStream.Write(bytes, 0, bytes.Length);
  response.OutputStream.Flush();
}
```

### Service Class Skeleton

Use this structure for configuration web services:

```csharp
[ServiceContract]
[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
public class <ServiceName> : BaseService, IReadOnlySessionState {

  [OperationContract]
  [WebInvoke(
    Method = "POST",
    RequestFormat = WebMessageFormat.Json,
    BodyStyle = WebMessageBodyStyle.Bare,
    ResponseFormat = WebMessageFormat.Json)]
  public <ResponseDto> <MethodName>(<RequestDto> request) {
    HttpRequest requestInstance = HttpContextAccessor.GetInstance().Request;
#if NETSTANDARD2_0
    HttpResponse response = HttpContextAccessor.GetInstance().Response;
#endif

    // Validate and execute via app layer; do not throw expected errors.
    // Return ErrorOr-driven result mapped to status code and response DTO.
  }
}
```

Endpoint format:
- `https://<creatio-host>/rest/<ServiceName>/<MethodName>`

## Configure and Register

1. Ensure service class visibility and attributes follow Creatio conventions used in the solution.
2. Confirm package dependencies include required assemblies.
3. Build solution/package and verify service compiles in target framework.
4. Record expected route and method names in tests and task notes.

## Test Strategy

Create or update tests for every production change.

1. Unit test service logic:
- Validate request validation paths.
- Validate success mapping.
- Validate error mapping (`ErrorOr`) without throwing.
2. Entry point test (no network) for endpoint contract:
- Execute service entry point with representative payload.
- Assert HTTP/result envelope fields and payload shape.
- Cover at least one negative case.
3. Manual HTTP endpoint test (runtime):
- Call `POST/GET https://<creatio-host>/rest/<ServiceName>/<MethodName>` with authenticated Creatio session.
- Assert status code, JSON schema, and error payload structure.
- Validate at least one success and one failure request.
4. Regression test changed behavior:
- Add focused test for new branch/condition.
- Keep existing behavior covered.

### Endpoint Test Fixture Pattern

Test fixture location and naming:
- Path: `tests/<PACKAGE_NAME>/EntryPoints/WebService`
- File name: `<ServiceName>TestFixture.cs`
- Namespace: `<PackageNamespace>.Tests.EntryPoints.WebService`

Create test class in this order:
1. Inherit from `BaseMarketplaceTestFixture`.
2. Add `[MockSettings(RequireMock.All)]`.
3. Add `[TestFixture(Category = "PreCommit")]`.
4. Create `IHttpContextAccessor` with mocked `HttpContext` in `SetUp`.

Self-contained fixture template (DemoServiceTestFixture-style, no repository dependency):

```csharp
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Configuration.Tests;
using Terrasoft.Web.Http.Abstractions;

namespace <PackageNamespace>.Tests.EntryPoints.WebService {

  [MockSettings(RequireMock.All)]
  [TestFixture(Category = "PreCommit")]
  public class <ServiceName>TestFixture : BaseMarketplaceTestFixture {
    private readonly HttpContext _context = Substitute.For<HttpContext>();
    private IHttpContextAccessor _httpContextAccessor;
    private System.IO.MemoryStream _outputStream;

    protected override void SetUp() {
      base.SetUp();
      _outputStream = new System.IO.MemoryStream();
      _context.Response.OutputStream.Returns(_outputStream);
      _httpContextAccessor = CustomSetupHttpContextAccessor(_context, UserConnection);
    }

    protected override void TearDown() {
      base.TearDown();
      _outputStream.Dispose();
    }
  }
}
```

Test method requirements:
1. Add `[Test]` and `[Description("...")]` for every test.
2. Arrange service under test and inject `HttpContextAccessor`.
3. Act by calling service method directly (no network).
4. Assert status code.
5. Assert payload or DTO return.
6. Add at least one negative-path assertion.

Required assertions per endpoint method:
1. Assert `Response.StatusCode`.
2. Assert response body content (for payload endpoints).
3. Assert error-to-status mapping for at least one error path.

For JSON body assertions:
1. Read `response.OutputStream`.
2. `Seek(0, Begin)` before read.
3. Compare JSON string or deserialize and assert fields.

Self-contained test example (DemoService-style assertion):

```csharp
[Test]
[Description("Returns OK and serialized payload for custom response writer")]
public void GetExample_ReturnsExpectedJson() {
  ExampleService sut = new ExampleService {
    HttpContextAccessor = _httpContextAccessor
  };

  sut.GetExample();

  HttpResponse response = _httpContextAccessor.GetInstance().Response;
  response.StatusCode.Should().Be(200);
  using (var reader = new System.IO.StreamReader(response.OutputStream)) {
    reader.BaseStream.Seek(0, System.IO.SeekOrigin.Begin);
    string result = reader.ReadToEnd();
    result.Should().Be("{\"text\":\"Hello World\"}");
  }
}
```

For methods that return DTO directly:
1. Assert returned object values.
2. Document that default Creatio serialization is used for DTO mapping.
3. If custom status is required, prefer entry-point pattern with explicit status and manual response body writing.

## Verification Checklist

1. Build succeeds with required Creatio environment variables set.
2. Tests added/updated for all changed production files.
3. Endpoint route, method name, and response schema documented in code/tests.
4. Error flow returns typed value and does not throw.
5. Endpoint test coverage includes status code + payload + negative case.
6. Manual HTTP test steps provided with concrete endpoint URL pattern.

## Output Requirements

When executing this skill for a user request, always provide:
- Files changed with short reason per file.
- Service contract summary (input/output + error format).
- Tests added/updated and coverage intent.
- Commands used to build and run tests (or exact blocker if not runnable).
