using TaskGuide.Domain.Common;
using TaskGuide.Infrastructure.Storage;
using Xunit;

namespace TaskGuide.Storage.Tests;

public sealed class BadStoreFileExceptionTests
{
    public static TheoryData<string, Action> CodecReads => new()
    {
        { "tasks.json", () => TaskCodec.Read("[") },
        { "day-templates.json", () => DayTemplateCodec.Read("[") },
        { "patterns.json", () => PatternCodec.Read("{") },
        { "overrides.json", () => OverrideCodec.Read("[") },
        { "events.json", () => EventCodec.Read("[") },
        { "event-exceptions.json", () => EventCodec.ReadExceptions("[") },
        { "completions/t_01ARZ3NDEKTSV4RRFFQ69G5FAV.json", () => CompletionCodec.Read(new TaskId("t_01ARZ3NDEKTSV4RRFFQ69G5FAV"), "[") },
        { "completions/derived.json", () => CompletionCodec.ReadDerived("[") },
        { "fires/2026-08-15.json", () => FireCodec.Read(new DateOnly(2026, 8, 15), "[") },
        { "manifest.json", () => ManifestCodec.Read("{") },
    };

    [Theory]
    [MemberData(nameof(CodecReads))]
    public void Every_codec_read_failure_arrives_as_one_catchable_type_naming_the_file_and_invariant(
        string expectedFilePath,
        Action read)
    {
        var exception = Record.Exception(read);

        var badStoreFile = Assert.IsType<BadStoreFileException>(exception);
        Assert.Equal(expectedFilePath, badStoreFile.FilePath);
        Assert.False(string.IsNullOrWhiteSpace(badStoreFile.Invariant));
        Assert.NotNull(badStoreFile.InnerException);
    }

    [Fact]
    public void A_known_record_identity_is_structured_on_the_bad_store_file_exception()
    {
        const string json = """
            [
              { "id": "t_1" }
            ]
            """;

        var exception = Record.Exception(() => TaskCodec.Read(json));

        var badStoreFile = Assert.IsType<BadStoreFileException>(exception);
        Assert.Equal("t_1", badStoreFile.RecordIdentity);
        Assert.Contains("Task record", badStoreFile.Invariant);
        Assert.IsType<KeyNotFoundException>(badStoreFile.InnerException);
    }

    [Fact]
    public void A_wrong_json_value_kind_is_preserved_as_the_inner_exception()
    {
        const string json = """
            [
              { "id": "t_1", "title": 42 }
            ]
            """;

        var badStoreFile = Assert.Throws<BadStoreFileException>(() => TaskCodec.Read(json));

        Assert.Equal("t_1", badStoreFile.RecordIdentity);
        Assert.IsType<InvalidOperationException>(badStoreFile.InnerException);
    }

    [Fact]
    public void A_data_format_failure_arrives_as_a_bad_store_file_exception_with_the_original_failure_inside()
    {
        const string json = """
            [
              {
                "id": "t_1", "title": "Bad date", "notes": null,
                "dimensions": {}, "looseTags": [], "deadline": "nonsense",
                "defer": null, "postpone": null, "recurrence": null,
                "createdAt": "2026-08-15T14:02:11Z"
              }
            ]
            """;

        var badStoreFile = Assert.Throws<BadStoreFileException>(() => TaskCodec.Read(json));

        Assert.Equal("t_1", badStoreFile.RecordIdentity);
        Assert.IsType<FormatException>(badStoreFile.InnerException);
    }

    public static TheoryData<string, Action> KnownRecordIdentityReads => new()
    {
        { "dt_1", () => DayTemplateCodec.Read("""[ { "id": "dt_1" } ]""") },
        { "p_1", () => PatternCodec.Read("""{ "activePatternId": "p_1", "patterns": [ { "id": "p_1" } ] }""") },
        { "2026-08-15", () => OverrideCodec.Read("""[ { "date": "2026-08-15" } ]""") },
        { "e_1", () => EventCodec.Read("""[ { "id": "e_1" } ]""") },
        { "(date, prototypeId)=(2026-08-15, ep_1)", () => EventCodec.ReadExceptions("""[ { "date": "2026-08-15", "prototypeId": "ep_1" } ]""") },
        { "(ruleId, triggerId, due)=(r_1, x, 2026-08-15)", () => CompletionCodec.ReadDerived("""[ { "ruleId": "r_1", "triggerId": "x", "due": "2026-08-15" } ]""") },
        { "(date, windowId, kind)=(2026-08-15, w_1, window)", () => FireCodec.Read(new DateOnly(2026, 8, 15), """[ { "windowId": "w_1", "kind": "window" } ]""") },
    };

    [Theory]
    [MemberData(nameof(KnownRecordIdentityReads))]
    public void Every_codec_preserves_a_known_record_identity(string expectedIdentity, Action read)
    {
        var badStoreFile = Assert.Throws<BadStoreFileException>(read);

        Assert.Equal(expectedIdentity, badStoreFile.RecordIdentity);
    }

    [Fact]
    public void An_unidentified_malformed_record_is_not_attributed_to_the_previous_record()
    {
        const string json = """
            [
              {
                "id": "t_1", "title": "Valid", "notes": null,
                "dimensions": {}, "looseTags": [], "deadline": null,
                "defer": null, "postpone": null, "recurrence": null,
                "createdAt": "2026-08-15T14:02:11Z"
              },
              {}
            ]
            """;

        var badStoreFile = Assert.Throws<BadStoreFileException>(() => TaskCodec.Read(json));

        Assert.Null(badStoreFile.RecordIdentity);
    }
}
