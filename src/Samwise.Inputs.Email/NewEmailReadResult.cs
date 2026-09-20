using Samwise.Inputs.Email;

namespace Samwise.Inputs.Email;

public sealed record NewEmailReadResult(
    IReadOnlyList<EmailMessageSnapshot> Messages,
    EmailMailboxCheckpoint? NextCheckpoint,
    bool CheckpointWasReset);
