using EventParking.API.Migrations.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;

namespace EventParking.API.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        // =========================================================
        // SEND VERIFICATION EMAIL
        // =========================================================
        public async Task SendVerificationEmailAsync(
            string email,
            string token)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new Exception("Receiver email is required.");

            if (string.IsNullOrWhiteSpace(token))
                throw new Exception("Verification token is required.");

            var frontendUrl =
                _config["EmailSettings:FrontendUrl"];

            if (string.IsNullOrWhiteSpace(frontendUrl))
            {
                throw new Exception(
                    "EmailSettings:FrontendUrl is missing in appsettings.json."
                );
            }

            frontendUrl = frontendUrl.TrimEnd('/');

            var verifyLink =
                $"{frontendUrl}/verify-email?token={Uri.EscapeDataString(token)}";

            var subject =
                "Verify your Event Parking Account";

            var body = $"""
Welcome to Event Parking!

Please verify your email address by opening the link below:

{verifyLink}

This verification link will expire after 24 hours.

If you did not create this account, you can ignore this email.
""";

            Console.WriteLine(
                $"[EMAIL] Preparing verification email for {email}"
            );

            await SendEmailAsync(
                email,
                subject,
                body
            );
        }

        // =========================================================
        // SEND PASSWORD RESET EMAIL
        // =========================================================
        public async Task SendPasswordResetEmailAsync(
            string email,
            string token)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new Exception("Receiver email is required.");

            if (string.IsNullOrWhiteSpace(token))
                throw new Exception("Password reset token is required.");

            var frontendUrl =
                _config["EmailSettings:FrontendUrl"];

            if (string.IsNullOrWhiteSpace(frontendUrl))
            {
                throw new Exception(
                    "EmailSettings:FrontendUrl is missing in appsettings.json."
                );
            }

            frontendUrl = frontendUrl.TrimEnd('/');

            var resetLink =
                $"{frontendUrl}/reset-password?token={Uri.EscapeDataString(token)}";

            var subject =
                "Reset your Event Parking Password";

            var body = $"""
You requested a password reset for your Event Parking account.

Please open the link below to create a new password:

{resetLink}

This link will expire after 60 minutes.

If you did not request a password reset, please ignore this email.
""";

            Console.WriteLine(
                $"[EMAIL] Preparing password reset email for {email}"
            );

            await SendEmailAsync(
                email,
                subject,
                body
            );
        }

        // =========================================================
        // SEND EMAIL USING GMAIL SMTP
        // =========================================================
        private async Task SendEmailAsync(
            string toEmail,
            string subject,
            string body)
        {
            var senderEmail =
                _config["EmailSettings:SenderEmail"];

            var appPassword =
                _config["EmailSettings:AppPassword"];

            var senderName =
                _config["EmailSettings:SenderName"];

            // -----------------------------------------------------
            // CONFIG VALIDATION
            // -----------------------------------------------------
            if (string.IsNullOrWhiteSpace(senderEmail))
            {
                throw new Exception(
                    "EmailSettings:SenderEmail is missing in appsettings.json."
                );
            }

            if (string.IsNullOrWhiteSpace(appPassword))
            {
                throw new Exception(
                    "EmailSettings:AppPassword is missing in appsettings.json."
                );
            }

            senderEmail = senderEmail.Trim();

            // Google App Password can be displayed with spaces.
            appPassword =
                appPassword.Replace(" ", "").Trim();

            senderName =
                string.IsNullOrWhiteSpace(senderName)
                    ? "Event Parking"
                    : senderName.Trim();

            toEmail = toEmail.Trim();

            // -----------------------------------------------------
            // VALIDATE EMAIL ADDRESSES
            // -----------------------------------------------------
            try
            {
                _ = new MailAddress(senderEmail);
            }
            catch
            {
                throw new Exception(
                    "SenderEmail in appsettings.json is not a valid email address."
                );
            }

            try
            {
                _ = new MailAddress(toEmail);
            }
            catch
            {
                throw new Exception(
                    "Receiver email address is invalid."
                );
            }

            Console.WriteLine(
                $"[EMAIL] Sender   : {senderEmail}"
            );

            Console.WriteLine(
                $"[EMAIL] Receiver : {toEmail}"
            );

            Console.WriteLine(
                "[EMAIL] Connecting to smtp.gmail.com:587..."
            );

            try
            {
                using var smtp =
                    new SmtpClient(
                        "smtp.gmail.com",
                        587
                    )
                    {
                        EnableSsl = true,

                        UseDefaultCredentials = false,

                        Credentials =
                            new NetworkCredential(
                                senderEmail,
                                appPassword
                            ),

                        DeliveryMethod =
                            SmtpDeliveryMethod.Network,

                        Timeout = 30000
                    };

                using var mail =
                    new MailMessage
                    {
                        From =
                            new MailAddress(
                                senderEmail,
                                senderName
                            ),

                        Subject = subject,

                        Body = body,

                        IsBodyHtml = false
                    };

                mail.To.Add(
                    new MailAddress(toEmail)
                );

                await smtp.SendMailAsync(mail);

                Console.WriteLine(
                    $"[EMAIL SUCCESS] Email sent successfully to {toEmail}"
                );
            }
            catch (SmtpException ex)
            {
                Console.WriteLine(
                    "========== SMTP ERROR =========="
                );

                Console.WriteLine(
                    $"Status Code : {ex.StatusCode}"
                );

                Console.WriteLine(
                    $"Message     : {ex.Message}"
                );

                if (ex.InnerException != null)
                {
                    Console.WriteLine(
                        $"Inner Error : {ex.InnerException.Message}"
                    );
                }

                Console.WriteLine(
                    "================================"
                );

                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "========== EMAIL ERROR =========="
                );

                Console.WriteLine(
                    ex.ToString()
                );

                Console.WriteLine(
                    "================================="
                );

                throw;
            }
        }
    }
}