namespace Samwise.Domain.Inputs.Email;

public record EmailMailboxCheckpoint(
    uint UidValidity,
    uint NextUid,
    DateTimeOffset UpdatedAt);
