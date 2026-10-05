using HotelBooking.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelBooking.Infrastructure;

public static class Seed
{
    private const string Img = "?auto=format&fit=crop&w=1200&q=70";

    public static async Task EnsureCreatedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Admin", "Owner", "Customer" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }

    public static async Task RunDemoDataAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        async Task<ApplicationUser> EnsureUser(string email, string password, string name, string role)
        {
            var u = await userManager.FindByEmailAsync(email);
            if (u is not null) return u;
            u = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, FullName = name, EmailConfirmed = true };
            var result = await userManager.CreateAsync(u, password);
            if (!result.Succeeded) throw new InvalidOperationException("Seeding user failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            await userManager.AddToRoleAsync(u, role);
            return u;
        }

        var owner = await EnsureUser("owner@hotel.local", "Owner123!", "Demo Owner", "Owner");
        await EnsureUser("admin@hotel.local", "Admin123!", "System Admin", "Admin");
        var customer = await EnsureUser("customer@hotel.local", "Customer123!", "Demo Customer", "Customer");

        if (await db.Hotels.AnyAsync()) return;

        var wifi = new Amenity { Name = "WiFi" };
        var ac = new Amenity { Name = "Air conditioning" };
        var breakfast = new Amenity { Name = "Breakfast" };
        var pool = new Amenity { Name = "Pool access" };
        var sea = new Amenity { Name = "Sea view" };
        var kitchen = new Amenity { Name = "Kitchen" };
        var nile = new Amenity { Name = "Nile view" };
        db.Amenities.AddRange(wifi, ac, breakfast, pool, sea, kitchen, nile);

        static Room R(string name, string desc, int cap, decimal price, params Amenity[] amenities)
        {
            var room = new Room { Name = name, Description = desc, Capacity = cap, PricePerNight = price };
            foreach (var a in amenities) room.Amenities.Add(new RoomAmenity { Amenity = a });
            return room;
        }

        Hotel H(string name, string desc, PropertyType type, string address, string city, string[] images, params Room[] rooms)
        {
            var hotel = new Hotel { Name = name, Description = desc, Type = type, Address = address, City = city, Country = "Egypt", OwnerId = owner.Id };
            foreach (var url in images) hotel.Images.Add(new HotelImage { Url = url + Img, CreatedAtUtc = DateTime.UtcNow.AddMilliseconds(hotel.Images.Count) });
            foreach (var r in rooms) hotel.Rooms.Add(r);
            return hotel;
        }

        var nileView = H("Nile View Hotel", "Quiet rooms on the Corniche with the river outside your window. Ten minutes by taxi from Tahrir Square and the Egyptian Museum.",
            PropertyType.Hotel, "Corniche El Nile", "Cairo",
            ["https://images.unsplash.com/photo-1566073771259-6a8506099945", "https://images.unsplash.com/photo-1551882547-ff40c63fe5fa"],
            R("Deluxe Room", "King bed, desk and a view over the city.", 2, 120, wifi, ac, breakfast),
            R("Family Suite", "Two bedrooms and a lounge, built for families.", 4, 220, wifi, ac, breakfast, nile));

        var breeze = H("Mediterranean Breeze Apartments", "Bright self-catering apartments a short walk from the Alexandria seafront and the Bibliotheca.",
            PropertyType.Apartment, "El Geish Road, Stanley", "Alexandria",
            ["https://images.unsplash.com/photo-1582719478250-c89cae4dc85b", "https://images.unsplash.com/photo-1542314831-068cd1dbfeeb"],
            R("Sea-view Studio", "Open-plan studio with a kitchenette and balcony.", 2, 65, wifi, ac, kitchen, sea),
            R("Two-bedroom Apartment", "Full kitchen, two bathrooms and room for five.", 5, 110, wifi, ac, kitchen));

        var coral = H("Coral Bay Resort", "Beachfront resort with a house reef, three pools and a dive centre on the Red Sea.",
            PropertyType.Resort, "Naama Bay", "Sharm El Sheikh",
            ["https://images.unsplash.com/photo-1520250497591-112f2f40a3f4", "https://images.unsplash.com/photo-1571896349842-33c89424de2d"],
            R("Garden Room", "Ground-floor room opening onto the gardens.", 2, 140, wifi, ac, breakfast, pool),
            R("Sea-view Suite", "Corner suite with a terrace over the bay.", 4, 260, wifi, ac, breakfast, pool, sea),
            R("Pool Villa", "Private pool, two bedrooms and a daily chef's breakfast.", 6, 480, wifi, ac, breakfast, pool, kitchen));

        var karnak = H("Karnak Courtyard Villa", "A restored villa around a shaded courtyard, five minutes from Karnak Temple.",
            PropertyType.Villa, "Karnak Street", "Luxor",
            ["https://images.unsplash.com/photo-1564501049412-61c2a3083791", "https://images.unsplash.com/photo-1566073771259-6a8506099945"],
            R("Courtyard Room", "Cool stone room facing the central courtyard.", 2, 85, wifi, ac),
            R("Garden Suite", "Suite with its own terrace and a daybed.", 3, 150, wifi, ac, breakfast));

        var elephantine = H("Elephantine Guesthouse", "Family-run guesthouse on the river with a rooftop for sunsets over the First Cataract.",
            PropertyType.Hotel, "Sharia El Corniche", "Aswan",
            ["https://images.unsplash.com/photo-1551882547-ff40c63fe5fa"],
            R("River Room", "Simple, clean room with a Nile view.", 2, 55, wifi, ac, nile),
            R("Rooftop Suite", "Top-floor suite with a private roof terrace.", 3, 95, wifi, ac, breakfast, nile));

        var hotels = new[] { nileView, breeze, coral, karnak, elephantine };
        db.Hotels.AddRange(hotels);

        // A few reviews so ratings aren't empty; the customer has a completed stay at Coral Bay and can still review it.
        void AddReview(Hotel hotel, int rating, string comment)
        {
            db.Reviews.Add(new Review { HotelId = hotel.Id, CustomerId = customer.Id, AuthorName = customer.FullName, Rating = rating, Comment = comment });
            hotel.Rating = rating;
            hotel.ReviewCount = 1;
        }
        AddReview(nileView, 5, "Spotless room, friendly staff and the breakfast on the terrace was the best part of the trip.");
        AddReview(breeze, 4, "Great location and a proper kitchen. The lift is slow but the balcony makes up for it.");
        AddReview(karnak, 5, "Peaceful courtyard and a short walk to the temple. We would come back.");
        AddReview(elephantine, 4, "Simple and welcoming. Watch the sunset from the roof.");

        var today = DateTime.UtcNow.Date;
        var pastRoom = coral.Rooms.First();
        db.Bookings.Add(new Booking
        {
            RoomId = pastRoom.Id, CustomerId = customer.Id, CheckIn = today.AddDays(-20), CheckOut = today.AddDays(-17), Guests = 2,
            TotalAmount = 3 * pastRoom.PricePerNight, Status = BookingStatus.CheckedOut, PaymentStatus = PaymentStatus.Paid
        });
        var upcomingRoom = nileView.Rooms.First();
        db.Bookings.Add(new Booking
        {
            RoomId = upcomingRoom.Id, CustomerId = customer.Id, CheckIn = today.AddDays(14), CheckOut = today.AddDays(17), Guests = 2,
            TotalAmount = 3 * upcomingRoom.PricePerNight, Status = BookingStatus.Confirmed, PaymentStatus = PaymentStatus.Paid
        });

        await db.SaveChangesAsync();
    }
}
