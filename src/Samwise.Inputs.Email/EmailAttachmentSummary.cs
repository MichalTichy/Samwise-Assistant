namespace Samwise.Inputs.Email;

public sealed record EmailAttachmentSummary(string FileName, string ContentType, long? Size);
