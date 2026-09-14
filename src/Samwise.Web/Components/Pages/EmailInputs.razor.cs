using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Samwise.Domain.Inputs.Email;
using Samwise.Logic.Inputs.Email;
using Samwise.Logic.Inputs.Email.Specifications;
using Samwise.Logic.Inputs.Email.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Components.Pages;

public partial class EmailInputs
{
    private const string AllAccountsMarker = "*";

    private IReadOnlyList<EmailAccountItem> accounts = [];
    private int pollingIntervalMinutes = EmailInputConfiguration.DefaultPollingIntervalMinutes;
    private List<string> ignoredSenderAddresses = [];
    private string ignoredSenderInput = string.Empty;
    private bool loading;
    private bool savingSettings;
    private string? settingsSaveMessage;
    private string? pageMessage;
    private bool pageMessageIsSuccess;
    private string? processingAccountId;
    private string? testingAccountId;

    private bool editingAccount;
    private string? selectedAccountId;
    private string accountName = string.Empty;
    private string accountEmailAddress = string.Empty;
    private string host = string.Empty;
    private int port = 993;
    private EmailConnectionSecurity security = EmailConnectionSecurity.SslOnConnect;
    private EmailAuthenticationMethod authenticationMethod = EmailAuthenticationMethod.Password;
    private string username = string.Empty;
    private string password = string.Empty;
    private MicrosoftEmailAuthorizationChallenge? microsoftAuthorizationChallenge;
    private bool startingMicrosoftAuthorization;
    private bool accountEnabled = true;
    private bool savingAccount;
    private IReadOnlyList<string> accountErrors = [];

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<EmailInputConfiguration> ConfigurationRepository { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<EmailAccount> AccountRepository { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            var configuration = await ConfigurationRepository.GetRequiredByIdAsync(
                EmailInputConfiguration.DocumentId);
            pollingIntervalMinutes = configuration.PollingIntervalMinutes;
            ignoredSenderAddresses = configuration.IgnoredSenderAddresses.ToList();
            var storedAccounts = await AccountRepository.ListAsync(new EmailAccountsSpecification());
            accounts = storedAccounts.Select(account => new EmailAccountItem
            {
                Id = account.Id,
                DisplayName = account.DisplayName,
                EmailAddress = account.EmailAddress,
                Host = account.Host,
                Port = account.Port,
                Security = account.Security,
                AuthenticationMethod = account.AuthenticationMethod,
                Username = account.Username,
                IsEnabled = account.IsEnabled,
                LastSuccessfulCheckAt = account.LastSuccessfulCheckAt,
                LastError = account.LastError
            }).ToList();
        }
        finally
        {
            loading = false;
        }
    }

    private void AddIgnoredSender()
    {
        var address = EmailInputConfiguration.NormalizeAddress(ignoredSenderInput);
        if (address.Length == 0)
        {
            return;
        }

        if (!ignoredSenderAddresses.Contains(address, StringComparer.OrdinalIgnoreCase))
        {
            ignoredSenderAddresses.Add(address);
            ignoredSenderAddresses.Sort(StringComparer.OrdinalIgnoreCase);
        }

        ignoredSenderInput = string.Empty;
        settingsSaveMessage = null;
    }

