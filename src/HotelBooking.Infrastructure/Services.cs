using HotelBooking.Application;
using HotelBooking.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace HotelBooking.Infrastructure;

internal static class Access
{
    public static void EnsureCanManage(Hotel hotel, Actor actor)
    {
        if (!actor.IsAdmin && hotel.OwnerId != actor.Id)
            throw new ForbiddenException("You can only manage your own properties.");
    }
}

// =====================================================================================
// Auth
// =====================================================================================
public class AuthService(UserManager<ApplicationUser> users, IConfiguration config, IEmailService mail, ILogger<AuthService> logger) : IAuthService
{
    private static readonly string[] RolePriority = ["Admin", "Owner", "Customer"];

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var address = request.Email.Trim();
        if (await users.FindByEmailAsync(address) is not null)
            throw new ConflictException("An account with this email already exists.");

        // Admin can never be self-assigned.
        var role = request.Role == "Owner" ? "Owner" : "Customer";
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = address, UserName = address, FullName = request.FullName.Trim() };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);

        try { await mail.SendAsync(address, "Welcome to Manzil", $"Hi {user.FullName}, your account is ready."); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send welcome email to {Email}", address); }

        return CreateToken(user, new[] { role });
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        const string invalid = "Email or password is incorrect.";
        var user = await users.FindByEmailAsync(request.Email.Trim()) ?? throw new UnauthorizedAccessException(invalid);

        if (await users.IsLockedOutAsync(user))
            throw new UnauthorizedAccessException("Too many failed attempts. Try again in a few minutes.");

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw new UnauthorizedAccessException(invalid);
        }

        await users.ResetAccessFailedCountAsync(user);
        var roles = await users.GetRolesAsync(user);
        return CreateToken(user, roles.Count > 0 ? roles.ToArray() : new[] { "Customer" });
    }

    private AuthResponse CreateToken(ApplicationUser user, IReadOnlyCollection<string> roles)
    {
        var jwt = config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
        var expires = DateTime.UtcNow.AddHours(8);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email!),
            new("full_name", user.FullName)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(jwt["Issuer"], jwt["Audience"], claims, expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var primary = RolePriority.FirstOrDefault(r => roles.Contains(r)) ?? "Customer";
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, user.Id.ToString(), user.Email!, user.FullName, primary);
    }
}

// =====================================================================================
// Hotels
// =====================================================================================
public class HotelService(ApplicationDbContext db) : IHotelService
{
    private static HotelResponse ToResponse(Hotel h, decimal? minPrice = null, string? cover = null) =>
        new(h.Id, h.Name, h.Description, h.Type, h.Address, h.City, h.Country, h.Rating, h.ReviewCount, minPrice, cover, h.IsActive);

    private static RoomResponse ToRoom(Room r, bool? available) =>
        new(r.Id, r.HotelId, r.Name, r.Description, r.Capacity, r.PricePerNight, r.IsActive,
            r.Amenities.Where(a => a.Amenity != null).Select(a => a.Amenity!.Name).OrderBy(n => n).ToList(), available);

    private static IQueryable<HotelResponse> Project(IQueryable<Hotel> q) => q.Select(h => new HotelResponse(
        h.Id, h.Name, h.Description, h.Type, h.Address, h.City, h.Country, h.Rating, h.ReviewCount,
        h.Rooms.Where(r => r.IsActive).Min(r => (decimal?)r.PricePerNight),
        h.Images.OrderBy(i => i.CreatedAtUtc).Select(i => i.Url).FirstOrDefault(),
        h.IsActive));

