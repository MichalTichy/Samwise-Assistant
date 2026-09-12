using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Inputs.Email;

public sealed class EmailInputConfiguration : IItemWithId
{
    public const string DocumentId = "email-input";
    public const int DefaultPollingIntervalMinutes = 5;

    public string Id { get; set; } = DocumentId;
    public int PollingIntervalMinutes { get; set; } = DefaultPollingIntervalMinutes;
    public List<string> IgnoredSenderAddresses { get; set; } = [];

    public void Update(int pollingIntervalMinutes, IEnumerable<string> ignoredSenderAddresses)
    {
        if (pollingIntervalMinutes is < 1 or > 1_440)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollingIntervalMinutes),
                "The polling interval must be between 1 and 1,440 minutes.");
        }

        PollingIntervalMinutes = pollingIntervalMinutes;
        IgnoredSenderAddresses = ignoredSenderAddresses
            .Select(NormalizeAddress)
            .Where(address => address.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsIgnored(string senderAddress)
    {
        var normalized = NormalizeAddress(senderAddress);
        return IgnoredSenderAddresses.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizeAddress(string address) => address.Trim().ToLowerInvariant();
}
