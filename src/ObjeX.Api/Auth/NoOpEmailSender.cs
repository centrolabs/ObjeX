using Microsoft.AspNetCore.Identity;
using ObjeX.Core.Models;

namespace ObjeX.Api.Auth;

/// <summary>
/// No-op email sender required by MapIdentityApi.
/// Replace with a real implementation when email functionality is needed.
/// </summary>
public class NoOpEmailSender : IEmailSender<User>
{
    public Task SendConfirmationLinkAsync(User user, string email, string confirmationLink) => Task.CompletedTask;
    public Task SendPasswordResetLinkAsync(User user, string email, string resetLink) => Task.CompletedTask;
    public Task SendPasswordResetCodeAsync(User user, string email, string resetCode) => Task.CompletedTask;
}
