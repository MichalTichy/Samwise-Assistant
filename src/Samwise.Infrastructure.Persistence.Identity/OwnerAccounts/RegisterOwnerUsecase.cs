using MediatR;

namespace Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

public sealed record RegisterOwnerUsecase(string Token, string Email, string Password) : IRequest<OwnerRegistrationResult>;
