using EventParking.API.Data;
using EventParking.API.DTOs;
using EventParking.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EventParking.API.Services
{
    public class PaymentService
    {
        private readonly AppDbContext _context;
        private readonly NotificationService _notificationService;

        public PaymentService(AppDbContext context, NotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<PaymentStatusDto> GetPaymentStatusAsync(int bookingId, string requestedByEmail, bool isAdmin)
        {
            var booking = await _context.Bookings.FindAsync(bookingId);
            if (booking == null) throw new KeyNotFoundException("Booking not found.");
            if (!isAdmin && !string.Equals(booking.CustomerEmail, requestedByEmail, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("You do not have permission to view this payment.");

            var isPaid = await _context.Payments.AnyAsync(p => p.BookingId == bookingId);
            return new PaymentStatusDto
            {
                BookingId = booking.Id,
                AmountDue = booking.TotalPrice,
                PaymentStatus = isPaid ? "Paid" : "Pending",
                IsPaid = isPaid
            };
        }

        public async Task<Payment> ProcessPaymentAsync(int bookingId, string customerEmail)
        {
            var booking = await _context.Bookings.FindAsync(bookingId);
            if (booking == null) throw new Exception("Booking not found.");

            if (!string.Equals(booking.CustomerEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("You do not have permission to pay for this booking.");

            if (booking.Status is "Expired" or "Cancelled")
                throw new Exception("Cannot process payment for an inactive booking. Please create a new booking.");

            if (booking.Status == "Pending" && DateTime.UtcNow > booking.HoldExpiresAt)
                throw new Exception("The booking hold has expired. Please create a new booking.");

            if (await _context.Payments.AnyAsync(p => p.BookingId == bookingId))
                throw new Exception("Payment has already been recorded for this booking.");

            var payment = new Payment
            {
                BookingId = booking.Id,
                CustomerEmail = customerEmail,
                Amount = booking.TotalPrice,
                ReceiptNumber = $"RCPT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}"
            };

            _context.Payments.Add(payment);
            booking.Status = "Confirmed";
            await _context.SaveChangesAsync();

            var customer = await _context.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Email == customerEmail);
            if (customer != null)
            {
                await _notificationService.CreateNotificationAsync(
                    customer.Id,
                    $"Payment successful. Booking {booking.BookingNumber} is confirmed. Receipt: {payment.ReceiptNumber}");
            }

            return payment;
        }

        public async Task<List<PaymentHistoryDto>> GetPaymentHistoryAsync(string customerEmail)
        {
            return await _context.Payments
                .Where(p => p.CustomerEmail == customerEmail)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new PaymentHistoryDto
                {
                    PaymentId = p.Id,
                    BookingId = p.BookingId,
                    CustomerEmail = p.CustomerEmail,
                    ReceiptNumber = p.ReceiptNumber,
                    Amount = p.Amount,
                    PaymentDate = p.PaymentDate
                }).ToListAsync();
        }

        public async Task<List<PaymentHistoryDto>> GetAllPaymentsAsync(string? search)
        {
            var query = _context.Payments.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p => p.CustomerEmail.Contains(term) || p.ReceiptNumber.Contains(term));
            }

            return await query
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new PaymentHistoryDto
                {
                    PaymentId = p.Id,
                    BookingId = p.BookingId,
                    CustomerEmail = p.CustomerEmail,
                    ReceiptNumber = p.ReceiptNumber,
                    Amount = p.Amount,
                    PaymentDate = p.PaymentDate
                }).ToListAsync();
        }

        public async Task<ReceiptDto> GetReceiptAsync(int paymentId, string customerEmail, bool isAdmin = false)
        {
            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null) throw new KeyNotFoundException("Payment not found.");
            if (!isAdmin && !string.Equals(payment.CustomerEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Unauthorized access to receipt.");

            var bookingSeat = await _context.BookingSeats
                .Include(bs => bs.Seat)
                .ThenInclude(s => s!.Event)
                .FirstOrDefaultAsync(bs => bs.BookingId == payment.BookingId);

            return new ReceiptDto
            {
                ReceiptNumber = payment.ReceiptNumber,
                CustomerEmail = payment.CustomerEmail,
                PaymentDate = payment.PaymentDate,
                TotalAmountPaid = payment.Amount,
                BookingReference = payment.Booking!.BookingNumber,
                EventName = bookingSeat?.Seat?.Event?.Title ?? "Unknown Event"
            };
        }
    }
}
