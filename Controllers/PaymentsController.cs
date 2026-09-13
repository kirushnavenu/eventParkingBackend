using EventParking.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EventParking.API.Controllers
{
    [ApiController]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly PaymentService _paymentService;

        public PaymentsController(PaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        [HttpGet("api/bookings/{id}/payment")]
        public async Task<IActionResult> GetPaymentStatus(int id)
        {
            var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            try { return Ok(await _paymentService.GetPaymentStatusAsync(id, email, User.IsInRole("Admin"))); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (KeyNotFoundException ex) { return NotFound(new { Message = ex.Message }); }
        }

        [HttpPost("api/bookings/{id}/payment")]
        public async Task<IActionResult> ProcessPayment(int id)
        {
            var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            try
            {
                var payment = await _paymentService.ProcessPaymentAsync(id, email);
                return Ok(new
                {
                    Message = "Payment completed successfully.",
                    PaymentId = payment.Id,
                    ReceiptNumber = payment.ReceiptNumber
                });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (Exception ex) { return BadRequest(new { Message = ex.Message }); }
        }

        [HttpGet("api/payments/customer")]
        public async Task<IActionResult> GetCustomerPayments()
        {
            var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            return Ok(await _paymentService.GetPaymentHistoryAsync(email));
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("api/payments")]
        public async Task<IActionResult> GetAllPayments([FromQuery] string? search)
        {
            return Ok(await _paymentService.GetAllPaymentsAsync(search));
        }

        [HttpGet("api/payments/{id}/receipt")]
        public async Task<IActionResult> DownloadReceipt(int id)
        {
            var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            try
            {
                return Ok(await _paymentService.GetReceiptAsync(id, email, User.IsInRole("Admin")));
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (KeyNotFoundException ex) { return NotFound(new { Message = ex.Message }); }
            catch (Exception ex) { return BadRequest(new { Message = ex.Message }); }
        }
    }
}