    public async Task<PagedResult<HotelResponse>> SearchAsync(SearchRequest r, CancellationToken ct)
    {
        var page = Math.Max(1, r.Page);
        var size = Math.Clamp(r.PageSize, 1, 50);
        var guests = Math.Max(1, r.Guests);
        decimal? maxPrice = r.MaxPrice;
        var hasDates = r.CheckIn.HasValue && r.CheckOut.HasValue && r.CheckOut.Value.Date > r.CheckIn.Value.Date;
        var ci = r.CheckIn?.Date ?? DateTime.MinValue;
        var co = r.CheckOut?.Date ?? DateTime.MinValue;

        var query = db.Hotels.AsNoTracking().Where(h => h.IsActive);

        if (!string.IsNullOrWhiteSpace(r.City))
        {
            var city = r.City.Trim();
            query = query.Where(h => h.City.Contains(city));
        }
        if (!string.IsNullOrWhiteSpace(r.Query))
        {
            var text = r.Query.Trim();
            query = query.Where(h => h.Name.Contains(text) || h.Description.Contains(text));
        }
        if (r.Type.HasValue)
        {
            var type = r.Type.Value;
            query = query.Where(h => h.Type == type);
        }

        // Only show properties that have at least one room that fits the guests, price and dates.
        query = query.Where(h => h.Rooms.Any(room => room.IsActive
            && room.Capacity >= guests
            && (maxPrice == null || room.PricePerNight <= maxPrice)
            && (!hasDates || !room.Bookings.Any(b => b.Status != BookingStatus.Cancelled && b.CheckIn < co && ci < b.CheckOut))));

        var total = await query.CountAsync(ct);

        IQueryable<Hotel> ordered = r.Sort switch
        {
            "price" => query.OrderBy(h => h.Rooms.Where(x => x.IsActive).Min(x => (decimal?)x.PricePerNight)),
            "name" => query.OrderBy(h => h.Name),
            _ => query.OrderByDescending(h => h.Rating).ThenByDescending(h => h.ReviewCount).ThenBy(h => h.Name)
        };

        var items = await Project(ordered.Skip((page - 1) * size).Take(size)).ToListAsync(ct);
        return new PagedResult<HotelResponse>(items, page, size, total);
    }

    public async Task<HotelDetailResponse> GetAsync(Guid id, DateTime? checkIn, DateTime? checkOut, Actor? viewer, CancellationToken ct)
    {
        var h = await db.Hotels.AsNoTracking()
            .Include(x => x.Images)
            .Include(x => x.Rooms).ThenInclude(r => r.Amenities).ThenInclude(a => a.Amenity)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Property not found.");

        var canManage = viewer is not null && (viewer.IsAdmin || h.OwnerId == viewer.Id);
        if (!h.IsActive && !canManage) throw new NotFoundException("Property not found.");

        var hasDates = checkIn.HasValue && checkOut.HasValue && checkOut.Value.Date > checkIn.Value.Date;
        var busy = new HashSet<Guid>();
        if (hasDates)
        {
            var ci = checkIn!.Value.Date;
            var co = checkOut!.Value.Date;
            var roomIds = h.Rooms.Select(r => r.Id).ToList();
            var taken = await db.Bookings
                .Where(b => roomIds.Contains(b.RoomId) && b.Status != BookingStatus.Cancelled && b.CheckIn < co && ci < b.CheckOut)
                .Select(b => b.RoomId).Distinct().ToListAsync(ct);
            busy = taken.ToHashSet();
        }

        var rooms = h.Rooms
            .Where(r => r.IsActive || canManage)
            .OrderBy(r => r.PricePerNight)
            .Select(r => ToRoom(r, hasDates ? !busy.Contains(r.Id) : null))
            .ToList();

        var images = h.Images.OrderBy(i => i.CreatedAtUtc).Select(i => i.Url).ToList();
        return new HotelDetailResponse(h.Id, h.Name, h.Description, h.Type, h.Address, h.City, h.Country, h.Rating, h.ReviewCount,
            h.IsActive, canManage, images, rooms);
    }

    public async Task<IReadOnlyList<HotelResponse>> ManagedAsync(Actor actor, CancellationToken ct)
    {
        var isAdmin = actor.IsAdmin;
        var id = actor.Id;
        return await Project(db.Hotels.AsNoTracking().Where(h => isAdmin || h.OwnerId == id).OrderBy(h => h.Name)).ToListAsync(ct);
    }

    public async Task<HotelResponse> CreateAsync(Guid ownerId, CreateHotelRequest r, CancellationToken ct)
    {
        var hotel = new Hotel
        {
            Name = r.Name.Trim(), Description = r.Description.Trim(), Type = r.Type, Address = r.Address.Trim(),
            City = r.City.Trim(), Country = r.Country.Trim(), OwnerId = ownerId
        };
        if (!string.IsNullOrWhiteSpace(r.ImageUrl)) hotel.Images.Add(new HotelImage { Url = r.ImageUrl.Trim() });
        db.Hotels.Add(hotel);
        await db.SaveChangesAsync(ct);
        return ToResponse(hotel, null, hotel.Images.FirstOrDefault()?.Url);
    }

