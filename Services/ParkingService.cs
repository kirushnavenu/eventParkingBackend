using EventParking.API.Data;
using EventParking.API.DTOs;
using EventParking.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EventParking.API.Services
{
    public class ParkingService
    {
        private readonly AppDbContext _context;

        public ParkingService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<ParkingSlotDto>> GetSlotsByEventAsync(int eventId)
        {
            return await _context.ParkingSlots
                .Where(p => p.EventId == eventId)
                .OrderBy(p => p.Zone).ThenBy(p => p.SlotNumber)
                .Select(p => new ParkingSlotDto
                {
                    Id = p.Id,
                    EventId = p.EventId,
                    Zone = p.Zone,
                    SlotNumber = p.SlotNumber,
                    Fee = p.Fee,
                    Status = p.Status
                }).ToListAsync();
        }

        public async Task GenerateLayoutAsync(int eventId, GenerateParkingLayoutDto dto)
        {
            var ev = await _context.Events.FindAsync(eventId);
            if (ev == null) throw new Exception("Event not found.");
            if (dto.NumberOfSlots <= 0) throw new Exception("Number of parking slots must be greater than zero.");
            if (dto.DefaultFee < 0) throw new Exception("Parking fee cannot be negative.");
            if (string.IsNullOrWhiteSpace(dto.Zone)) throw new Exception("Parking zone is required.");

            var nextNumber = (await _context.ParkingSlots
                .Where(p => p.EventId == eventId && p.Zone == dto.Zone)
                .MaxAsync(p => (int?)p.SlotNumber) ?? 0) + 1;

            for (int i = 0; i < dto.NumberOfSlots; i++)
            {
                _context.ParkingSlots.Add(new ParkingSlot
                {
                    EventId = eventId,
                    Zone = dto.Zone.Trim(),
                    SlotNumber = nextNumber + i,
                    Fee = dto.DefaultFee,
                    Status = "Available"
                });
            }
            await _context.SaveChangesAsync();
        }

        public async Task UpdateSlotAsync(int slotId, UpdateParkingSlotDto dto)
        {
            var slot = await _context.ParkingSlots.FindAsync(slotId);
            if (slot == null) throw new Exception("Slot not found.");

            var isReserved = await _context.ParkingReservations.AnyAsync(r => r.ParkingSlotId == slotId && r.Booking != null && r.Booking.Status != "Cancelled" && r.Booking.Status != "Expired");
            if (isReserved) throw new Exception("Cannot edit a slot that is already reserved.");
            if (dto.Fee < 0) throw new Exception("Parking fee cannot be negative.");

            slot.Zone = dto.Zone;
            slot.SlotNumber = dto.SlotNumber;
            slot.Fee = dto.Fee;
            await _context.SaveChangesAsync();
        }

        public async Task DeleteSlotAsync(int slotId)
        {
            var slot = await _context.ParkingSlots.FindAsync(slotId);
            if (slot == null) throw new Exception("Slot not found.");

            var isReserved = await _context.ParkingReservations.AnyAsync(r => r.ParkingSlotId == slotId && r.Booking != null && r.Booking.Status != "Cancelled" && r.Booking.Status != "Expired");
            if (isReserved) throw new Exception("Cannot delete a slot with an active reservation.");

            _context.ParkingSlots.Remove(slot);
            await _context.SaveChangesAsync();
        }

        public async Task ReserveParkingAsync(int bookingId, int slotId, string requestedByEmail, bool isAdmin)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var booking = await _context.Bookings.FindAsync(bookingId);
                if (booking == null) throw new Exception("Booking not found.");
                if (!isAdmin && !string.Equals(booking.CustomerEmail, requestedByEmail, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("You do not own this booking.");
                if (booking.Status != "Pending")
                    throw new Exception("Parking can only be changed while the booking is pending.");

                var hasParking = await _context.ParkingReservations.AnyAsync(r => r.BookingId == bookingId);
                if (hasParking) throw new Exception("Booking already has a parking slot. Only one slot per booking is allowed.");

                var slot = await _context.ParkingSlots.FindAsync(slotId);
                if (slot == null) throw new Exception("Parking slot not found.");
                if (slot.Status != "Available") throw new Exception("Slot is already reserved.");

                var eventId = await _context.BookingSeats
                    .Where(bs => bs.BookingId == bookingId)
                    .Select(bs => bs.Seat!.EventId)
                    .FirstOrDefaultAsync();

                if (eventId == 0 || slot.EventId != eventId)
                    throw new Exception("Parking slot must belong to the booking event.");

                slot.Status = "Reserved";
                booking.TotalPrice += slot.Fee;
                _context.ParkingReservations.Add(new ParkingReservation
                {
                    BookingId = bookingId,
                    ParkingSlotId = slotId,
                    FeeAtReservation = slot.Fee
                });

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task RemoveParkingReservationAsync(int bookingId, string requestedByEmail, bool isAdmin)
        {
            var reservation = await _context.ParkingReservations
                .Include(r => r.ParkingSlot)
                .Include(r => r.Booking)
                .FirstOrDefaultAsync(r => r.BookingId == bookingId);

            if (reservation == null) throw new Exception("No parking reservation found for this booking.");
            if (reservation.Booking == null) throw new Exception("Booking not found.");
            if (!isAdmin && !string.Equals(reservation.Booking.CustomerEmail, requestedByEmail, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("You do not own this booking.");
            if (reservation.Booking.Status != "Pending")
                throw new Exception("Parking can only be changed while the booking is pending.");

            reservation.Booking.TotalPrice -= reservation.FeeAtReservation;
            if (reservation.ParkingSlot != null) reservation.ParkingSlot.Status = "Available";
            _context.ParkingReservations.Remove(reservation);
            await _context.SaveChangesAsync();
        }
    }
}
