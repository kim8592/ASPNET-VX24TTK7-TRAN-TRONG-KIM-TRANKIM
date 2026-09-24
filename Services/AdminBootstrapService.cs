using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Options;
using MilkTeaWeb.Security;

namespace MilkTeaWeb.Services;

public sealed class AdminBootstrapService(
    IOptions<AdminBootstrapOptions> adminBootstrapOptions,
    UserManager<ApplicationUser> userManager,
    ILogger<AdminBootstrapService> logger)
{
    public async Task EnsureAdminAsync()
    {
        var options = adminBootstrapOptions.Value;
        var email = (options.Email ?? string.Empty).Trim();
        var password = options.Password ?? string.Empty;
        var fullName = (options.FullName ?? string.Empty).Trim();
        var hasAnyConfiguration = !string.IsNullOrWhiteSpace(email)
            || !string.IsNullOrWhiteSpace(password)
            || !string.IsNullOrWhiteSpace(fullName);

        if (!hasAnyConfiguration)
        {
            logger.LogInformation("Admin bootstrap was skipped because no bootstrap configuration was provided.");
            return;
        }

        if (string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(fullName)
            || fullName.Length > 150)
        {
            logger.LogError("Admin bootstrap configuration is incomplete or invalid. No account was created.");
            return;
        }

        try
        {
            var existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser is not null)
            {
                if (await userManager.IsInRoleAsync(existingUser, RoleNames.Admin))
                {
                    logger.LogInformation("Configured Admin bootstrap account already exists. No changes were made.");
                    return;
                }

                logger.LogError("Admin bootstrap email matches an existing non-Admin user. No privilege change was made.");
                return;
            }

            var admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName
            };

            var createResult = await userManager.CreateAsync(admin, password);
            if (!createResult.Succeeded)
            {
                logger.LogError(
                    "Admin bootstrap account creation failed with Identity error codes: {ErrorCodes}.",
                    string.Join(", ", createResult.Errors.Select(error => error.Code)));
                return;
            }

            var roleResult = await userManager.AddToRoleAsync(admin, RoleNames.Admin);
            if (roleResult.Succeeded)
            {
                logger.LogInformation("Admin bootstrap account was created successfully.");
                return;
            }

            logger.LogError(
                "Admin bootstrap role assignment failed with Identity error codes: {ErrorCodes}.",
                string.Join(", ", roleResult.Errors.Select(error => error.Code)));

            var deleteResult = await userManager.DeleteAsync(admin);
            if (!deleteResult.Succeeded)
            {
                logger.LogError("Admin bootstrap rollback failed after role assignment failure.");
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Admin bootstrap did not complete.");
        }
    }
}
