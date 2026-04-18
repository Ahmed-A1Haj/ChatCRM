using System.Net;
using System.Net.Mail;
using System.Text;
using ChatCRM.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ChatCRM.MVC.Services
{
    public class SmtpEmailSender : IEmailSender<User>
    {
        private readonly SmtpEmailOptions _options;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IOptions<SmtpEmailOptions> options, ILogger<SmtpEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public Task SendConfirmationLinkAsync(User user, string email, string confirmationLink)
        {
            var displayName = BuildDisplayName(user);
            var html = BuildEmailHtml(
                preheader: "Confirm your ChatCRM account.",
                heading: "Confirm your email address",
                greetingName: displayName,
                bodyText: "Thanks for creating your ChatCRM account. Please confirm your email address to finish setting up secure access.",
                actionText: "Confirm email",
                actionUrl: confirmationLink,
                footerText: "If you didn't create this account, you can safely ignore this email.");

            return SendEmailAsync(email, "Confirm your ChatCRM account", html);
        }

        public Task SendPasswordResetLinkAsync(User user, string email, string resetLink)
        {
            var displayName = BuildDisplayName(user);
            var html = BuildEmailHtml(
                preheader: "Reset your ChatCRM password.",
                heading: "Reset your password",
                greetingName: displayName,
                bodyText: "We received a request to reset your ChatCRM password. Use the secure button below to choose a new password.",
                actionText: "Reset password",
                actionUrl: resetLink,
                footerText: "If you didn't request a password reset, you can ignore this email and your password will stay the same.");

            return SendEmailAsync(email, "Reset your ChatCRM password", html);
        }

        public Task SendPasswordResetCodeAsync(User user, string email, string resetCode)
        {
            var displayName = BuildDisplayName(user);
            var html = BuildEmailHtml(
                preheader: "Use this code to reset your password.",
                heading: "Your password reset code",
                greetingName: displayName,
                bodyText: $"Use this code to reset your password: <strong>{WebUtility.HtmlEncode(resetCode)}</strong>",
                actionText: null,
                actionUrl: null,
                footerText: "If you didn't request a password reset, you can ignore this email.");

            return SendEmailAsync(email, "Your ChatCRM password reset code", html);
        }

        private async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            ValidateConfiguration();

            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromEmail, _options.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            message.To.Add(toEmail);

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_options.Username, _options.Password),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            try
            {
                await client.SendMailAsync(message);
                _logger.LogInformation("Email sent to {Email} with subject {Subject}", toEmail, subject);
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "SMTP error while sending email to {Email}", toEmail);
                throw new InvalidOperationException("We couldn't send the email right now. Please try again in a moment.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while sending email to {Email}", toEmail);
                throw new InvalidOperationException("We couldn't send the email right now. Please try again in a moment.", ex);
            }
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_options.Host) ||
                string.IsNullOrWhiteSpace(_options.FromEmail) ||
                string.IsNullOrWhiteSpace(_options.Username) ||
                string.IsNullOrWhiteSpace(_options.Password))
            {
                throw new InvalidOperationException(
                    "SMTP email settings are incomplete. Configure Smtp:Host, Smtp:FromEmail, Smtp:Username, and Smtp:Password via configuration/environment variables.");
            }
        }

        private static string BuildDisplayName(User user)
        {
            var fullName = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrWhiteSpace(fullName) ? "there" : fullName;
        }

        private static string BuildEmailHtml(
            string preheader,
            string heading,
            string greetingName,
            string bodyText,
            string? actionText,
            string? actionUrl,
            string footerText)
        {
            var safeHeading = WebUtility.HtmlEncode(heading);
            var safeGreeting = WebUtility.HtmlEncode(greetingName);
            var safePreheader = WebUtility.HtmlEncode(preheader);
            var safeFooter = WebUtility.HtmlEncode(footerText);

            var builder = new StringBuilder();
            builder.AppendLine("<!DOCTYPE html>");
            builder.AppendLine("<html><body style=\"margin:0;background:#f4efe7;font-family:Segoe UI,Arial,sans-serif;color:#182126;\">");
            builder.AppendLine($"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">{safePreheader}</div>");
            builder.AppendLine("<div style=\"max-width:620px;margin:24px auto;padding:24px;\">");
            builder.AppendLine("<div style=\"background:#113c52;border-radius:24px;padding:20px 24px;color:#fff;\">");
            builder.AppendLine("<div style=\"font-size:12px;letter-spacing:2px;text-transform:uppercase;opacity:.75;\">ChatCRM</div>");
            builder.AppendLine($"<h1 style=\"margin:12px 0 0;font-size:30px;line-height:1.1;\">{safeHeading}</h1>");
            builder.AppendLine("</div>");
            builder.AppendLine("<div style=\"background:#fffdf9;padding:28px 24px;border-radius:0 0 24px 24px;border:1px solid rgba(24,33,38,.08);border-top:0;\">");
            builder.AppendLine($"<p style=\"font-size:16px;\">Hi {safeGreeting},</p>");
            builder.AppendLine($"<p style=\"font-size:16px;line-height:1.6;\">{bodyText}</p>");

            if (!string.IsNullOrWhiteSpace(actionText) && !string.IsNullOrWhiteSpace(actionUrl))
            {
                var safeActionText = WebUtility.HtmlEncode(actionText);
                var safeActionUrl = WebUtility.HtmlEncode(actionUrl);
                builder.AppendLine("<div style=\"margin:28px 0;\">");
                builder.AppendLine($"<a href=\"{safeActionUrl}\" style=\"display:inline-block;background:#d66f3d;color:#fff;text-decoration:none;padding:14px 22px;border-radius:999px;font-weight:700;\">{safeActionText}</a>");
                builder.AppendLine("</div>");
                builder.AppendLine($"<p style=\"font-size:14px;color:#5b6974;line-height:1.6;\">If the button doesn't work, copy and paste this link into your browser:<br /><a href=\"{safeActionUrl}\" style=\"color:#113c52;word-break:break-all;\">{safeActionUrl}</a></p>");
            }

            builder.AppendLine($"<p style=\"font-size:14px;color:#5b6974;line-height:1.6;\">{safeFooter}</p>");
            builder.AppendLine("</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("</body></html>");

            return builder.ToString();
        }
    }
}
