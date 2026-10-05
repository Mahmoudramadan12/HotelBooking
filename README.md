# HotelBooking

A complete ASP.NET Core 8 Web API training project for hotel/apartment reservations.

## Included
- Clean Architecture style: API / Application / Domain / Infrastructure
- ASP.NET Core Identity + JWT
- Roles: Admin, Owner, Customer
- Hotels, rooms, amenities, images
- Search/filter/sort/pagination
- Availability checking
- Booking creation/cancellation/check-in/check-out
- Transaction + overlap protection
- Reviews and ratings
- Global exception handling
- FluentValidation
- Serilog
- Redis-ready caching
- Hangfire background jobs
- Email abstraction + development logging provider
- Audit logs
- Dashboard/report endpoints
- Unit tests
- Integration test skeleton
- Docker + docker-compose
- Swagger
- EF Core SQL Server
- Seed data

## Requirements
- .NET 8 SDK
- Docker Desktop (recommended)
- SQL Server if running without Docker

## Run with Docker
```bash
docker compose up --build
```

API:
http://localhost:8080/swagger

Default seeded accounts:
- admin@hotel.local / Admin123!
- owner@hotel.local / Owner123!
- customer@hotel.local / Customer123!

The development email provider logs emails to the application console.

## Run locally
```bash
dotnet restore
dotnet build
dotnet run --project src/HotelBooking.API
```

Set `ConnectionStrings:DefaultConnection` in appsettings.json.

## Notes
This is intentionally structured as a realistic learning project. Payment is represented by a mock payment service, so no real money is charged.
