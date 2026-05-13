using System.Net;
using System.Net.Mail;
using ChatCRM.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace ChatCRM.MVC.Services
{
    /// <summary>
    /// SMTP-backed billing email sender. Uses the same SMTP options as the Identity email
    /// flows but never bubbles failures to the caller — top-up logic must succeed even when
    /// the mail server is unreachable. Receipts that fail are logged for ops follow-up.
    /// </summary>
    public sealed class BillingEmailSender : IBillingEmailSender
    {
        private readonly SmtpEmailOptions _options;
        private readonly ILogger<BillingEmailSender> _logger;

        public BillingEmailSender(IOptions<SmtpEmailOptions> options, ILogger<BillingEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendTopUpReceiptAsync(string toEmail, string displayName, decimal amountUsd, decimal balanceAfterUsd, string sessionId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                _logger.LogWarning("[BILLING-EMAIL] Skipping receipt — no email address.");
                return;
            }

            var amountStr = $"${amountUsd:0.00}";
            var newBalanceStr = $"${balanceAfterUsd:0.00}";
            var subject = $"Receipt for your ChatCRM top-up — {amountStr}";
            var name = string.IsNullOrWhiteSpace(displayName) ? "there" : displayName;

            var bodyText =
                $"Thanks for your top-up — we've added {amountStr} to your ChatCRM messaging balance.<br>" +
                $"Your new balance is <strong>{newBalanceStr}</strong>.";

            var html = BuildHtml(
                preheader: $"Receipt for your {amountStr} top-up.",
                heading: "Top-up confirmed",
                greeting: name,
                body: bodyText,
                footer: $"Reference: {WebUtility.HtmlEncode(sessionId)}. Keep this email for your records.");

            try
            {
                await SendAsync(toEmail, subject, html, ct);
            }
            catch (Exception ex)
            {
                // Receipt failures are non-fatal for the top-up flow. Log + move on.
                _logger.LogError(ex, "[BILLING-EMAIL] Failed to send top-up receipt to {Email} for session {Session}.", toEmail, sessionId);
            }
        }

        // ── SMTP send ────────────────────────────────────────────────────────

        private async Task SendAsync(string toEmail, string subject, string html, CancellationToken ct)
        {
            ValidateConfiguration();

            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromEmail, _options.FromName),
                Subject = subject,
                Body = html,
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

            await client.SendMailAsync(message, ct);
            _logger.LogInformation("[BILLING-EMAIL] Sent {Subject} to {Email}.", subject, toEmail);
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_options.Host) ||
                string.IsNullOrWhiteSpace(_options.FromEmail) ||
                string.IsNullOrWhiteSpace(_options.Username) ||
                string.IsNullOrWhiteSpace(_options.Password))
            {
                throw new InvalidOperationException(
                    "SMTP settings are incomplete — receipt cannot be sent. Configure Smtp:Host, Smtp:FromEmail, Smtp:Username, Smtp:Password.");
            }
        }

        // ── HTML scaffold (kept simple — single inline template) ─────────────

        private static string BuildHtml(string preheader, string heading, string greeting, string body, string footer)
        {
            var safePreheader = WebUtility.HtmlEncode(preheader);
            var safeHeading   = WebUtility.HtmlEncode(heading);
            var safeGreeting  = WebUtility.HtmlEncode(greeting);
            return $$"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8" />
                    <title>{{safeHeading}}</title>
                </head>
                <body style="margin:0;padding:0;background:#f8fafc;font-family:Inter,system-ui,-apple-system,sans-serif;color:#0f172a;">
                    <span style="display:none;visibility:hidden;opacity:0;height:0;width:0;overflow:hidden;">{{safePreheader}}</span>
                    <table width="100%" cellpadding="0" cellspacing="0" style="background:#f8fafc;padding:32px 16px;">
                        <tr>
                            <td align="center">
                                <table width="540" cellpadding="0" cellspacing="0" style="background:#ffffff;border:1px solid #e5e7eb;border-radius:12px;padding:28px;max-width:540px;">
                                    <tr><td style="font-size:20px;font-weight:700;color:#0f172a;padding-bottom:8px;">{{safeHeading}}</td></tr>
                                    <tr><td style="font-size:14px;color:#1f2937;padding-bottom:14px;">Hi {{safeGreeting}},</td></tr>
                                    <tr><td style="font-size:14px;color:#1f2937;line-height:1.55;padding-bottom:18px;">{{body}}</td></tr>
                                    <tr><td style="font-size:12px;color:#64748b;border-top:1px solid #e5e7eb;padding-top:14px;line-height:1.5;">{{footer}}</td></tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                </body>
                </html>
                """;
        }
    }
}
