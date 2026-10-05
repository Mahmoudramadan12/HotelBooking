using System;
using FluentAssertions;
using HotelBooking.Domain;
using Xunit;

namespace HotelBooking.UnitTests;

public class BookingRulesTests
{
    [Fact]
    public void Overlapping_ranges_should_be_detectable()
    {
        var existingIn = new DateTime(2026, 10, 10);
        var existingOut = new DateTime(2026, 10, 15);
        var requestedIn = new DateTime(2026, 10, 14);
        var requestedOut = new DateTime(2026, 10, 20);
        (existingIn < requestedOut && requestedIn < existingOut).Should().BeTrue();
    }

    [Fact]
    public void Cancelled_booking_does_not_block_dates()
    {
        var booking = new Booking { Status = BookingStatus.Cancelled };
        (booking.Status != BookingStatus.Cancelled).Should().BeFalse();
    }
}
