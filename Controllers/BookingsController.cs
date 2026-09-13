using EventParking.API.Data;
using EventParking.API.DTOs;
using EventParking.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EventParking.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingsController : ControllerBase
    {
        private readonly BookingService _bookingService;
        private readonly AppDbContext _context;

        public BookingsController(BookingService bookingService, AppDbContext context)
        {
            _bookingService = bookingService;
            _context = context;
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateBooking([FromBody] CreateUnifiedBookingDto dto)
        {
            var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            try
            {
                var booking = await _bookingService.CreateBookingAsync(email, dto);
                var response = new
                {
                    Message = "Booking created. Seats are on hold pending payment.",
                    BookingId = booking.Id,
                    BookingNumber = booking.BookingNumber,
                    HoldExpiresAt = booking.HoldExpiresAt
                };

                return CreatedAtAction(nameof(GetBooking), new { id = booking.Id }, response);
            }
            catch (Exception ex) when (IsAvailabilityConflict(ex.Message))
            {
                return Conflict(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [Authorize]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetBooking(int id)
        {
            var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            try
            {
                return Ok(await _bookingService.GetBookingAsync(id, email, User.IsInRole("Admin")));
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (KeyNotFoundException ex) { return NotFound(new { Message = ex.Message }); }
        }

        [Authorize]
        [HttpGet("{id}/hold-status")]
        public async Task<IActionResult> GetHoldStatus(int id)
        {
            var booking = await _context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
            if (booking == null) return NotFound(new { Message = "Booking not found." });

            var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            if (!User.IsInRole("Admin") && !string.Equals(booking.CustomerEmail, email, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            var remainingSeconds = (booking.HoldExpiresAt - DateTime.UtcNow).TotalSeconds;
            return Ok(new HoldStatusDto
            {
                BookingNumber = booking.BookingNumber,
                Status = booking.Status,
                RemainingSeconds = remainingSeconds > 0 ? remainingSeconds : 0
            });
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> CancelBooking(int id)
        {
            var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            try
            {
                await _bookingService.CancelBookingAsync(id, email, User.IsInRole("Admin"));
                return Ok(new { Message = "Booking cancelled successfully." });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (Exception ex) { return BadRequest(new { Message = ex.Message }); }
        }

        [Authorize]
        [HttpGet("my-bookings")]
        public async Task<IActionResult> GetMyBookings()
        {
            var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            try
            {
                return Ok(await _bookingService.GetCustomerBookingsAsync(email));
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> GetAllBookings(
            [FromQuery] int? eventId,
            [FromQuery] string? status,
            [FromQuery] string? search)
        {
            return Ok(await _bookingService.GetAdminBookingsAsync(eventId, status, search));
        }

        private static bool IsAvailabilityConflict(string message)
        {
            var text = message.ToLowerInvariant();
            return text.Contains("no longer available")
                   || text.Contains("already reserved")
                   || text.Contains("same seat");
        }
    }
}