    public async Task<HotelResponse> UpdateAsync(Actor actor, Guid hotelId, UpdateHotelRequest r, CancellationToken ct)
    {
        var hotel = await LoadManagedAsync(hotelId, actor, ct);
        hotel.Name = r.Name.Trim(); hotel.Description = r.Description.Trim(); hotel.Type = r.Type;
        hotel.Address = r.Address.Trim(); hotel.City = r.City.Trim(); hotel.Country = r.Country.Trim(); hotel.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        return ToResponse(hotel);
    }

    public async Task<RoomResponse> AddRoomAsync(Actor actor, Guid hotelId, CreateRoomRequest r, CancellationToken ct)
    {
        await LoadManagedAsync(hotelId, actor, ct);
        var room = new Room
        {
            HotelId = hotelId, Name = r.Name.Trim(), Description = r.Description?.Trim() ?? "",
            Capacity = r.Capacity, PricePerNight = r.PricePerNight
        };
        db.Rooms.Add(room);
        await ApplyAmenitiesAsync(room, r.AmenityIds, ct);
        await db.SaveChangesAsync(ct);
        return ToRoom(room, null);
    }

    public async Task<RoomResponse> UpdateRoomAsync(Actor actor, Guid hotelId, Guid roomId, UpdateRoomRequest r, CancellationToken ct)
    {
        await LoadManagedAsync(hotelId, actor, ct);
        var room = await db.Rooms.Include(x => x.Amenities).ThenInclude(a => a.Amenity)
            .FirstOrDefaultAsync(x => x.Id == roomId && x.HotelId == hotelId, ct) ?? throw new NotFoundException("Room not found.");

        room.Name = r.Name.Trim(); room.Description = r.Description?.Trim() ?? "";
        room.Capacity = r.Capacity; room.PricePerNight = r.PricePerNight; room.IsActive = r.IsActive;
        await ApplyAmenitiesAsync(room, r.AmenityIds, ct);
        await db.SaveChangesAsync(ct);
        return ToRoom(room, null);
    }

    public async Task AddImageAsync(Actor actor, Guid hotelId, string url, CancellationToken ct)
    {
        await LoadManagedAsync(hotelId, actor, ct);
        db.HotelImages.Add(new HotelImage { HotelId = hotelId, Url = url.Trim() });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AmenityResponse>> AmenitiesAsync(CancellationToken ct) =>
        await db.Amenities.AsNoTracking().OrderBy(a => a.Name).Select(a => new AmenityResponse(a.Id, a.Name)).ToListAsync(ct);

    private async Task<Hotel> LoadManagedAsync(Guid hotelId, Actor actor, CancellationToken ct)
    {
        var hotel = await db.Hotels.FirstOrDefaultAsync(h => h.Id == hotelId, ct) ?? throw new NotFoundException("Property not found.");
        Access.EnsureCanManage(hotel, actor);
        return hotel;
    }

    /// <summary>Makes the room's amenities match <paramref name="ids"/>. A null list leaves them untouched.</summary>
    private async Task ApplyAmenitiesAsync(Room room, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        if (ids is null) return;
        var idList = ids.ToList();
        var wanted = await db.Amenities.Where(a => idList.Contains(a.Id)).ToListAsync(ct);
        var wantedIds = wanted.Select(a => a.Id).ToHashSet();

        foreach (var existing in room.Amenities.Where(x => !wantedIds.Contains(x.AmenityId)).ToList())
            db.RoomAmenities.Remove(existing);

        var have = room.Amenities.Select(x => x.AmenityId).ToHashSet();
        foreach (var amenity in wanted.Where(a => !have.Contains(a.Id)))
            room.Amenities.Add(new RoomAmenity { Amenity = amenity });
    }
}

// =====================================================================================
// Bookings
// =====================================================================================
public class BookingService(ApplicationDbContext db, IEmailService mail, ILogger<BookingService> logger) : IBookingService
{
    public async Task<BookingResponse> CreateAsync(Guid customerId, CreateBookingRequest request, CancellationToken ct)
    {
        var checkIn = request.CheckIn.Date;
        var checkOut = request.CheckOut.Date;

        // One day of slack so guests ahead of UTC can still book "today".
        if (checkIn < DateTime.UtcNow.Date.AddDays(-1)) throw new InvalidOperationException("Check-in cannot be in the past.");
        var nights = (checkOut - checkIn).Days;
        if (nights < 1) throw new InvalidOperationException("Check-out must be after check-in.");
        if (nights > 30) throw new InvalidOperationException("Stays are limited to 30 nights.");

        var room = await db.Rooms.Include(x => x.Hotel)
            .FirstOrDefaultAsync(x => x.Id == request.RoomId && x.IsActive && x.Hotel!.IsActive, ct)
            ?? throw new NotFoundException("Room not found.");
        if (request.Guests > room.Capacity) throw new InvalidOperationException($"This room sleeps up to {room.Capacity} guests.");

        // Serializable transaction: two people can't book the same room for overlapping dates at the same time.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var taken = await db.Bookings.AnyAsync(b => b.RoomId == room.Id && b.Status != BookingStatus.Cancelled
            && b.CheckIn < checkOut && checkIn < b.CheckOut, ct);
        if (taken) throw new ConflictException("This room is already booked for the selected dates.");

