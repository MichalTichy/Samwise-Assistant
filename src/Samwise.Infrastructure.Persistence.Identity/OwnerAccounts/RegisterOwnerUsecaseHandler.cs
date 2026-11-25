using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

public sealed class RegisterOwnerUsecaseHandler(
    ApplicationDbContext database,
    UserManager<ApplicationUser> users,
    OwnerRegistrationAccess access,
    IPublisher publisher) : IRequestHandler<RegisterOwnerUsecase, OwnerRegistrationResult>
{
    public async Task<OwnerRegistrationResult> Handle(RegisterOwnerUsecase request, CancellationToken cancellationToken)
    {
        if (!await access.IsAvailableAsync(request.Token, cancellationToken))
            return OwnerRegistrationResult.Unavailable;
        var email = request.Email.Trim();
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email) || request.Password.Length is < 10 or > 128)
            return OwnerRegistrationResult.InvalidInput;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync(OwnerAccountConcurrency.LockSql, cancellationToken);
        // Recheck inside the database lock: parallel requests/processes must never create a second owner.
        if (!await access.IsAvailableAsync(request.Token, cancellationToken))
            return OwnerRegistrationResult.Unavailable;
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, LockoutEnabled = false };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return OwnerRegistrationResult.InvalidInput;
        await transaction.CommitAsync(cancellationToken);
        await publisher.Publish(new OwnerAccountCreatedNotification(user.Id), cancellationToken);
        return OwnerRegistrationResult.Created;
    }
}
