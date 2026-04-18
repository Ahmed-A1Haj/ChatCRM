using System.Text;
using ChatCRM.Application.Users.DTOS;
using ChatCRM.Domain.Entities;
using ChatCRM.MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace ChatCRM.MVC.Controllers
{
    public class AccountController : Controller
    {
        private const string GenericSignInError = "We couldn't sign you in with those details.";

        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IEmailSender<User> _emailSender;
        private readonly IProfileImageStorageService _profileImageStorageService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IEmailSender<User> emailSender,
            IProfileImageStorageService profileImageStorageService,
            ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _profileImageStorageService = profileImageStorageService;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View(new RegisterDto());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterDto model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var email = NormalizeEmail(model.Email);

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser is not null)
            {
                if (!await _userManager.IsEmailConfirmedAsync(existingUser))
                {
                    var confirmationResent = await TrySendConfirmationEmailAsync(existingUser);
                    TempData["StatusMessage"] = confirmationResent
                        ? "Your account already exists but the email address is still unverified. We sent a fresh confirmation link."
                        : "Your account already exists but the email address is still unverified. We couldn't send the confirmation email right now, so please try again in a moment.";
                    TempData["StatusType"] = confirmationResent ? "warning" : "danger";
                    return RedirectToAction(nameof(VerifyEmail), new { email });
                }

                ModelState.AddModelError(nameof(model.Email), "An account with this email already exists. Sign in or reset your password instead.");
                return View(model);
            }

            var user = new User
            {
                UserName = email,
                Email = email,
                EmailConfirmed = false
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                return View(model);
            }

            var confirmationSent = await TrySendConfirmationEmailAsync(user);
            TempData["StatusMessage"] = confirmationSent
                ? "Your account has been created. Check your email to confirm it before signing in."
                : "Your account was created, but we couldn't send the confirmation email right now. Please try resending verification from the next screen.";
            TempData["StatusType"] = confirmationSent ? "success" : "warning";
            return RedirectToAction(nameof(VerifyEmail), new { email });
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View(new LoginDto { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginDto model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var email = NormalizeEmail(model.Email);
            var user = await _userManager.FindByEmailAsync(email);

            if (user is null)
            {
                ModelState.AddModelError(string.Empty, GenericSignInError);
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                user,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _logger.LogInformation("User {Email} logged in successfully.", email);
                return RedirectToLocal(model.ReturnUrl);
            }

            if (result.IsLockedOut)
            {
                var lockoutEnd = await _userManager.GetLockoutEndDateAsync(user);
                var message = lockoutEnd.HasValue
                    ? $"Too many failed sign-in attempts. Try again after {lockoutEnd.Value.LocalDateTime:g}."
                    : "Too many failed sign-in attempts. Try again later.";
                ModelState.AddModelError(string.Empty, message);
                return View(model);
            }

            if (result.IsNotAllowed)
            {
                var confirmationSent = await TrySendConfirmationEmailAsync(user);
                TempData["StatusMessage"] = confirmationSent
                    ? "Please confirm your email before signing in. We sent a fresh verification link."
                    : "Please confirm your email before signing in. We couldn't send a fresh verification email right now.";
                TempData["StatusType"] = confirmationSent ? "warning" : "danger";
                return RedirectToAction(nameof(VerifyEmail), new { email });
            }

            ModelState.AddModelError(string.Empty, GenericSignInError);
            return View(model);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await GetCurrentUserAsync();
            if (user is null)
            {
                return Challenge();
            }

            return View(CreateProfileModel(user));
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(UpdateProfileDto model, IFormFile? profileImage)
        {
            var user = await GetCurrentUserAsync();
            if (user is null)
            {
                return Challenge();
            }

            model.ProfileImagePath = user.ProfileImagePath;
            model.EmailConfirmed = user.EmailConfirmed;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var normalizedEmail = NormalizeEmail(model.Email);
            var currentImagePath = user.ProfileImagePath;
            var emailChanged = !string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase);

            if (emailChanged)
            {
                var otherUser = await _userManager.FindByEmailAsync(normalizedEmail);
                if (otherUser is not null && otherUser.Id != user.Id)
                {
                    ModelState.AddModelError(nameof(model.Email), "That email address is already being used by another account.");
                    return View(model);
                }
            }

            string? newImagePath = null;

            if (profileImage is not null)
            {
                var imageResult = await _profileImageStorageService.SaveAsync(user.Id, profileImage, user.ProfileImagePath, HttpContext.RequestAborted);
                if (!imageResult.Succeeded)
                {
                    ModelState.AddModelError(nameof(profileImage), imageResult.ErrorMessage ?? "We couldn't save that image.");
                    return View(model);
                }

                newImagePath = imageResult.RelativePath;
            }
            else if (model.RemoveProfileImage && !string.IsNullOrWhiteSpace(user.ProfileImagePath))
            {
                await _profileImageStorageService.DeleteAsync(user.ProfileImagePath);
                user.ProfileImagePath = null;
            }

            user.FirstName = SanitizeText(model.FirstName);
            user.LastName = SanitizeText(model.LastName);
            user.PhoneNumber = SanitizeText(model.PhoneNumber);

            if (emailChanged)
            {
                user.Email = normalizedEmail;
                user.UserName = normalizedEmail;
                user.EmailConfirmed = false;
            }

            if (!string.IsNullOrWhiteSpace(newImagePath))
            {
                user.ProfileImagePath = newImagePath;
            }

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(newImagePath))
                {
                    await _profileImageStorageService.DeleteAsync(newImagePath);
                    user.ProfileImagePath = currentImagePath;
                }

                AddIdentityErrors(updateResult);
                model.ProfileImagePath = currentImagePath;
                return View(model);
            }

            await _signInManager.RefreshSignInAsync(user);

            if (emailChanged)
            {
                var confirmationSent = await TrySendConfirmationEmailAsync(user);
                TempData["StatusMessage"] = confirmationSent
                    ? "Your profile was updated. Please confirm your new email address."
                    : "Your profile was updated, but we couldn't send the confirmation email for the new address right now.";
                TempData["StatusType"] = confirmationSent ? "warning" : "danger";
            }
            else
            {
                TempData["StatusMessage"] = "Your profile was updated successfully.";
                TempData["StatusType"] = "success";
            }

            return RedirectToAction(nameof(Profile));
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            TempData["StatusMessage"] = "You have been signed out.";
            TempData["StatusType"] = "info";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult VerifyEmail(string email)
        {
            var model = new ResendConfirmationEmailDto
            {
                Email = NormalizeEmail(email)
            };

            return View(model);
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendConfirmationEmail(ResendConfirmationEmailDto model)
        {
            if (!ModelState.IsValid)
            {
                return View(nameof(VerifyEmail), model);
            }

            var email = NormalizeEmail(model.Email);
            var user = await _userManager.FindByEmailAsync(email);

            if (user is not null && !await _userManager.IsEmailConfirmedAsync(user))
            {
                await TrySendConfirmationEmailAsync(user);
            }

            TempData["StatusMessage"] = "If an account exists and still needs verification, we sent a fresh confirmation link if delivery was available.";
            TempData["StatusType"] = "info";
            return RedirectToAction(nameof(VerifyEmail), new { email });
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            {
                TempData["StatusMessage"] = "That verification link is invalid or incomplete.";
                TempData["StatusType"] = "danger";
                return RedirectToAction(nameof(Login));
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                TempData["StatusMessage"] = "We couldn't find the account for that verification link.";
                TempData["StatusType"] = "danger";
                return RedirectToAction(nameof(Login));
            }

            var decodedToken = DecodeToken(token);
            var result = await _userManager.ConfirmEmailAsync(user, decodedToken);

            if (!result.Succeeded)
            {
                TempData["StatusMessage"] = "This verification link has expired or has already been used.";
                TempData["StatusType"] = "danger";
                return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
            }

            TempData["StatusMessage"] = "Your email has been confirmed. You can sign in now.";
            TempData["StatusType"] = "success";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View(new ForgotPasswordDto());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var email = NormalizeEmail(model.Email);
            var user = await _userManager.FindByEmailAsync(email);

            if (user is not null && await _userManager.IsEmailConfirmedAsync(user))
            {
                var resetSent = await TrySendPasswordResetEmailAsync(user);
                if (!resetSent)
                {
                    ModelState.AddModelError(string.Empty, "We couldn't send the password reset email right now. Please try again in a moment.");
                    return View(model);
                }
            }

            TempData["StatusMessage"] = "If an eligible account exists for that email, we sent password reset instructions.";
            TempData["StatusType"] = "info";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPassword(string email, string token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                TempData["StatusMessage"] = "That password reset link is invalid or incomplete.";
                TempData["StatusType"] = "danger";
                return RedirectToAction(nameof(ForgotPassword));
            }

            return View(new ResetPasswordDto
            {
                Email = NormalizeEmail(email),
                Token = token
            });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var email = NormalizeEmail(model.Email);
            var user = await _userManager.FindByEmailAsync(email);

            if (user is null)
            {
                TempData["StatusMessage"] = "If the reset link is valid, you can sign in with your new password now.";
                TempData["StatusType"] = "info";
                return RedirectToAction(nameof(Login));
            }

            var decodedToken = DecodeToken(model.Token);
            var result = await _userManager.ResetPasswordAsync(user, decodedToken, model.Password);

            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                return View(model);
            }

            TempData["StatusMessage"] = "Your password has been updated. Sign in with your new password.";
            TempData["StatusType"] = "success";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private UpdateProfileDto CreateProfileModel(User user)
        {
            return new UpdateProfileDto
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber,
                ProfileImagePath = user.ProfileImagePath,
                EmailConfirmed = user.EmailConfirmed
            };
        }

        private async Task<User?> GetCurrentUserAsync()
        {
            return await _userManager.GetUserAsync(User);
        }

        private async Task SendConfirmationEmailAsync(User user)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var callbackUrl = Url.Action(
                nameof(ConfirmEmail),
                "Account",
                new
                {
                    userId = user.Id,
                    token = EncodeToken(token)
                },
                protocol: Request.Scheme);

            if (callbackUrl is not null && user.Email is not null)
            {
                await _emailSender.SendConfirmationLinkAsync(user, user.Email, callbackUrl);
            }
        }

        private async Task<bool> TrySendConfirmationEmailAsync(User user)
        {
            try
            {
                await SendConfirmationEmailAsync(user);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email to user {UserId}", user.Id);
                return false;
            }
        }

        private async Task SendPasswordResetEmailAsync(User user)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var callbackUrl = Url.Action(
                nameof(ResetPassword),
                "Account",
                new
                {
                    email = user.Email,
                    token = EncodeToken(token)
                },
                protocol: Request.Scheme);

            if (callbackUrl is not null && user.Email is not null)
            {
                await _emailSender.SendPasswordResetLinkAsync(user, user.Email, callbackUrl);
            }
        }

        private async Task<bool> TrySendPasswordResetEmailAsync(User user)
        {
            try
            {
                await SendPasswordResetEmailAsync(user);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to user {UserId}", user.Id);
                return false;
            }
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        private void AddIdentityErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        private static string NormalizeEmail(string? email)
        {
            return (email ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string? SanitizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var clean = new string(value.Trim().Where(ch => !char.IsControl(ch)).ToArray());
            return string.IsNullOrWhiteSpace(clean) ? null : clean;
        }

        private static string EncodeToken(string token)
        {
            return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        }

        private static string DecodeToken(string token)
        {
            try
            {
                return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }
    }
}
