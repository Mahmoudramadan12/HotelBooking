namespace HotelBooking.Domain;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

public enum BookingStatus { Pending, Confirmed, Cancelled, CheckedIn, CheckedOut }
public enum PaymentStatus { Pending, Paid, Refunded, Failed }
public enum PropertyType { Hotel, Apartment, Villa, Resort }

public class Hotel : BaseEntity
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public PropertyType Type { get; set; } = PropertyType.Hotel;
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";

    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }

    public Guid OwnerId { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<HotelImage> Images { get; set; } = new List<HotelImage>();
}

public class Room : BaseEntity
{
    public Guid HotelId { get; set; }
    public Hotel? Hotel { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Capacity { get; set; }
    public decimal PricePerNight { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<RoomAmenity> Amenities { get; set; } = new List<RoomAmenity>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

public class HotelImage : BaseEntity
{
    public Guid HotelId { get; set; }
    public Hotel? Hotel { get; set; }
    public string Url { get; set; } = "";
}

public class Amenity : BaseEntity
{
    public string Name { get; set; } = "";
    public ICollection<RoomAmenity> Rooms { get; set; } = new List<RoomAmenity>();
}

public class RoomAmenity
{
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }
    public Guid AmenityId { get; set; }
    public Amenity? Amenity { get; set; }
}

public class Booking : BaseEntity
{
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }
    public Guid CustomerId { get; set; }

    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }
    public int Guests { get; set; }
    public decimal TotalAmount { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? Notes { get; set; }
}

public class Review : BaseEntity
{
    public Guid HotelId { get; set; }
    public Hotel? Hotel { get; set; }
    public Guid CustomerId { get; set; }

    public string AuthorName { get; set; } = "";
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
}

public class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Details { get; set; }
}
