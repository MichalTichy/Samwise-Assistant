namespace Samwise.Domain.Inbox;

public sealed record EmailAttachmentSummary(string FileName, string ContentType, long? Size);
