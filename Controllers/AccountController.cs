using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Security;
using MilkTeaWeb.ViewModels.Cart;
using MilkTeaWeb.Services;
using MilkTeaWeb.ViewModels.Account;

namespace MilkTeaWeb.Controllers;

public class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    EmailService emailService,
    CartService cartService,
    ILogger<AccountController> logger) : Controller
{
    private const string GenericLoginFailureMessage =
        "Không thể đăng nhập. Hãy kiểm tra email, mật khẩu hoặc trạng thái xác nhận email.";

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register() => View(new RegisterInputModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInputModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            PhoneNumber = model.Phone
        };

        var createResult = await userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return View(model);
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Customer);
        if (!roleResult.Succeeded)
        {
            var deleteResult = await userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
            {
                logger.LogError("Customer role assignment and registration rollback failed.");
            }

            AddIdentityErrors(roleResult);
            return View(model);
        }

        try
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmationUrl = CreateCallbackUrl(
                nameof(ConfirmEmail),
                new { userId = user.Id, token = EncodeToken(token) });

            await emailService.SendConfirmationEmailAsync(user.Email!, confirmationUrl);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Confirmation email delivery could not be started.");
            TempData["Warning"] = "Tài khoản đã được tạo nhưng chưa thể gửi email xác nhận. Bạn có thể gửi lại email xác nhận sau.";
        }

        return RedirectToAction(nameof(RegisterConfirmation));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult RegisterConfirmation() => View();

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? token)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
        {
            return ConfirmEmailResult(false, "Liên kết xác nhận không hợp lệ hoặc đã hết hạn.");
        }

        var user = await userManager.FindByIdAsync(userId);
        var decodedToken = DecodeToken(token);
        if (user is null || decodedToken is null)
        {
            return ConfirmEmailResult(false, "Liên kết xác nhận không hợp lệ hoặc đã hết hạn.");
        }

        if (user.EmailConfirmed)
        {
            return ConfirmEmailResult(true, "Email này đã được xác nhận. Bạn có thể đăng nhập.");
        }

        var result = await userManager.ConfirmEmailAsync(user, decodedToken);
        return result.Succeeded
            ? ConfirmEmailResult(true, "Email đã được xác nhận. Bạn có thể đăng nhập.")
            : ConfirmEmailResult(false, "Liên kết xác nhận không hợp lệ hoặc đã hết hạn.");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResendEmailConfirmation() => View(new ResendEmailConfirmationInputModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendEmailConfirmation(ResendEmailConfirmationInputModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is not null && !user.EmailConfirmed)
        {
            try
            {
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                var confirmationUrl = CreateCallbackUrl(
                    nameof(ConfirmEmail),
                    new { userId = user.Id, token = EncodeToken(token) });

                await emailService.SendConfirmationEmailAsync(user.Email!, confirmationUrl);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Confirmation email resend failed.");
            }
        }

        TempData["Info"] = "Nếu tài khoản phù hợp, hướng dẫn xác nhận đã được gửi.";
        return RedirectToAction(nameof(ResendEmailConfirmation));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl) =>
        View(new LoginInputModel { ReturnUrl = returnUrl });

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInputModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, GenericLoginFailureMessage);
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var isCustomer = await userManager.IsInRoleAsync(user, RoleNames.Customer);
            var isAdmin = await userManager.IsInRoleAsync(user, RoleNames.Admin);

            if (!isCustomer && !isAdmin)
            {
                await signInManager.SignOutAsync();
                logger.LogError("A user without an approved role completed Identity sign-in.");
                ModelState.AddModelError(string.Empty, GenericLoginFailureMessage);
                return View(model);
            }

            CartMergeResult? mergeResult = null;
            if (isCustomer && !isAdmin)
            {
                mergeResult = await cartService.MergeGuestCartIntoCustomerAsync(user);
                if (mergeResult.DatabaseMergeFailed || mergeResult.SessionUpdateFailed)
                {
                    TempData["Warning"] = "Đăng nhập thành công. Giỏ hàng tạm thời sẽ được giữ lại để xử lý an toàn.";
                }
                else if (mergeResult.HasGuestLeftovers)
                {
                    TempData["Warning"] = "Đăng nhập thành công. Một số mục trong giỏ hàng tạm thời cần được kiểm tra lại.";
                }
            }

            return RedirectAfterLogin(model.ReturnUrl, isAdmin, mergeResult);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản tạm thời bị khóa sau nhiều lần đăng nhập không thành công.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, GenericLoginFailureMessage);
        return View(model);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordInputModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordInputModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is not null && user.EmailConfirmed)
        {
            try
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var resetUrl = CreateCallbackUrl(
                    nameof(ResetPassword),
                    new { email = user.Email, token = EncodeToken(token) });

                await emailService.SendPasswordResetEmailAsync(user.Email!, resetUrl);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Password reset email delivery could not be started.");
            }
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token) =>
        View(new ResetPasswordInputModel
        {
            Email = email ?? string.Empty,
            Token = token ?? string.Empty
        });

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordInputModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        var decodedToken = DecodeToken(model.Token);
        if (user is null || decodedToken is null)
        {
            ModelState.AddModelError(string.Empty, "Không thể đặt lại mật khẩu bằng liên kết này.");
            return View(model);
        }

        var result = await userManager.ResetPasswordAsync(user, decodedToken, model.NewPassword);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        return RedirectToAction(nameof(ResetPasswordConfirmation));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPasswordConfirmation() => View();

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordInputModel());

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordInputModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        await signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Mật khẩu đã được cập nhật.";
        return RedirectToAction("Index", "Home");
    }

    [Authorize(Roles = RoleNames.Customer)]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await GetCurrentCustomerAsync();
        return user is null ? Forbid() : View(ToProfileViewModel(user));
    }

    [Authorize(Roles = RoleNames.Customer)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(UpdateProfileInputModel model)
    {
        var user = await GetCurrentCustomerAsync();
        if (user is null)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View(ToProfileViewModel(user, model));
        }

        user.FullName = model.FullName;
        user.PhoneNumber = model.Phone;
        user.DefaultDeliveryAddress = model.DefaultDeliveryAddress;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(ToProfileViewModel(user, model));
        }

        TempData["Success"] = "Hồ sơ đã được cập nhật.";
        return RedirectToAction(nameof(Profile));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    private async Task<ApplicationUser?> GetCurrentCustomerAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return null;
        }

        var isCustomer = await userManager.IsInRoleAsync(user, RoleNames.Customer);
        var isAdmin = await userManager.IsInRoleAsync(user, RoleNames.Admin);
        return isCustomer && !isAdmin ? user : null;
    }

    private IActionResult ConfirmEmailResult(bool succeeded, string message)
    {
        ViewData["Succeeded"] = succeeded;
        ViewData["Message"] = message;
        return View("ConfirmEmail");
    }

    private string CreateCallbackUrl(string action, object values) =>
        Url.ActionLink(action, "Account", values, protocol: Request.Scheme)
        ?? throw new InvalidOperationException("Unable to generate an account callback URL.");

    private IActionResult RedirectAfterLogin(
        string? returnUrl,
        bool isAdmin,
        CartMergeResult? mergeResult)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl!);
        }

        if (isAdmin)
        {
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        return mergeResult is { DatabaseMergeFailed: true }
            or { SessionUpdateFailed: true }
            or { HasGuestLeftovers: true }
            ? RedirectToAction("Index", "Cart")
            : RedirectToAction("Index", "Products");
    }

    private static string EncodeToken(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static string? DecodeToken(string token)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }

    private static ProfileViewModel ToProfileViewModel(ApplicationUser user, UpdateProfileInputModel? model = null) =>
        new()
        {
            Email = user.Email ?? string.Empty,
            FullName = model?.FullName ?? user.FullName,
            Phone = model?.Phone ?? user.PhoneNumber ?? string.Empty,
            DefaultDeliveryAddress = model?.DefaultDeliveryAddress ?? user.DefaultDeliveryAddress
        };
}
