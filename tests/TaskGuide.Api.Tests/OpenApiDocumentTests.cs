using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TaskGuide.Api.Tests;

/// <summary>
/// The OpenAPI document at <c>/openapi/v1.json</c> is what the React SPA generates its
/// <c>Task</c> type from (openapi-typescript). Scoped to the two real Task endpoints (#51):
/// <c>GET /api/tasks</c> and <c>POST /api/tasks</c> must describe their response bodies with a
/// <c>TaskResponse</c> schema, not the bare "200: OK" a raw <see cref="Microsoft.AspNetCore.Http.IResult"/>
/// return type produces.
/// </summary>
/// <remarks>
/// <c>Program.cs</c> reads <c>Storage:DataDir</c> from configuration on the line before
/// <c>builder.Build()</c> — earlier than <c>WithWebHostBuilder().ConfigureAppConfiguration()</c>
/// takes effect for a minimal-hosting entry point (that hook applies at <c>Build()</c>, too late
/// for a value already read). An environment variable is visible from the moment
/// <c>WebApplication.CreateBuilder</c> runs, so that's the override path that actually reaches
/// this line. <c>AssemblyInfo.cs</c> disables test parallelization so no other test in this
/// assembly observes the environment variable while it's set.
/// </remarks>
public sealed class OpenApiDocumentTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";

    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public OpenApiDocumentTests()
    {
        Environment.SetEnvironmentVariable(DataDirEnvVar, _dataDir);
        try
        {
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient(); // forces host startup now, while the env var is still set
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirEnvVar, null);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Directory.Delete(_dataDir, recursive: true);
    }

    private async Task<JsonElement> GetDocumentAsync() =>
        await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

    [Fact]
    public async Task TaskResponse_schema_is_present_with_its_seventeen_members()
    {
        var doc = await GetDocumentAsync();

        var schemas = doc.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("TaskResponse", out var taskResponse));

        var properties = taskResponse.GetProperty("properties");
        Assert.True(properties.TryGetProperty("id", out _));
        Assert.True(properties.TryGetProperty("title", out _));
        Assert.True(properties.TryGetProperty("notes", out _));
        Assert.True(properties.TryGetProperty("duration", out _));
        Assert.True(properties.TryGetProperty("dimensions", out _));
        Assert.True(properties.TryGetProperty("looseTags", out _));
        Assert.True(properties.TryGetProperty("createdAt", out _));
        Assert.True(properties.TryGetProperty("status", out _));
        Assert.True(properties.TryGetProperty("eligible", out _));
        Assert.True(properties.TryGetProperty("deadline", out _));
        Assert.True(properties.TryGetProperty("defer", out _));
        Assert.True(properties.TryGetProperty("postpone", out _));
        Assert.True(properties.TryGetProperty("recurring", out _));
        Assert.True(properties.TryGetProperty("derived", out _));
        Assert.True(properties.TryGetProperty("opportunities", out _));
        Assert.True(properties.TryGetProperty("patternWeekCount", out _));
        Assert.True(properties.TryGetProperty("zeroKind", out _));

        Assert.Equal(
            ["createdAt", "deadline", "defer", "derived", "dimensions", "duration", "eligible", "id", "looseTags", "notes", "opportunities", "patternWeekCount", "postpone", "recurring", "status", "title", "zeroKind"],
            properties.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public async Task TaskResponse_and_CaptureTaskResponse_duration_are_documented_as_nullable_strings()
    {
        var doc = await GetDocumentAsync();

        var schemas = doc.GetProperty("components").GetProperty("schemas");
        AssertNullableString(schemas.GetProperty("TaskResponse"));
        AssertNullableString(schemas.GetProperty("CaptureTaskResponse"));
    }

    private static void AssertNullableString(JsonElement schema)
    {
        var duration = schema.GetProperty("properties").GetProperty("duration");
        // Nullable reference types surface either as {"type": ["string","null"]} or a bare
        // {"type": "string"} with a sibling "nullable": true, depending on generator version.
        var typeElement = duration.GetProperty("type");
        var typeValues = typeElement.ValueKind == JsonValueKind.Array
            ? typeElement.EnumerateArray().Select(e => e.GetString()).ToArray()
            : [typeElement.GetString()];
        Assert.Contains("string", typeValues);
        Assert.True(typeValues.Contains("null") ||
                    (duration.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean()));
    }

    [Fact]
    public async Task GET_api_tasks_200_response_is_an_array_of_TaskResponse()
    {
        var doc = await GetDocumentAsync();

        var response200 = doc.GetProperty("paths").GetProperty("/api/tasks").GetProperty("get")
            .GetProperty("responses").GetProperty("200");

        var schema = response200.GetProperty("content").GetProperty("application/json").GetProperty("schema");

        Assert.Equal("array", schema.GetProperty("type").GetString());
        var itemsRef = schema.GetProperty("items").GetProperty("$ref").GetString();
        Assert.Equal("#/components/schemas/TaskResponse", itemsRef);
    }

    [Fact]
    public async Task POST_api_tasks_declares_201_400_and_503_with_a_TaskResponse_body_on_201()
    {
        var doc = await GetDocumentAsync();

        var responses = doc.GetProperty("paths").GetProperty("/api/tasks").GetProperty("post")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("201", out var response201));
        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("503", out _));

        var createdRef = response201.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();
        Assert.Equal("#/components/schemas/TaskResponse", createdRef);
    }

    [Fact]
    public async Task PUT_api_tasks_id_duration_declares_request_and_204_400_409_outcomes()
    {
        var doc = await GetDocumentAsync();
        var operation = doc.GetProperty("paths").GetProperty("/api/tasks/{id}/duration").GetProperty("put");
        var responses = operation.GetProperty("responses");

        Assert.True(responses.TryGetProperty("204", out _));
        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("409", out _));
        Assert.False(responses.TryGetProperty("200", out _));

        var requestSchema = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
        Assert.Equal("#/components/schemas/SetTaskDurationRequest", requestSchema);
    }

    [Fact]
    public async Task Day_template_lifecycle_responses_are_typed_for_SPA_generation()
    {
        var doc = await GetDocumentAsync();
        var paths = doc.GetProperty("paths");

        var promotion = paths.GetProperty("/api/overrides/{date}/promote").GetProperty("post")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
        var stamp = paths.GetProperty("/api/overrides/{date}/stamp").GetProperty("put")
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();

        Assert.Equal("#/components/schemas/DayTemplateResponse", promotion);
        Assert.Equal("#/components/schemas/DateOverrideResponse", stamp);
    }

    // #134 typed these four handlers' return types as Results<NoContent, BadRequest<object>,
    // Conflict<object>> (or the matching-on equivalent) instead of the bare `IResult` that used to
    // document a bodiless 200. A revert back to `async Task<IResult>` keeps the rest of the suite
    // green while silently degrading the document back to that bodiless 200 — so this asserts both
    // that 204/400/409 are declared AND that 200 is gone, since a test that only checked the new
    // statuses would also pass against the old document.
    [Theory]
    [InlineData("/api/right-now/matching-on", "put")]
    [InlineData("/api/tasks/{id}", "patch")]
    [InlineData("/api/tasks/{id}/completions", "post")]
    [InlineData("/api/tasks/{id}/postpone", "put")]
    public async Task Refusal_shaped_handler_declares_204_400_and_409_and_not_a_bare_200(string path, string method)
    {
        var doc = await GetDocumentAsync();

        var responses = doc.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");

        Assert.True(responses.TryGetProperty("204", out _));
        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("409", out _));
        Assert.False(responses.TryGetProperty("200", out _));
    }

    // #170: these two reads return Results<Ok<T>, BadRequest<object>, NotFound<object>>. A revert
    // to a bare `IResult` would silently degrade the document's 200 to a bodiless one (and drop
    // 400/404 along with it, same as the refusal handlers above) — so the $ref assertion on 200 is
    // what pins the body to a real schema rather than just checking the status codes exist.
    [Theory]
    [InlineData("/api/tasks/{id}", "#/components/schemas/TaskResponse")]
    [InlineData("/api/day-templates/{id}", "#/components/schemas/DayTemplateResponse")]
    public async Task Read_handler_declares_200_400_and_404_with_a_typed_200_body(string path, string schemaRef)
    {
        var doc = await GetDocumentAsync();

        var responses = doc.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("responses");

        Assert.True(responses.TryGetProperty("200", out var okResponse));
        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("404", out _));

        var okRef = okResponse.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();
        Assert.Equal(schemaRef, okRef);
    }
}
