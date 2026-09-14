using EventParking.API.Data;
using Microsoft.EntityFrameworkCore;

namespace EventParking.API.Workers
{
    public class BookingExpirationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BookingExpirationWorker> _logger;

        public BookingExpirationWorker(
            IServiceProvider serviceProvider,
            ILogger<BookingExpirationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope =
                        _serviceProvider.CreateScope();

                    var context =
                        scope.ServiceProvider
                            .GetRequiredService<AppDbContext>();

                    var expiredBookings =
                        await context.Bookings
                            .Where(b =>
                                b.Status == "Pending" &&
                                b.HoldExpiresAt <= DateTime.UtcNow)
                            .ToListAsync(stoppingToken);

                    foreach (var booking in expiredBookings)
                    {
                        booking.Status = "Expired";

                        var seatsToRelease =
                            await context.BookingSeats
                                .Include(bs => bs.Seat)
                                .Where(bs =>
                                    bs.BookingId == booking.Id)
                                .ToListAsync(stoppingToken);

                        foreach (var bookingSeat in seatsToRelease)
                        {
                            if (bookingSeat.Seat != null)
                            {
                                bookingSeat.Seat.Status = "Available";
                            }
                        }

                        var parkingToRelease =
                            await context.ParkingReservations
                                .Include(pr => pr.ParkingSlot)
                                .FirstOrDefaultAsync(
                                    pr => pr.BookingId == booking.Id,
                                    stoppingToken);

                        if (parkingToRelease?.ParkingSlot != null)
                        {
                            parkingToRelease.ParkingSlot.Status =
                                "Available";
                        }
                    }

                    if (expiredBookings.Count > 0)
                    {
                        await context.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Booking expiration worker failed. Retrying next cycle.");
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromMinutes(1),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
