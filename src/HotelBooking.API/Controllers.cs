using HotelBooking.Application;
using HotelBooking.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HotelBooking.API.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public Task<AuthResponse> Register(RegisterRequest r) => auth.RegisterAsync(r);

    [HttpPost("login")]
    public Task<AuthResponse> Login(LoginRequest r) => auth.LoginAsync(r);
}

[ApiController]
[Route("api/hotels")]
public class HotelsController(IHotelService hotels, IReviewService reviews) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<HotelResponse>> Search(
        [FromQuery] string? city, [FromQuery] string? q,
        [FromQuery] DateTime? checkIn, [FromQuery] DateTime? checkOut,
        [FromQuery] int guests = 1, [FromQuery] decimal? maxPrice = null,
        [FromQuery] PropertyType? type = null, [FromQuery] string? sort = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12,
        CancellationToken ct = default) =>
        hotels.SearchAsync(new SearchRequest(city, q, checkIn, checkOut, guests, maxPrice, type, sort, page, pageSize), ct);

    [Authorize(Roles = "Owner,Admin")]
    [HttpGet("mine")]
    public Task<IReadOnlyList<HotelResponse>> Mine(CancellationToken ct) => hotels.ManagedAsync(User.ToActor(), ct);

    [HttpGet("{id:guid}")]
    public Task<HotelDetailResponse> Get(Guid id, [FromQuery] DateTime? checkIn, [FromQuery] DateTime? checkOut, CancellationToken ct) =>
        hotels.GetAsync(id, checkIn, checkOut, User.ToActorOrNull(), ct);

    [HttpGet("{id:guid}/reviews")]
    public Task<IReadOnlyList<ReviewResponse>> Reviews(Guid id, CancellationToken ct) => reviews.ListAsync(id, ct);

    [Authorize(Roles = "Owner,Admin")]
    [HttpPost]
    public async Task<ActionResult<HotelResponse>> Create(CreateHotelRequest r, CancellationToken ct)
    {
        var created = await hotels.CreateAsync(User.GetUserId(), r, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Roles = "Owner,Admin")]
    [HttpPut("{id:guid}")]
    public Task<HotelResponse> Update(Guid id, UpdateHotelRequest r, CancellationToken ct) => hotels.UpdateAsync(User.ToActor(), id, r, ct);

    [Authorize(Roles = "Owner,Admin")]
    [HttpPost("{hotelId:guid}/rooms")]
    public async Task<ActionResult<RoomResponse>> AddRoom(Guid hotelId, CreateRoomRequest r, CancellationToken ct) =>
        Ok(await hotels.AddRoomAsync(User.ToActor(), hotelId, r, ct));

    [Authorize(Roles = "Owner,Admin")]
    [HttpPut("{hotelId:guid}/rooms/{roomId:guid}")]
    public Task<RoomResponse> UpdateRoom(Guid hotelId, Guid roomId, UpdateRoomRequest r, CancellationToken ct) =>
        hotels.UpdateRoomAsync(User.ToActor(), hotelId, roomId, r, ct);

    [Authorize(Roles = "Owner,Admin")]
    [HttpPost("{hotelId:guid}/images")]
    public async Task<IActionResult> AddImage(Guid hotelId, AddImageRequest r, CancellationToken ct)
    {
        await hotels.AddImageAsync(User.ToActor(), hotelId, r.Url, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/amenities")]
public class AmenitiesController(IHotelService hotels) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AmenityResponse>> List(CancellationToken ct) => hotels.AmenitiesAsync(ct);
}

[ApiController]
[Authorize]
[Route("api/bookings")]
public class BookingsController(IBookingService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest r, CancellationToken ct) =>
        Ok(await service.CreateAsync(User.GetUserId(), r, ct));

    [HttpGet("mine")]
    public Task<IReadOnlyList<BookingResponse>> Mine(CancellationToken ct) => service.MineAsync(User.GetUserId(), ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await service.CancelAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize(Roles = "Admin,Owner")]
[Route("api/admin/bookings")]
public class AdminBookingsController(IBookingService service) : ControllerBase
{
    /// <summary>Reservations for the owner's properties (everything for admins).</summary>
    [HttpGet]
    public Task<IReadOnlyList<BookingResponse>> List(CancellationToken ct) => service.ManagedAsync(User.ToActor(), ct);

    [HttpPost("{id:guid}/check-in")]
    public async Task<IActionResult> CheckIn(Guid id, CancellationToken ct)
    {
        await service.CheckInAsync(User.ToActor(), id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/check-out")]
    public async Task<IActionResult> CheckOut(Guid id, CancellationToken ct)
    {
        await service.CheckOutAsync(User.ToActor(), id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/reviews")]
public class ReviewsController(IReviewService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ReviewResponse>> Create(CreateReviewRequest r, CancellationToken ct) =>
        Ok(await service.CreateAsync(User.GetUserId(), User.GetFullName(), r, ct));
}
