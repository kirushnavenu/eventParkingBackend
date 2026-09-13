using EventParking.API.Migrations.Interfaces;
using EventParking.API.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using static EventParking.API.DTOs.AuthDTOs;

namespace EventParking.API.Services
{
    public class AuthService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;

        public AuthService(
            ICustomerRepository customerRepository,
            IConfiguration config,
            IEmailService emailService)
        {
            _customerRepository = customerRepository;
            _config = config;
            _emailService = emailService;
        }

        // =========================================================
        // REGISTER
        // =========================================================
        public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
        {
            if (dto == null)
                throw new Exception("Registration details are required.");

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new Exception("Name is required.");

            if (string.IsNullOrWhiteSpace(dto.Email))
                throw new Exception("Email is required.");

            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new Exception("Password is required.");

            var email = NormalizeEmail(dto.Email);

            var existingCustomer =
                await _customerRepository.GetByEmailAsync(email);

            if (existingCustomer != null)
                throw new Exception("Email is already registered.");

            var plainToken = GenerateSecureToken();

            var customer = new Customer
            {
                Name = dto.Name.Trim(),

                Email = email,

                // Prevent CS8601 warning when Customer.Phone is non-nullable
                Phone = dto.Phone?.Trim() ?? string.Empty,

                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(dto.Password),

                // Only hashed token is stored in database
                EmailVerificationToken =
                    HashToken(plainToken),

                EmailVerificationTokenExpiresAt =
                    DateTime.UtcNow.AddHours(24),

                EmailVerified = false,

                Status = "Pending"
            };

            await _customerRepository.AddAsync(customer);

            try
            {
                await _emailService.SendVerificationEmailAsync(
                    customer.Email,
                    plainToken
                );

                Console.WriteLine(
                    $"[SUCCESS] Verification email sent to {customer.Email}"
                );

                return new AuthResponseDto(
                    string.Empty,
                    "Registration successful. Please check your email and verify your account."
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[EMAIL ERROR] Registration email failed: {ex.Message}"
                );

                // Customer is already created.
                // They can use Resend Verification.
                return new AuthResponseDto(
                    string.Empty,
                    "Registration successful, but verification email could not be sent. Please use Resend Verification."
                );
            }
        }

        // =========================================================
        // LOGIN
        // =========================================================
        public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
        {
            if (dto == null)
                throw new Exception("Login details are required.");

            if (string.IsNullOrWhiteSpace(dto.Email))
                throw new Exception("Email is required.");

            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new Exception("Password is required.");

            var email = NormalizeEmail(dto.Email);

            var customer =
                await _customerRepository.GetByEmailAsync(email);

            if (customer == null)
                throw new Exception("Invalid email or password.");

            if (string.IsNullOrWhiteSpace(customer.PasswordHash))
                throw new Exception("Invalid email or password.");

            var passwordValid =
                BCrypt.Net.BCrypt.Verify(
                    dto.Password,
                    customer.PasswordHash
                );

            if (!passwordValid)
                throw new Exception("Invalid email or password.");

            if (!customer.EmailVerified)
            {
                throw new Exception(
                    "Please verify your email address before logging in."
                );
            }

            if (string.Equals(
                    customer.Status,
                    "Deactivated",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    "Your account has been deactivated. Please contact support."
                );
            }

            var token = GenerateJwtToken(customer);

            return new AuthResponseDto(
                token,
                customer.Id.ToString()
            );
        }

        // =========================================================
        // VERIFY EMAIL
        // =========================================================
        public async Task<bool> VerifyEmailAsync(string plainToken)
        {
            if (string.IsNullOrWhiteSpace(plainToken))
                return false;

            var hashedToken =
                HashToken(plainToken.Trim());

            var customer =
                await _customerRepository
                    .GetByVerificationTokenAsync(hashedToken);

            if (customer == null)
                return false;

            if (!customer.EmailVerificationTokenExpiresAt.HasValue)
                return false;

            if (customer.EmailVerificationTokenExpiresAt.Value
                < DateTime.UtcNow)
            {
                return false;
            }

            // Already verified
            if (customer.EmailVerified)
                return true;

            customer.EmailVerified = true;

            customer.EmailVerificationToken = null;

            customer.EmailVerificationTokenExpiresAt = null;

            customer.Status = "Active";

            await _customerRepository.UpdateAsync(customer);

            Console.WriteLine(
                $"[SUCCESS] Email verified: {customer.Email}"
            );

            return true;
        }

