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
    public async Task TaskResponse_schema_is_present_with_its_four_members()
    {
        var doc = await GetDocumentAsync();

        var schemas = doc.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("TaskResponse", out var taskResponse));

        var properties = taskResponse.GetProperty("properties");
        Assert.True(properties.TryGetProperty("id", out _));
        Assert.True(properties.TryGetProperty("title", out _));
        Assert.True(properties.TryGetProperty("duration", out _));
        Assert.True(properties.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Duration_is_documented_as_a_nullable_integer()
    {
        var doc = await GetDocumentAsync();

        var duration = doc.GetProperty("components").GetProperty("schemas")
            .GetProperty("TaskResponse").GetProperty("properties").GetProperty("duration");

        // Nullable value types surface either as {"type": ["integer","null"]} or a bare
        // {"type": "integer"} with a sibling "nullable": true, depending on generator version —
        // accept either shape, but the schema must be integer-typed.
        var typeElement = duration.GetProperty("type");
        var typeValues = typeElement.ValueKind == JsonValueKind.Array
            ? typeElement.EnumerateArray().Select(e => e.GetString()).ToArray()
            : [typeElement.GetString()];
        Assert.Contains("integer", typeValues);
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
}
