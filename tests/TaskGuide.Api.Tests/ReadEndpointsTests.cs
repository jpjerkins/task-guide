using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;
using TaskGuide.Domain.Tags;
using Xunit;

namespace TaskGuide.Api.Tests;

public sealed class ReadEndpointsTests : IDisposable
{
    private const string DataDirEnvVar = "Storage__DataDir";
    private readonly string _dataDir = Directory.CreateTempSubdirectory("taskguide-read-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ReadEndpointsTests()
    {
        Environment.SetEnvironmentVariable(DataDirEnvVar, _dataDir);
        try
        {
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient();
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

    [Fact]
    public async Task GET_api_dimensions_returns_the_declared_Dimensions()
    {
        var response = await _client.GetAsync("/api/dimensions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dimensions = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(dimensions.EnumerateArray(), dimension => dimension.GetProperty("id").GetString() == "location");
    }

    [Fact]
    public async Task GET_api_dimensions_names_each_Dimensions_window_value_source()
    {
        var response = await _client.GetAsync("/api/dimensions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dimensions = await response.Content.ReadFromJsonAsync<JsonElement>();
        string SourceOf(string id) => dimensions.EnumerateArray()
            .Single(dimension => dimension.GetProperty("id").GetString() == id)
            .GetProperty("source").GetString()!;

        Assert.Equal("derived", SourceOf("duration"));
        Assert.Equal("fetched", SourceOf("weather"));
        Assert.Equal("authored", SourceOf("location"));
    }

    [Fact]
    public async Task GET_api_dimensions_claiming_names_the_Dimension_that_claims_a_Tag()
    {
        var response = await _client.GetAsync("/api/dimensions/claiming?tag=garage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var claim = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("location", claim.GetProperty("dimensionId").GetString());
    }

    [Fact]
    public async Task GET_api_dimensions_loose_tags_returns_the_inert_Tag_staging_area_and_count()
    {
        var task = new TaskGuide.Domain.Tasks.TaskItem(
            new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"), "Seeded", null,
            new TagSet(new Dictionary<TaskGuide.Domain.Dimensions.DimensionId, IReadOnlyList<TagValue>>(), [new LooseTag("garge")]),
            null, null, null, null, DateTimeOffset.UtcNow);
        await _factory.Services.GetRequiredService<IStore>().MutateAsync<Never>(
            _ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation([new TasksWrite([task])])), CancellationToken.None);

        var response = await _client.GetAsync("/api/dimensions/loose-tags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var looseTags = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, looseTags.GetProperty("count").GetInt32());
        Assert.Equal("garge", looseTags.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task GET_api_days_date_writes_nothing()
    {
        var date = new DateOnly(2030, 1, 7);
        await SeedScheduleAsync(new AvailabilityWindow(
            new WindowId("w_evening"), "Evening", new TimeOnly(18, 0), new TimeOnly(19, 0), TagSet.Empty));

        var response = await _client.GetAsync($"/api/days/{date:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shape = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2030-01-07", shape.GetProperty("date").GetString());
        Assert.Empty(_factory.Services.GetRequiredService<IStore>().Read().Overrides);
    }

    private async Task SeedScheduleAsync(AvailabilityWindow window)
    {
        var template = new DayTemplate(new DayTemplateId("dt_everyday"), "Everyday", [window], []);
        var pattern = new Pattern(new PatternId("p_everyday"), "Everyday", Enumerable.Repeat(template.Id, 7).ToArray());
        var store = _factory.Services.GetRequiredService<IStore>();
        await store.MutateAsync<Never>(
            _ => OneOf<StoreMutation, Never>.FromT0(new StoreMutation([
                new DayTemplatesWrite([template]),
                new PatternsWrite(new PatternBook(pattern.Id, [pattern])),
            ])),
            CancellationToken.None);
    }
}
