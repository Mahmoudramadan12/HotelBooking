using HotelBooking.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HotelBooking.Infrastructure;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";
}

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Hotel> Hotels => Set<Hotel>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<HotelImage> HotelImages => Set<HotelImage>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<RoomAmenity> RoomAmenities => Set<RoomAmenity>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ApplicationUser>().Property(x => x.FullName).HasMaxLength(100);

        b.Entity<RoomAmenity>().HasKey(x => new { x.RoomId, x.AmenityId });

        b.Entity<Hotel>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Country).HasMaxLength(100);
            e.Property(x => x.Rating).HasPrecision(3, 2);
            e.HasIndex(x => x.City);
            e.HasMany(x => x.Rooms).WithOne(x => x.Hotel).HasForeignKey(x => x.HotelId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<HotelImage>().Property(x => x.Url).HasMaxLength(1000);
        b.Entity<Amenity>().Property(x => x.Name).HasMaxLength(100);

        b.Entity<Room>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.PricePerNight).HasPrecision(18, 2);
            e.HasMany(x => x.Bookings).WithOne(x => x.Room).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Booking>(e =>
        {
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(500);
            // Speeds up the overlap check used for availability.
            e.HasIndex(x => new { x.RoomId, x.CheckIn, x.CheckOut });
            e.HasIndex(x => x.CustomerId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Review>(e =>
        {
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.Property(x => x.AuthorName).HasMaxLength(100);
            e.HasIndex(x => new { x.HotelId, x.CustomerId }).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.EntityName).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(64);
            e.Property(x => x.Details).HasMaxLength(1000);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>().Where(e => e.State == EntityState.Modified))
            entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
        return base.SaveChangesAsync(cancellationToken);
    }
}
