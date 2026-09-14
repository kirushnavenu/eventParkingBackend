using EventParking.API.Data;
using EventParking.API.Migrations.Interfaces;
using EventParking.API.Repositories;
using EventParking.API.Services;
using EventParking.API.Workers;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ======================================================
// CONFIGURATION VALIDATION
// ======================================================

// Connection String
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "DefaultConnection is missing in appsettings.json."
    );
}

// JWT
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt:Key is missing in appsettings.json."
    );
}

if (string.IsNullOrWhiteSpace(jwtIssuer))
{
    throw new InvalidOperationException(
        "Jwt:Issuer is missing in appsettings.json."
    );
}

if (string.IsNullOrWhiteSpace(jwtAudience))
{
    throw new InvalidOperationException(
        "Jwt:Audience is missing in appsettings.json."
    );
}

// Email Settings
var senderEmail =
    builder.Configuration["EmailSettings:SenderEmail"];

var appPassword =
    builder.Configuration["EmailSettings:AppPassword"];

var frontendUrl =
    builder.Configuration["EmailSettings:FrontendUrl"];

if (string.IsNullOrWhiteSpace(senderEmail))
{
    throw new InvalidOperationException(
        "EmailSettings:SenderEmail is missing."
    );
}

if (string.IsNullOrWhiteSpace(appPassword))
{
    throw new InvalidOperationException(
        "EmailSettings:AppPassword is missing."
    );
}

if (string.IsNullOrWhiteSpace(frontendUrl))
{
    throw new InvalidOperationException(
        "EmailSettings:FrontendUrl is missing."
    );
}

// ======================================================
// DATABASE
// ======================================================

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// ======================================================
// REPOSITORIES
// ======================================================

builder.Services.AddScoped<
    ICustomerRepository,
    CustomerRepository
>();

// ======================================================
// SERVICES
// ======================================================

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<SeatService>();
builder.Services.AddScoped<VenueService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<ParkingService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<NotificationService>();

// Gmail SMTP Email Service
builder.Services.AddScoped<
    IEmailService,
    EmailService
>();

// ======================================================
// BACKGROUND SERVICE
// ======================================================

//builder.Services.AddHostedService<
//    BookingExpirationWorker
//>();

// ======================================================
// CORS
// ======================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowAngularFrontend",
        policy =>
        {
            policy
                .WithOrigins(
                    "http://localhost:4200",
                    "https://localhost:4200"
                )
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

// ======================================================
// JWT AUTHENTICATION
// ======================================================

builder.Services
    .AddAuthentication(
        options =>
        {
            options.DefaultAuthenticateScheme =
                JwtBearerDefaults.AuthenticationScheme;

            options.DefaultChallengeScheme =
                JwtBearerDefaults.AuthenticationScheme;
        })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,

                ValidateAudience = true,

                ValidateLifetime = true,

                ValidateIssuerSigningKey = true,

                ValidIssuer = jwtIssuer,

                ValidAudience = jwtAudience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)
                    ),

                RoleClaimType =
                    ClaimTypes.Role,

                NameClaimType =
                    ClaimTypes.Email,

                ClockSkew =
                    TimeSpan.FromMinutes(1)
            };
    });

// ======================================================
// AUTHORIZATION
// ======================================================

builder.Services.AddAuthorization();

// ======================================================
// CONTROLLERS
// ======================================================

builder.Services.AddControllers();

// ======================================================
// SWAGGER
// ======================================================

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "EventParking.API",
            Version = "v1",
            Description =
                "Event & Parking Reservation System API"
        });

    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Description =
                "Enter JWT token like: Bearer {your token}",

            Name = "Authorization",

            In = ParameterLocation.Header,

            Type = SecuritySchemeType.ApiKey,

            Scheme = "Bearer"
        });

    options.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference =
                        new OpenApiReference
                        {
                            Type =
                                ReferenceType.SecurityScheme,

                            Id = "Bearer"
                        }
                },

                Array.Empty<string>()
            }
        });
});

// ======================================================
// BUILD APP
// ======================================================

var app = builder.Build();

// ======================================================
// DEVELOPMENT
// ======================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "EventParking.API v1"
        );
    });
}

// ======================================================
// MIDDLEWARE
// ======================================================

app.UseHttpsRedirection();

app.UseCors(
    "AllowAngularFrontend"
);

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

// ======================================================
// START APP
// ======================================================

app.Run();