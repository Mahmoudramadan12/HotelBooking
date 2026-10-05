using HotelBooking.Domain;

namespace HotelBooking.Application;

// ---------- Errors (mapped to HTTP status codes by ExceptionMiddleware) ----------
public class NotFoundException(string message) : Exception(message);      // 404
public class ConflictException(string message) : Exception(message);      // 409
public class ForbiddenException(string message) : Exception(message);     // 403

public record Actor(Guid Id, bool IsAdmin);

// ---------- Auth ----------
public record RegisterRequest(string Email, string Password, string FullName, string Role = "Customer");
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, DateTime ExpiresAtUtc, string UserId, string Email, string FullName, string Role);

// ---------- Hotels & rooms ----------
public record CreateHotelRequest(string Name, string Description, PropertyType Type, string Address, string City, string Country, string? ImageUrl = null);
public record UpdateHotelRequest(string Name, string Description, PropertyType Type, string Address, string City, string Country, bool IsActive);
public record AddImageRequest(string Url);

public record HotelResponse(Guid Id, string Name, string Description, PropertyType Type, string Address, string City, string Country,
    decimal Rating, int ReviewCount, decimal? MinPrice, string? CoverImage, bool IsActive);

public record HotelDetailResponse(Guid Id, string Name, string Description, PropertyType Type, string Address, string City, string Country,
    decimal Rating, int ReviewCount, bool IsActive, bool CanManage, IReadOnlyList<string> Images, IReadOnlyList<RoomResponse> Rooms);

public record AmenityResponse(Guid Id, string Name);

public record CreateRoomRequest(string Name, string Description, int Capacity, decimal PricePerNight, IReadOnlyList<Guid>? AmenityIds = null);
public record UpdateRoomRequest(string Name, string Description, int Capacity, decimal PricePerNight, bool IsActive, IReadOnlyList<Guid>? AmenityIds = null);

/// <param name="Available">null when no dates were supplied, otherwise whether the room is free for them.</param>
public record RoomResponse(Guid Id, Guid HotelId, string Name, string Description, int Capacity, decimal PricePerNight, bool IsActive,
    IReadOnlyList<string> Amenities, bool? Available);

public record SearchRequest(string? City, string? Query, DateTime? CheckIn, DateTime? CheckOut, int Guests = 1, decimal? MaxPrice = null,
    PropertyType? Type = null, string? Sort = null, int Page = 1, int PageSize = 12);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

// ---------- Bookings ----------
public record CreateBookingRequest(Guid RoomId, DateTime CheckIn, DateTime CheckOut, int Guests, string? Notes);

public record BookingResponse(Guid Id, Guid HotelId, string HotelName, Guid RoomId, string RoomName, Guid CustomerId,
    string? CustomerName, string? CustomerEmail, DateTime CheckIn, DateTime CheckOut, int Nights, int Guests,
    decimal TotalAmount, BookingStatus Status, PaymentStatus PaymentStatus, string? Notes, DateTime CreatedAtUtc);

// ---------- Reviews ----------
public record CreateReviewRequest(Guid HotelId, int Rating, string Comment);
public record ReviewResponse(Guid Id, Guid HotelId, Guid CustomerId, string AuthorName, int Rating, string Comment, DateTime CreatedAtUtc);

// ---------- Service interfaces ----------
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
}

public interface IHotelService
{
    Task<PagedResult<HotelResponse>> SearchAsync(SearchRequest request, CancellationToken ct);
    Task<HotelDetailResponse> GetAsync(Guid id, DateTime? checkIn, DateTime? checkOut, Actor? viewer, CancellationToken ct);
    Task<IReadOnlyList<HotelResponse>> ManagedAsync(Actor actor, CancellationToken ct);
    Task<HotelResponse> CreateAsync(Guid ownerId, CreateHotelRequest request, CancellationToken ct);
    Task<HotelResponse> UpdateAsync(Actor actor, Guid hotelId, UpdateHotelRequest request, CancellationToken ct);
    Task<RoomResponse> AddRoomAsync(Actor actor, Guid hotelId, CreateRoomRequest request, CancellationToken ct);
    Task<RoomResponse> UpdateRoomAsync(Actor actor, Guid hotelId, Guid roomId, UpdateRoomRequest request, CancellationToken ct);
    Task AddImageAsync(Actor actor, Guid hotelId, string url, CancellationToken ct);
    Task<IReadOnlyList<AmenityResponse>> AmenitiesAsync(CancellationToken ct);
}

public interface IBookingService
{
    Task<BookingResponse> CreateAsync(Guid customerId, CreateBookingRequest request, CancellationToken ct);
    Task<IReadOnlyList<BookingResponse>> MineAsync(Guid customerId, CancellationToken ct);
    Task<IReadOnlyList<BookingResponse>> ManagedAsync(Actor actor, CancellationToken ct);
    Task CancelAsync(Guid customerId, Guid bookingId, CancellationToken ct);
    Task CheckInAsync(Actor actor, Guid bookingId, CancellationToken ct);
    Task CheckOutAsync(Actor actor, Guid bookingId, CancellationToken ct);
}

public interface IReviewService
{
    Task<IReadOnlyList<ReviewResponse>> ListAsync(Guid hotelId, CancellationToken ct);
    Task<ReviewResponse> CreateAsync(Guid customerId, string authorName, CreateReviewRequest request, CancellationToken ct);
}

public interface IEmailService { Task SendAsync(string to, string subject, string body); }
