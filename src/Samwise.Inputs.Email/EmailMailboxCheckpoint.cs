namespace Samwise.Inputs.Email;

public record EmailMailboxCheckpoint(
    uint UidValidity,
    uint NextUid,
    DateTimeOffset UpdatedAt);
