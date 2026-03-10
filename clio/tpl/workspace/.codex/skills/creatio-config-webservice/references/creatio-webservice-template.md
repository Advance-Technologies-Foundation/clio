# Creatio Configuration Web Service Template

## Minimal Structure

Target location:
- `packages/<PACKAGE_NAME>/src/cs/EntryPoints/WebService`
- Or `packages/<PACKAGE_NAME>/Files/src/cs/EntryPoints/WebService` when repository layout includes `Files/`.

1. Define request/response DTOs.
2. Define service method contract.
3. Implement method with:
- Input validation.
- Domain/service execution.
- `ErrorOr`-based success or error mapping.
4. Add unit tests for success + validation + error.
5. Add integration-style test for endpoint contract.

## Response And Serialization Behavior

Default DTO invocation:
- `public <ResponseDto> <MethodName>(<RequestDto> request)`
- Creatio uses built-in serializer/deserializer to map request (JSON/XML) to `<RequestDto>` and response DTO back to payload.
- This flow is typically treated as `200 OK` unless you explicitly handle response status.

Use custom status/body control when endpoint contract requires strict HTTP semantics:
1. Set status explicitly on `HttpContextAccessor.GetInstance().Response.StatusCode`.
2. Use `void` method when needed.
3. Serialize response manually and write to `Response.OutputStream`.

Example (DemoService-style, embedded so it works even if DemoService does not exist):

```csharp
[OperationContract]
[WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare,
  ResponseFormat = WebMessageFormat.Json)]
public void GetSample() {
#if NETSTANDARD2_0
  HttpResponse response = HttpContextAccessor.GetInstance().Response;
  response.StatusCode = 200;
#endif
  var responseContent = new { text = "Hello World" };
  var json = System.Text.Json.JsonSerializer.Serialize(responseContent);
  var bytes = Encoding.UTF8.GetBytes(json);
  response.OutputStream.Write(bytes, 0, bytes.Length);
  response.OutputStream.Flush();
}
```

## Pseudocode Pattern

```csharp
public sealed class CreateSomethingRequest {
  public string Name { get; set; }
}

public sealed class CreateSomethingResponse {
  public bool Success { get; set; }
  public string Message { get; set; }
  public object Data { get; set; }
  public object[] Errors { get; set; }
}

public class MyConfigService {
  public CreateSomethingResponse CreateSomething(CreateSomethingRequest request) {
    // Validate input and return typed error value; do not throw for expected failures.
    ErrorOr<object> result = Validate(request)
      .Then(_ => ExecuteCore(request));

    return result.Match(
      value => new CreateSomethingResponse {
        Success = true,
        Message = "ok",
        Data = value,
        Errors = Array.Empty<object>()
      },
      errors => new CreateSomethingResponse {
        Success = false,
        Message = "validation_failed",
        Data = null,
        Errors = errors.Select(e => new { e.Code, e.Description }).Cast<object>().ToArray()
      });
  }
}
```

## Test Cases Checklist

- Valid request returns `Success=true` and expected payload.
- Invalid request returns `Success=false` and validation error shape.
- Internal/domain failure path maps to stable error response contract.
- Endpoint name and payload schema remain backward-compatible unless request explicitly changes contract.

## Endpoint Testing Playbook

Automated entry point tests (no network):
1. Place fixture in `tests/<PACKAGE_NAME>/EntryPoints/WebService/<ServiceName>TestFixture.cs`.
2. Create class inheriting `BaseMarketplaceTestFixture`.
3. Add class attributes:
   - `[MockSettings(RequireMock.All)]`
   - `[TestFixture(Category = "PreCommit")]`
4. Create `HttpContext` substitute and `IHttpContextAccessor` field.
5. In `SetUp`, create `MemoryStream` and map it to `Response.OutputStream`.
6. Build accessor with `CustomSetupHttpContextAccessor(_context, UserConnection)`.
7. Instantiate service and assign `HttpContextAccessor`.
8. Call service method directly.
9. Assert status code and payload/DTO.
10. Cover at least one negative path with error mapping assertion.

Fixture template:

```csharp
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
```

Per-test method checklist:
1. Add `[Test]`.
2. Add `[Description("Explain behavior and expected outcome")]`.
3. Arrange `sut` with `HttpContextAccessor`.
4. Act by invoking the endpoint method.
5. Assert `Response.StatusCode`.
6. Assert response body or returned DTO.
7. Add explicit negative-path test.

Manual runtime endpoint tests:
1. Authenticate in Creatio (browser or API flow) and keep session cookies/headers.
2. Call endpoint:
   - `GET https://<creatio-host>/rest/<ServiceName>/<MethodName>`
   - `POST https://<creatio-host>/rest/<ServiceName>/<MethodName>`
3. For POST, send `Content-Type: application/json` with representative body.
4. Verify status code and JSON contract:
   - Success shape fields.
   - Error shape fields/code/message.
5. Capture one success response and one failure response in task notes.
6. If endpoint uses default DTO invocation, verify DTO fields are mapped correctly from JSON/XML payload.
7. If endpoint uses custom serialization, verify exact JSON body and explicit custom status code.

Example test assertion (DemoService-style, embedded):

```csharp
[Test]
[Description("Returns OK and serialized payload for custom response writing")]
public void GetSample_ReturnsExpectedJson() {
  SampleService sut = new SampleService { HttpContextAccessor = _httpContextAccessor };
  sut.GetSample();

HttpResponse responseInstance = _httpContextAccessor.GetInstance().Response;
responseInstance.StatusCode.Should().Be(200);
using (var reader = new System.IO.StreamReader(responseInstance.OutputStream)) {
  reader.BaseStream.Seek(0, System.IO.SeekOrigin.Begin);
  var result = reader.ReadToEnd();
  result.Should().Be("{\"text\":\"Hello World\"}");
}
}
```