        var booking = new Booking
        {
            Room = room, RoomId = room.Id, CustomerId = customerId, CheckIn = checkIn, CheckOut = checkOut,
            Guests = request.Guests, TotalAmount = nights * room.PricePerNight,
            // Payment gateway placeholder: bookings are confirmed and marked paid immediately.
            Status = BookingStatus.Confirmed, PaymentStatus = PaymentStatus.Paid,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        db.Bookings.Add(booking);
        Audit(customerId, "BookingCreated", booking, $"{room.Hotel!.Name} / {room.Name}, {checkIn:yyyy-MM-dd} to {checkOut:yyyy-MM-dd}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await TrySendConfirmationAsync(customerId, booking, room, ct);
        return Map(booking, null, null);
    }

    public async Task<IReadOnlyList<BookingResponse>> MineAsync(Guid customerId, CancellationToken ct)
    {
        var rows = await db.Bookings.AsNoTracking()
            .Include(b => b.Room).ThenInclude(r => r!.Hotel)
            .Where(b => b.CustomerId == customerId)
            .OrderByDescending(b => b.CheckIn).Take(200).ToListAsync(ct);
        return rows.Select(b => Map(b, null, null)).ToList();
    }

    public async Task<IReadOnlyList<BookingResponse>> ManagedAsync(Actor actor, CancellationToken ct)
    {
        var isAdmin = actor.IsAdmin;
        var id = actor.Id;
        var rows = await db.Bookings.AsNoTracking()
            .Include(b => b.Room).ThenInclude(r => r!.Hotel)
            .Where(b => isAdmin || b.Room!.Hotel!.OwnerId == id)
            .OrderByDescending(b => b.CreatedAtUtc).Take(300).ToListAsync(ct);

        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
        var people = await db.Users.AsNoTracking().Where(u => customerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email }).ToDictionaryAsync(u => u.Id, ct);

        return rows.Select(b => people.TryGetValue(b.CustomerId, out var p) ? Map(b, p.FullName, p.Email) : Map(b, null, null)).ToList();
    }

    public async Task CancelAsync(Guid customerId, Guid bookingId, CancellationToken ct)
    {
        var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == bookingId && x.CustomerId == customerId, ct)
            ?? throw new NotFoundException("Booking not found.");
        if (b.Status is BookingStatus.CheckedIn or BookingStatus.CheckedOut or BookingStatus.Cancelled)
            throw new InvalidOperationException("This booking can no longer be cancelled.");

        // Free cancellation until the day before arrival.
        var refundable = b.CheckIn.Date > DateTime.UtcNow.Date;
        b.Status = BookingStatus.Cancelled;
        if (b.PaymentStatus == PaymentStatus.Paid && refundable) b.PaymentStatus = PaymentStatus.Refunded;
        Audit(customerId, "BookingCancelled", b, refundable ? "Refunded" : "No refund (inside cancellation window)");
        await db.SaveChangesAsync(ct);
    }

    public async Task CheckInAsync(Actor actor, Guid bookingId, CancellationToken ct)
    {
        var b = await LoadForStaffAsync(bookingId, actor, ct);
        if (b.Status != BookingStatus.Confirmed) throw new InvalidOperationException("Only confirmed bookings can be checked in.");
        b.Status = BookingStatus.CheckedIn;
        Audit(actor.Id, "BookingCheckedIn", b, null);
        await db.SaveChangesAsync(ct);
    }

