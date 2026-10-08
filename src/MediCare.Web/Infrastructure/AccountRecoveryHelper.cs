using MediCare.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MediCare.Web.Infrastructure;

/// <summary>
/// Server-side administrative recovery helper to clear account lockouts via CLI switch.
/// Usage: dotnet run --project src/MediCare.Web -- --recovery-unlock-user <email>
/// </summary>
public static class AccountRecoveryHelper
{
    public static async Task<bool> UnlockUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            logger?.LogWarning("Account recovery aborted: Email was not specified.");
            return false;
        }

        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user == null)
        {
            logger?.LogWarning("Account recovery failed: User '{Email}' not found.", email);
            return false;
        }

        IdentityResult setLockoutResult;
        IdentityResult resetCountResult;

        if (user.LockoutEnabled)
        {
            setLockoutResult = await userManager.SetLockoutEndDateAsync(user, null);
            resetCountResult = await userManager.ResetAccessFailedCountAsync(user);
        }
        else
        {
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            setLockoutResult = await userManager.UpdateAsync(user);
            resetCountResult = IdentityResult.Success;
        }

        if (setLockoutResult.Succeeded && resetCountResult.Succeeded)
        {
            logger?.LogInformation("SECURITY RECOVERY SUCCESS: Account lockout cleared and failed access count reset for user '{Email}' (UserId: {UserId}).", user.Email, user.Id);
            return true;
        }

        logger?.LogError("Account recovery encountered errors for user '{Email}'. LockoutErrors: {L}, ResetErrors: {R}",
            user.Email,
            string.Join(", ", setLockoutResult.Errors.Select(e => e.Description)),
            string.Join(", ", resetCountResult.Errors.Select(e => e.Description)));
        return false;
    }
}