    private void HandleIgnoredSenderKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            AddIgnoredSender();
        }
    }

    private void RemoveIgnoredSender(string address)
    {
        ignoredSenderAddresses.Remove(address);
        settingsSaveMessage = null;
    }

    private async Task SaveSettingsAsync()
    {
        savingSettings = true;
        settingsSaveMessage = null;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var result = await Sender.Send(new SaveEmailInputConfigurationUsecase(
                pollingIntervalMinutes,
                ignoredSenderAddresses));
            if (!result.Succeeded)
            {
                pageMessage = string.Join(" ", result.Errors);
                return;
            }

            await LoadAsync();
            settingsSaveMessage = "Saved";
        }
        finally
        {
            savingSettings = false;
        }
    }

    private void NewAccount()
    {
        selectedAccountId = null;
        accountName = string.Empty;
        accountEmailAddress = string.Empty;
        host = string.Empty;
        port = 993;
        security = EmailConnectionSecurity.SslOnConnect;
        authenticationMethod = EmailAuthenticationMethod.Password;
        username = string.Empty;
        password = string.Empty;
        microsoftAuthorizationChallenge = null;
        accountEnabled = true;
        accountErrors = [];
        editingAccount = true;
    }

    private void EditAccount(EmailAccountItem account)
    {
        selectedAccountId = account.Id;
        accountName = account.DisplayName;
        accountEmailAddress = account.EmailAddress;
        host = account.Host;
        port = account.Port;
        security = account.Security;
        authenticationMethod = account.AuthenticationMethod;
        username = account.Username;
        password = string.Empty;
        microsoftAuthorizationChallenge = null;
        accountEnabled = account.IsEnabled;
        accountErrors = [];
        editingAccount = true;
    }

    private void CancelAccountEdit()
    {
        password = string.Empty;
        microsoftAuthorizationChallenge = null;
        editingAccount = false;
        accountErrors = [];
    }

    private async Task SaveAccountAsync()
    {
        savingAccount = true;
        accountErrors = [];
        try
        {
            var result = await Sender.Send(new SaveEmailAccountUsecase(
                selectedAccountId,
                accountName,
                accountEmailAddress,
                host,
                port,
                security,
                authenticationMethod,
                username,
                password,
                microsoftAuthorizationChallenge?.AuthorizationId,
                accountEnabled));
            accountErrors = result.Errors;
            if (!result.Succeeded)
            {
                return;
            }

            CancelAccountEdit();
            await LoadAsync();
            pageMessage = "Email account saved.";
            pageMessageIsSuccess = true;
        }
        finally
        {
            savingAccount = false;
        }
    }

    private void AuthenticationMethodChanged()
    {
        accountErrors = [];
        microsoftAuthorizationChallenge = null;
    }

    private async Task StartMicrosoftAuthorizationAsync()
    {
        startingMicrosoftAuthorization = true;
        accountErrors = [];
        try
        {
            ApplyMicrosoft365Defaults();
            var result = await Sender.Send(new StartMicrosoftEmailAuthorizationUsecase());
            if (!result.Succeeded || result.Challenge is null)
            {
                accountErrors = result.Errors;
                return;
            }

            microsoftAuthorizationChallenge = result.Challenge;
        }
        finally
        {
            startingMicrosoftAuthorization = false;
        }
    }

    private void ApplyMicrosoft365Defaults()
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "outlook.office365.com";
        }

        port = 993;
        security = EmailConnectionSecurity.SslOnConnect;
        if (string.IsNullOrWhiteSpace(username))
        {
            username = accountEmailAddress.Trim();
        }
    }

    private async Task TestAccountAsync(string accountId)
    {
        testingAccountId = accountId;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var result = await Sender.Send(new TestEmailAccountConnectionUsecase(accountId));
            pageMessage = result.Message;
            pageMessageIsSuccess = result.Succeeded;
        }
        finally
        {
            testingAccountId = null;
        }
    }

    private Task ProcessAllAsync() => ProcessAsync(null);

    private Task ProcessAccountAsync(string accountId) => ProcessAsync(accountId);

    private async Task ProcessAsync(string? accountId)
    {
        processingAccountId = accountId ?? AllAccountsMarker;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var result = await Sender.Send(new ProcessEmailsUsecase(EmailSelection.Unread, accountId));
            pageMessage = FormatProcessingResult(result);
            pageMessageIsSuccess = result.Errors.Count == 0;
            await LoadAsync();
        }
        finally
        {
            processingAccountId = null;
        }
    }

    private static string FormatProcessingResult(EmailProcessingResult result)
    {
        var summary = $"Checked {result.AccountsChecked} account(s): {result.Imported} imported, "
            + $"{result.Ignored} ignored, {result.AlreadyProcessed} already processed.";
        return result.Errors.Count == 0
            ? summary
            : $"{summary} {string.Join(" ", result.Errors)}";
    }

    private static string FormatLastCheck(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("g") ?? "Never";

    private static string FormatSecurity(EmailConnectionSecurity value) => value switch
    {
        EmailConnectionSecurity.SslOnConnect => "SSL/TLS",
        EmailConnectionSecurity.StartTls => "STARTTLS",
        _ => value.ToString()
    };

    private static string FormatAuthentication(EmailAuthenticationMethod value) => value switch
    {
        EmailAuthenticationMethod.Password => "Password",
        EmailAuthenticationMethod.Microsoft365OAuth => "Microsoft 365",
        _ => value.ToString()
    };

    private sealed record EmailAccountItem
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string EmailAddress { get; init; } = string.Empty;
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; }
        public EmailConnectionSecurity Security { get; init; }
        public EmailAuthenticationMethod AuthenticationMethod { get; init; }
        public string Username { get; init; } = string.Empty;
        public bool IsEnabled { get; init; }
        public DateTimeOffset? LastSuccessfulCheckAt { get; init; }
        public string? LastError { get; init; }
    }
}
