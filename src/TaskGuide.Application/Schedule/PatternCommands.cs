using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Application.Schedule;

public sealed class CreatePattern(IStore store)
{
    public async Task<Pattern> ExecuteAsync(Pattern pattern, CancellationToken cancellationToken)
    {
        await store.MutateAsync<Never>(view => new StoreMutation([
            new PatternsWrite(view.Patterns with { Patterns = [.. view.Patterns.Patterns, pattern] }),
        ]), cancellationToken);
        return pattern;
    }
}

public sealed class EditPattern(IStore store)
{
    public async Task<EditPatternOutcome> ExecuteAsync(Pattern pattern, CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<PatternNotFound>(view =>
        {
            if (!view.Patterns.Patterns.Any(candidate => candidate.Id.Equals(pattern.Id)))
            {
                return new PatternNotFound();
            }

            return OneOf<StoreMutation, PatternNotFound>.FromT0(new StoreMutation([
                new PatternsWrite(view.Patterns with
                {
                    Patterns = view.Patterns.Patterns.Select(candidate => candidate.Id.Equals(pattern.Id) ? pattern : candidate).ToArray(),
                }),
            ]));
        }, cancellationToken);

        return outcome.Match<EditPatternOutcome>(_ => new EditedPattern(pattern), refusal => refusal);
    }
}

public sealed class SwitchActivePattern(IStore store)
{
    public async Task<SwitchActivePatternOutcome> ExecuteAsync(PatternId patternId, CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<PatternNotFound>(view =>
        {
            if (!view.Patterns.Patterns.Any(candidate => candidate.Id.Equals(patternId)))
            {
                return new PatternNotFound();
            }

            return OneOf<StoreMutation, PatternNotFound>.FromT0(new StoreMutation([
                new PatternsWrite(view.Patterns with { ActivePatternId = patternId }),
            ]));
        }, cancellationToken);

        return outcome.Match<SwitchActivePatternOutcome>(_ => new SwitchedActivePattern(), refusal => refusal);
    }
}

public sealed class DeletePattern(IStore store)
{
    public async Task<DeletePatternOutcome> ExecuteAsync(PatternId patternId, CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<DeletePatternRefused>(view =>
        {
            if (!view.Patterns.Patterns.Any(candidate => candidate.Id.Equals(patternId)))
            {
                return new DeletePatternRefused("Pattern was not found");
            }

            if (view.Patterns.ActivePatternId.Equals(patternId))
            {
                return new DeletePatternRefused("Active Pattern cannot be deleted");
            }

            return OneOf<StoreMutation, DeletePatternRefused>.FromT0(new StoreMutation([
                new PatternsWrite(view.Patterns with
                {
                    Patterns = view.Patterns.Patterns.Where(candidate => !candidate.Id.Equals(patternId)).ToArray(),
                }),
            ]));
        }, cancellationToken);

        return outcome.Match<DeletePatternOutcome>(_ => new DeletedPattern(), refusal => refusal);
    }
}

[GenerateOneOf]
public partial class EditPatternOutcome : OneOfBase<EditedPattern, PatternNotFound>;

public sealed record EditedPattern(Pattern Pattern);
public sealed record PatternNotFound;

[GenerateOneOf]
public partial class SwitchActivePatternOutcome : OneOfBase<SwitchedActivePattern, PatternNotFound>;

public sealed record SwitchedActivePattern;

[GenerateOneOf]
public partial class DeletePatternOutcome : OneOfBase<DeletedPattern, DeletePatternRefused>;

public sealed record DeletedPattern;
public sealed record DeletePatternRefused(string Reason);
