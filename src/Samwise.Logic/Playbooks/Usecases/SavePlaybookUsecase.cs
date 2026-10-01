using MediatR;
using Samwise.Domain.Playbooks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Playbooks.Usecases;

public sealed record SavePlaybookUsecase(
    string? PlaybookId,
    string Name,
    string Applicability,
    string Description,
    bool IsEnabled,
    bool? SuitableForAutomaticApproval = null) : IRequest<PlaybookSaveResult>;

internal sealed class SavePlaybookUsecaseHandler(
    IRepository<Playbook> repository,
    TimeProvider timeProvider) : IRequestHandler<SavePlaybookUsecase, PlaybookSaveResult>
{
    public async Task<PlaybookSaveResult> Handle(
        SavePlaybookUsecase request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new PlaybookSaveResult(false, request.PlaybookId, errors);
        }

        var playbook = string.IsNullOrWhiteSpace(request.PlaybookId)
            ? null
            : await repository.GetByIdAsync(request.PlaybookId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.PlaybookId) && playbook is null)
        {
            return new PlaybookSaveResult(false, request.PlaybookId, ["The playbook no longer exists."]);
        }

        if (playbook is null)
        {
            playbook = new Playbook
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name.Trim(),
                Applicability = request.Applicability.Trim(),
                Description = request.Description.Trim(),
                IsEnabled = request.IsEnabled,
                SuitableForAutomaticApproval = request.SuitableForAutomaticApproval ?? false,
                UpdatedAt = timeProvider.GetUtcNow()
            };
            await repository.AddAsync(playbook, cancellationToken);
        }
        else
        {
            playbook.Name = request.Name.Trim();
            playbook.Applicability = request.Applicability.Trim();
            playbook.Description = request.Description.Trim();
            playbook.IsEnabled = request.IsEnabled;
            playbook.SuitableForAutomaticApproval = request.SuitableForAutomaticApproval
                ?? playbook.SuitableForAutomaticApproval;
            playbook.UpdatedAt = timeProvider.GetUtcNow();
            await repository.UpdateAsync(playbook, cancellationToken);
        }

        return new PlaybookSaveResult(true, playbook.Id, []);
    }

    private static List<string> Validate(SavePlaybookUsecase request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("The playbook name cannot be empty.");
        }
        else if (request.Name.Trim().Length > 80)
        {
            errors.Add("The playbook name cannot be longer than 80 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Applicability))
        {
            errors.Add("The playbook applicability cannot be empty.");
        }
        else if (request.Applicability.Trim().Length > 1_000)
        {
            errors.Add("The playbook applicability cannot be longer than 1,000 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            errors.Add("The playbook description cannot be empty.");
        }
        else if (request.Description.Trim().Length > 20_000)
        {
            errors.Add("The playbook description cannot be longer than 20,000 characters.");
        }

        return errors;
    }
}
