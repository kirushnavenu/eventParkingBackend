using EventParking.API.Data;
using EventParking.API.DTOs;
using EventParking.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EventParking.API.Services
{
    public class BookingService
    {
        private readonly AppDbContext _context;
        private readonly NotificationService _notificationService;
        private readonly int _holdDurationMinutes;

        public BookingService(AppDbContext context, IConfiguration config, NotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
            _holdDurationMinutes = config.GetValue<int>("BookingSettings:HoldDurationMinutes", 15);
        }

        public async Task<Booking> CreateBookingAsync(string customerEmail, CreateUnifiedBookingDto dto)
        {
            if (dto.SeatIds == null || dto.SeatIds.Count == 0)
                throw new Exception("A booking must contain at least one seat.");

            if (dto.SeatIds.Distinct().Count() != dto.SeatIds.Count)
                throw new Exception("The same seat cannot be selected more than once.");

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var seats = await _context.Seats
                    .Where(s => dto.SeatIds.Contains(s.Id))
                    .ToListAsync();

                if (seats.Count != dto.SeatIds.Count)
                    throw new Exception("Some seats were not found.");

                var eventIds = seats.Select(s => s.EventId).Distinct().ToList();
                if (eventIds.Count != 1)
                    throw new Exception("All selected seats must belong to the same event.");

                if (seats.Any(s => s.Status != "Available"))
                    throw new Exception("One or more seats are no longer available.");

                var eventId = eventIds[0];
                decimal totalPrice = seats.Sum(s => s.Price);

                ParkingSlot? parkingSlot = null;
                if (dto.ParkingSlotId.HasValue)
                {
                    parkingSlot = await _context.ParkingSlots
                        .FirstOrDefaultAsync(p => p.Id == dto.ParkingSlotId.Value);

                    if (parkingSlot == null)
                        throw new Exception("The selected parking slot was not found.");

                    if (parkingSlot.EventId != eventId)
                        throw new Exception("The selected parking slot does not belong to this event.");

                    if (parkingSlot.Status != "Available")
                        throw new Exception("The selected parking slot is no longer available.");

                    totalPrice += parkingSlot.Fee;
                }

                var booking = new Booking
                {
                    BookingNumber = $"BKG-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
                    CustomerEmail = customerEmail,
                    TotalPrice = totalPrice,
                    Status = "Pending",
                    HoldExpiresAt = DateTime.UtcNow.AddMinutes(_holdDurationMinutes)
                };

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();

                foreach (var seat in seats)
                {
                    seat.Status = "Booked";
                    _context.BookingSeats.Add(new BookingSeat
                    {
                        BookingId = booking.Id,
                        SeatId = seat.Id
                    });
                }

                if (parkingSlot != null)
                {
                    parkingSlot.Status = "Reserved";
                    _context.ParkingReservations.Add(new ParkingReservation
                    {
                        BookingId = booking.Id,
                        ParkingSlotId = parkingSlot.Id,
                        FeeAtReservation = parkingSlot.Fee
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var customer = await _context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Email == customerEmail);

                if (customer != null)
                {
                    await _notificationService.CreateNotificationAsync(
                        customer.Id,
                        $"Booking {booking.BookingNumber} was created. Complete payment before the hold expires.");
                }

                return booking;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task CancelBookingAsync(int bookingId, string requestedByEmail, bool isAdmin = false)
        {
            var booking = await _context.Bookings.FindAsync(bookingId);
            if (booking == null) throw new Exception("Booking not found.");

            if (!isAdmin && !string.Equals(booking.CustomerEmail, requestedByEmail, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("You do not have permission to cancel this booking.");

            if (booking.Status is "Cancelled" or "Expired")
                throw new Exception("Booking is already inactive.");

            booking.Status = "Cancelled";

            var bookingSeats = await _context.BookingSeats
                .Include(bs => bs.Seat)
                .Where(bs => bs.BookingId == bookingId)
                .ToListAsync();

            foreach (var bs in bookingSeats)
            {
                if (bs.Seat != null) bs.Seat.Status = "Available";
            }

            var parkingRes = await _context.ParkingReservations
                .Include(pr => pr.ParkingSlot)
                .FirstOrDefaultAsync(pr => pr.BookingId == bookingId);

            if (parkingRes?.ParkingSlot != null)
                parkingRes.ParkingSlot.Status = "Available";

            await _context.SaveChangesAsync();

            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Email == booking.CustomerEmail);

            if (customer != null)
            {
                await _notificationService.CreateNotificationAsync(
                    customer.Id,
                    $"Booking {booking.BookingNumber} was cancelled. Its seats and parking were released.");
            }
        }

        public async Task ConfirmPaymentAsync(int bookingId)
        {
            var booking = await _context.Bookings.FindAsync(bookingId);
            if (booking == null || booking.Status != "Pending")
                throw new Exception("Invalid booking for payment.");

            if (DateTime.UtcNow > booking.HoldExpiresAt)
                throw new Exception("Hold period expired. Please create a new booking.");

            booking.Status = "Confirmed";
            await _context.SaveChangesAsync();
        }

        public async Task<List<CustomerBookingDto>> GetCustomerBookingsAsync(string customerEmail)
        {
            var ids = await _context.Bookings
                .Where(b => b.CustomerEmail == customerEmail)
                .OrderByDescending(b => b.BookingDate)
                .Select(b => b.Id)
                .ToListAsync();

            return await BuildBookingDtosAsync(ids);
        }

        public async Task<CustomerBookingDto> GetBookingAsync(int bookingId, string requestedByEmail, bool isAdmin)
        {
            var booking = await _context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null) throw new KeyNotFoundException("Booking not found.");

            if (!isAdmin && !string.Equals(booking.CustomerEmail, requestedByEmail, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("You do not have permission to view this booking.");

            var result = (await BuildBookingDtosAsync(new List<int> { bookingId })).FirstOrDefault();
            return result ?? throw new KeyNotFoundException("Booking not found.");
        }

        public async Task<List<CustomerBookingDto>> GetAdminBookingsAsync(int? eventId, string? status, string? search)
        {
            var query = _context.Bookings.AsNoTracking().AsQueryable();

            if (eventId.HasValue)
            {
                query = query.Where(b => _context.BookingSeats.Any(bs =>
                    bs.BookingId == b.Id && bs.Seat != null && bs.Seat.EventId == eventId.Value));
            }

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(b => b.Status == status);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(b => b.BookingNumber.Contains(term) || b.CustomerEmail.Contains(term));
            }

            var ids = await query
                .OrderByDescending(b => b.BookingDate)
                .Select(b => b.Id)
                .ToListAsync();

            return await BuildBookingDtosAsync(ids);
        }

        private async Task<List<CustomerBookingDto>> BuildBookingDtosAsync(IReadOnlyCollection<int> bookingIds)
        {
            if (bookingIds.Count == 0) return new List<CustomerBookingDto>();

            var bookings = await _context.Bookings
                .AsNoTracking()
                .Where(b => bookingIds.Contains(b.Id))
                .ToListAsync();

            var bookingSeats = await _context.BookingSeats
                .AsNoTracking()
                .Include(bs => bs.Seat)
                .ThenInclude(s => s!.Event)
                .Where(bs => bookingIds.Contains(bs.BookingId))
                .ToListAsync();

            var parking = await _context.ParkingReservations
                .AsNoTracking()
                .Include(pr => pr.ParkingSlot)
                .Where(pr => bookingIds.Contains(pr.BookingId))
                .ToListAsync();

            return bookings
                .OrderByDescending(b => b.BookingDate)
                .Select(b =>
                {
                    var seatRows = bookingSeats.Where(bs => bs.BookingId == b.Id).ToList();
                    var firstSeat = seatRows.FirstOrDefault()?.Seat;
                    var parkingRow = parking.FirstOrDefault(pr => pr.BookingId == b.Id);

                    return new CustomerBookingDto
                    {
                        Id = b.Id,
                        BookingNumber = b.BookingNumber,
                        EventId = firstSeat?.EventId ?? 0,
                        EventName = firstSeat?.Event?.Title ?? "Unknown",
                        EventDate = firstSeat?.Event?.EventDate ?? default,
                        CustomerEmail = b.CustomerEmail,
                        BookingDate = b.BookingDate,
                        HoldExpiresAt = b.HoldExpiresAt,
                        TotalPrice = b.TotalPrice,
                        Status = b.Status,
                        SeatNumbers = seatRows
                            .Where(x => x.Seat != null)
                            .Select(x => x.Seat!.SeatNumber.ToString())
                            .ToList(),
                        ParkingDetails = parkingRow?.ParkingSlot == null
                            ? "None"
                            : $"Zone {parkingRow.ParkingSlot.Zone} - Slot {parkingRow.ParkingSlot.SlotNumber}"
                    };
                })
                .ToList();
        }
    }
}