    public async Task CheckOutAsync(Actor actor, Guid bookingId, CancellationToken ct)
    {
        var b = await LoadForStaffAsync(bookingId, actor, ct);
        if (b.Status != BookingStatus.CheckedIn) throw new InvalidOperationException("This guest is not checked in.");
        b.Status = BookingStatus.CheckedOut;
        Audit(actor.Id, "BookingCheckedOut", b, null);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Booking> LoadForStaffAsync(Guid bookingId, Actor actor, CancellationToken ct)
    {
        var b = await db.Bookings.Include(x => x.Room).ThenInclude(r => r!.Hotel)
            .FirstOrDefaultAsync(x => x.Id == bookingId, ct) ?? throw new NotFoundException("Booking not found.");
        Access.EnsureCanManage(b.Room!.Hotel!, actor);
        return b;
    }

    private void Audit(Guid userId, string action, Booking b, string? details) =>
        db.AuditLogs.Add(new AuditLog { UserId = userId, Action = action, EntityName = nameof(Booking), EntityId = b.Id.ToString(), Details = details });

    private async Task TrySendConfirmationAsync(Guid customerId, Booking b, Room room, CancellationToken ct)
    {
        try
        {
            var address = await db.Users.Where(u => u.Id == customerId).Select(u => u.Email).FirstOrDefaultAsync(ct);
            if (address is null) return;
            await mail.SendAsync(address, $"Booking confirmed: {room.Hotel!.Name}",
                $"{room.Name}, {b.CheckIn:ddd d MMM yyyy} to {b.CheckOut:ddd d MMM yyyy}. Total {b.TotalAmount:0.00}.");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send booking confirmation for {BookingId}", b.Id); }
    }

    private static BookingResponse Map(Booking b, string? customerName, string? customerEmail) =>
        new(b.Id, b.Room!.HotelId, b.Room.Hotel!.Name, b.RoomId, b.Room.Name, b.CustomerId, customerName, customerEmail,
            b.CheckIn, b.CheckOut, (b.CheckOut.Date - b.CheckIn.Date).Days, b.Guests, b.TotalAmount, b.Status, b.PaymentStatus,
            b.Notes, b.CreatedAtUtc);
}

// =====================================================================================
// Reviews
// =====================================================================================
public class ReviewService(ApplicationDbContext db) : IReviewService
{
    public async Task<IReadOnlyList<ReviewResponse>> ListAsync(Guid hotelId, CancellationToken ct) =>
        await db.Reviews.AsNoTracking().Where(r => r.HotelId == hotelId)
            .OrderByDescending(r => r.CreatedAtUtc).Take(50)
            .Select(r => new ReviewResponse(r.Id, r.HotelId, r.CustomerId, r.AuthorName, r.Rating, r.Comment, r.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task<ReviewResponse> CreateAsync(Guid customerId, string authorName, CreateReviewRequest r, CancellationToken ct)
    {
        var hotel = await db.Hotels.FirstOrDefaultAsync(h => h.Id == r.HotelId, ct) ?? throw new NotFoundException("Property not found.");

        var stayed = await db.Bookings.AnyAsync(b => b.CustomerId == customerId && b.Room!.HotelId == r.HotelId
            && b.Status == BookingStatus.CheckedOut, ct);
        if (!stayed) throw new InvalidOperationException("Only guests with a completed stay can leave a review.");

        if (await db.Reviews.AnyAsync(x => x.HotelId == r.HotelId && x.CustomerId == customerId, ct))
            throw new ConflictException("You have already reviewed this property.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var review = new Review { HotelId = r.HotelId, CustomerId = customerId, AuthorName = authorName, Rating = r.Rating, Comment = r.Comment.Trim() };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);

        // Recompute from the database so the average is always exact, even with concurrent reviews.
        var all = db.Reviews.Where(x => x.HotelId == r.HotelId);
        hotel.ReviewCount = await all.CountAsync(ct);
        hotel.Rating = Math.Round(await all.AverageAsync(x => (decimal)x.Rating, ct), 2);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ReviewResponse(review.Id, review.HotelId, review.CustomerId, review.AuthorName, review.Rating, review.Comment, review.CreatedAtUtc);
    }
}

// =====================================================================================
// Email (development implementation writes to the console)
// =====================================================================================
public class DevEmailService : IEmailService
{
    public Task SendAsync(string to, string subject, string body)
    {
        Console.WriteLine($"EMAIL -> {to}: {subject}\n{body}");
        return Task.CompletedTask;
    }
}
