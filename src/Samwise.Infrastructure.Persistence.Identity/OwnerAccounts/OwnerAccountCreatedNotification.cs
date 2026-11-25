using MediatR;

namespace Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

public sealed record OwnerAccountCreatedNotification(string OwnerId) : INotification;
