using OneOf;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Schedule;

namespace TaskGuide.Application.Schedule;

public sealed class CreateWindow(IStore store)
{
    public async Task<CreateWindowOutcome> ExecuteAsync(DayTemplateId templateId, AvailabilityWindow window, CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<DayTemplateNotFound>(view =>
        {
            if (!view.DayTemplates.Any(template => template.Id.Equals(templateId))) return new DayTemplateNotFound();

            return OneOf<StoreMutation, DayTemplateNotFound>.FromT0(new StoreMutation([
                new DayTemplatesWrite(view.DayTemplates.Select(template => template.Id.Equals(templateId)
                    ? template with { Windows = [.. template.Windows, window] }
                    : template).ToArray()),
            ]));
        }, cancellationToken);

        return outcome.Match<CreateWindowOutcome>(_ => new CreatedWindow(window), refusal => refusal);
    }
}

public sealed class EditWindow(IStore store)
{
    public async Task<EditWindowOutcome> ExecuteAsync(DayTemplateId templateId, AvailabilityWindow window, CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<EditWindowRefusal>(view =>
        {
            var template = view.DayTemplates.SingleOrDefault(candidate => candidate.Id.Equals(templateId));
            if (template is null) return (EditWindowRefusal)new WindowTemplateNotFound();
            if (!template.Windows.Any(candidate => candidate.Id.Equals(window.Id))) return (EditWindowRefusal)new WindowNotFound();

            return OneOf<StoreMutation, EditWindowRefusal>.FromT0(new StoreMutation([
                new DayTemplatesWrite(view.DayTemplates.Select(candidate => !candidate.Id.Equals(templateId)
                    ? candidate
                    : candidate with { Windows = candidate.Windows.Select(existing => existing.Id.Equals(window.Id) ? window : existing).ToArray() }).ToArray()),
            ]));
        }, cancellationToken);

        return outcome.Match<EditWindowOutcome>(_ => new EditedWindow(window), refusal => refusal);
    }
}

public sealed class DeleteWindow(IStore store)
{
    public async Task<DeleteWindowOutcome> ExecuteAsync(DayTemplateId templateId, WindowId windowId, CancellationToken cancellationToken)
    {
        var outcome = await store.MutateAsync<EditWindowRefusal>(view =>
        {
            var template = view.DayTemplates.SingleOrDefault(candidate => candidate.Id.Equals(templateId));
            if (template is null) return (EditWindowRefusal)new WindowTemplateNotFound();
            if (!template.Windows.Any(candidate => candidate.Id.Equals(windowId))) return (EditWindowRefusal)new WindowNotFound();

            return OneOf<StoreMutation, EditWindowRefusal>.FromT0(new StoreMutation([
                new DayTemplatesWrite(view.DayTemplates.Select(candidate => !candidate.Id.Equals(templateId)
                    ? candidate
                    : candidate with { Windows = candidate.Windows.Where(window => !window.Id.Equals(windowId)).ToArray() }).ToArray()),
            ]));
        }, cancellationToken);

        return outcome.Match<DeleteWindowOutcome>(_ => new DeletedWindow(), refusal => refusal);
    }
}

[GenerateOneOf]
public partial class CreateWindowOutcome : OneOfBase<CreatedWindow, DayTemplateNotFound>;

[GenerateOneOf]
public partial class EditWindowOutcome : OneOfBase<EditedWindow, EditWindowRefusal>;

[GenerateOneOf]
public partial class DeleteWindowOutcome : OneOfBase<DeletedWindow, EditWindowRefusal>;

public sealed record CreatedWindow(AvailabilityWindow Window);
public sealed record EditedWindow(AvailabilityWindow Window);
public sealed record DeletedWindow;
public sealed record DayTemplateNotFound;

[GenerateOneOf]
public partial class EditWindowRefusal : OneOfBase<WindowTemplateNotFound, WindowNotFound>;

public sealed record WindowTemplateNotFound;
public sealed record WindowNotFound;
