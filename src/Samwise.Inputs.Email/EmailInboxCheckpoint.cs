namespace Samwise.Inputs.Email;

// Existing Marten documents contain this CLR type name in their JSON.
// Keep it as a compatible subtype after generalizing checkpoints for Sent mail.
public sealed record EmailInboxCheckpoint(
    uint UidValidity,
    uint NextUid,
    DateTimeOffset UpdatedAt)
    : EmailMailboxCheckpoint(UidValidity, NextUid, UpdatedAt);
