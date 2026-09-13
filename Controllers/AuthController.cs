using EventParking.API.DTOs;
using EventParking.API.Services;
using Microsoft.AspNetCore.Mvc;
using static EventParking.API.DTOs.AuthDTOs;

namespace EventParking.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        // =========================================================
        // REGISTER
        // POST: /api/auth/register
        // =========================================================
        [HttpPost("register")]
        public async Task<IActionResult> Register(
            [FromBody] RegisterDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new
                    {
                        message = "Registration details are required."
                    });
                }

                var result =
                    await _authService.RegisterAsync(dto);

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        message = result.Message
                    }
                );
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }

        // =========================================================
        // LOGIN
        // POST: /api/auth/login
        // =========================================================
        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromBody] LoginDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new
                    {
                        message = "Login details are required."
                    });
                }

                var result =
                    await _authService.LoginAsync(dto);

                return Ok(new
                {
                    token = result.Token,
                    customerId = result.Message
                });
            }
            catch (Exception ex)
            {
                return Unauthorized(new
                {
                    message = ex.Message
                });
            }
        }

        // =========================================================
        // VERIFY EMAIL
        // GET: /api/auth/verify-email?token=xxxx
        // =========================================================
        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail(
            [FromQuery] string token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return BadRequest(new
                    {
                        message = "Verification token is required."
                    });
                }

                var success =
                    await _authService.VerifyEmailAsync(token);

                if (!success)
                {
                    return BadRequest(new
                    {
                        message =
                            "Invalid or expired verification token."
                    });
                }

                return Ok(new
                {
                    message = "Email verified successfully."
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }

        // =========================================================
        // FORGOT PASSWORD
        // POST: /api/auth/forgot-password
        // =========================================================
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(
            [FromBody] ForgotPasswordDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new
                    {
                        message = "Email is required."
                    });
                }

                await _authService.ForgotPasswordAsync(dto);

                // Same response whether email exists or not.
                // This helps prevent account/email enumeration.
                return Ok(new
                {
                    message =
                        "If an account exists, a reset link has been sent."
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FORGOT PASSWORD ERROR] {ex.Message}"
                );

                // Don't expose internal email/service information.
                return Ok(new
                {
                    message =
                        "If an account exists, a reset link has been sent."
                });
            }
        }

        // =========================================================
        // RESET PASSWORD
        // POST: /api/auth/reset-password
        // =========================================================
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(
            [FromBody] ResetPasswordDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new
                    {
                        message =
                            "Reset password details are required."
                    });
                }

                await _authService.ResetPasswordAsync(dto);

                return Ok(new
                {
                    message = "Password reset successful."
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }

        // =========================================================
        // RESEND VERIFICATION EMAIL
        // POST: /api/auth/resend-verification
        // =========================================================
        [HttpPost("resend-verification")]
        public async Task<IActionResult> ResendVerification(
            [FromBody] ResendVerificationDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new
                    {
                        message = "Email is required."
                    });
                }

                var success =
                    await _authService
                        .ResendVerificationEmailAsync(dto);

                if (success)
                {
                    return Ok(new
                    {
                        message =
                            "A new verification email has been sent."
                    });
                }

                // Generic message:
                // don't reveal whether account exists / is verified.
                return Ok(new
                {
                    message =
                        "If the account exists and is unverified, a new verification link has been sent."
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[RESEND VERIFICATION ERROR] {ex.Message}"
                );

                // Keep public response generic.
                return Ok(new
                {
                    message =
                        "If the account exists and is unverified, a new verification link has been sent."
                });
            }
        }
    }
}