using Samwise.Domain.Inputs.Email;

namespace Samwise.Logic.Inputs.Email;

public sealed record NewEmailReadResult(
    IReadOnlyList<EmailMessageSnapshot> Messages,
    EmailMailboxCheckpoint? NextCheckpoint,
    bool CheckpointWasReset);
