namespace Samwise.Domain.Inputs.Email;

public sealed record EmailMailboxCheckpoint(
    uint UidValidity,
    uint NextUid,
    DateTimeOffset UpdatedAt);