        // =========================================================
        // RESEND VERIFICATION EMAIL
        // =========================================================
        public async Task<bool> ResendVerificationEmailAsync(
            ResendVerificationDto dto)
        {
            if (dto == null ||
                string.IsNullOrWhiteSpace(dto.Email))
            {
                Console.WriteLine(
                    "[RESEND] Email is missing."
                );

                return false;
            }

            var email = NormalizeEmail(dto.Email);

            Console.WriteLine(
                $"[RESEND] Searching customer: {email}"
            );

            var customer =
                await _customerRepository.GetByEmailAsync(email);

            if (customer == null)
            {
                Console.WriteLine(
                    "[RESEND] Customer not found."
                );

                return false;
            }

            if (customer.EmailVerified)
            {
                Console.WriteLine(
                    "[RESEND] Customer is already verified."
                );

                return false;
            }

            if (string.Equals(
                    customer.Status,
                    "Deactivated",
                    StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    "[RESEND] Customer account is deactivated."
                );

                return false;
            }

            var plainToken = GenerateSecureToken();

            customer.EmailVerificationToken =
                HashToken(plainToken);

            customer.EmailVerificationTokenExpiresAt =
                DateTime.UtcNow.AddHours(24);

            customer.Status = "Pending";

            await _customerRepository.UpdateAsync(customer);

            Console.WriteLine(
                "[RESEND] New verification token saved."
            );

            try
            {
                await _emailService.SendVerificationEmailAsync(
                    customer.Email,
                    plainToken
                );

                Console.WriteLine(
                    $"[SUCCESS] Verification email resent to {customer.Email}"
                );

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[EMAIL ERROR] Resend verification failed: {ex.Message}"
                );

                return false;
            }
        }

        // =========================================================
        // FORGOT PASSWORD
        // =========================================================
        public async Task ForgotPasswordAsync(
            ForgotPasswordDto dto)
        {
            if (dto == null ||
                string.IsNullOrWhiteSpace(dto.Email))
            {
                return;
            }

            var email = NormalizeEmail(dto.Email);

            var customer =
                await _customerRepository.GetByEmailAsync(email);

            // Do not reveal whether account exists
            if (customer == null)
                return;

            var plainToken = GenerateSecureToken();

            customer.PasswordResetToken =
                HashToken(plainToken);

            customer.PasswordResetTokenExpiresAt =
                DateTime.UtcNow.AddHours(1);

            await _customerRepository.UpdateAsync(customer);

            try
            {
                await _emailService.SendPasswordResetEmailAsync(
                    customer.Email,
                    plainToken
                );

                Console.WriteLine(
                    $"[SUCCESS] Password reset email sent to {customer.Email}"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[EMAIL ERROR] Password reset email failed: {ex.Message}"
                );
            }
        }

        // =========================================================
        // RESET PASSWORD
        // =========================================================
        public async Task ResetPasswordAsync(
            ResetPasswordDto dto)
        {
            if (dto == null)
                throw new Exception(
                    "Reset password details are required."
                );

            if (string.IsNullOrWhiteSpace(dto.Token))
                throw new Exception(
                    "Reset token is required."
                );

            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                throw new Exception(
                    "New password is required."
                );

            var hashedToken =
                HashToken(dto.Token.Trim());

            var customer =
                await _customerRepository
                    .GetByResetTokenAsync(hashedToken);

            if (customer == null)
            {
                throw new Exception(
                    "Invalid or expired reset token."
                );
            }

            if (!customer.PasswordResetTokenExpiresAt.HasValue)
            {
                throw new Exception(
                    "Invalid or expired reset token."
                );
            }

            if (customer.PasswordResetTokenExpiresAt.Value
                < DateTime.UtcNow)
            {
                throw new Exception(
                    "Invalid or expired reset token."
                );
            }

            customer.PasswordHash =
                BCrypt.Net.BCrypt.HashPassword(
                    dto.NewPassword
                );

            customer.PasswordResetToken = null;

            customer.PasswordResetTokenExpiresAt = null;

            await _customerRepository.UpdateAsync(customer);

            Console.WriteLine(
                $"[SUCCESS] Password reset completed for {customer.Email}"
            );
        }

        // =========================================================
        // NORMALIZE EMAIL
        // =========================================================
        private static string NormalizeEmail(string email)
        {
            return email
                .Trim()
                .ToLowerInvariant();
        }

        // =========================================================
        // GENERATE SECURE TOKEN
        // =========================================================
        private static string GenerateSecureToken()
        {
            var randomBytes =
                RandomNumberGenerator.GetBytes(32);

            // URL-safe token
            return Base64UrlEncoder.Encode(randomBytes);
        }

        // =========================================================
        // HASH TOKEN
        // =========================================================
        private static string HashToken(string token)
        {
            var tokenBytes =
                Encoding.UTF8.GetBytes(token);

            var hashedBytes =
                SHA256.HashData(tokenBytes);

            return Convert.ToBase64String(hashedBytes);
        }

        // =========================================================
        // GENERATE JWT TOKEN
        // =========================================================
        private string GenerateJwtToken(Customer customer)
        {
            var jwtKey = _config["Jwt:Key"];
            var jwtIssuer = _config["Jwt:Issuer"];
            var jwtAudience = _config["Jwt:Audience"];

            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                throw new Exception(
                    "Jwt:Key is missing in appsettings.json."
                );
            }

            var securityKey =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtKey)
                );

            var credentials =
                new SigningCredentials(
                    securityKey,
                    SecurityAlgorithms.HmacSha256
                );

            var role =
                string.IsNullOrWhiteSpace(customer.Role)
                    ? "Customer"
                    : customer.Role;

            var claims = new List<Claim>
            {
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    customer.Id.ToString()
                ),

                new Claim(
                    JwtRegisteredClaimNames.Email,
                    customer.Email ?? string.Empty
                ),

                new Claim(
                    ClaimTypes.Role,
                    role
                )
            };

            var jwtToken =
                new JwtSecurityToken(
                    issuer: jwtIssuer,
                    audience: jwtAudience,
                    claims: claims,
                    notBefore: DateTime.UtcNow,
                    expires: DateTime.UtcNow.AddHours(2),
                    signingCredentials: credentials
                );

            return new JwtSecurityTokenHandler()
                .WriteToken(jwtToken);
        }
    }
}