using Microsoft.OpenApi.Models;
using System.Reflection; // Add this using statement if using XML comments later

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// --- CORRECT SWAGGER SETUP for Controllers ---
// This is necessary for Swagger to discover your API endpoints from controllers.
builder.Services.AddEndpointsApiExplorer(); 

// This is the main Swagger configuration service from Swashbuckle.
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Agentic AI API", Version = "v1" });
});

// REMOVED: builder.Services.AddOpenApi(); // This conflicts with AddSwaggerGen

// CORS configuration (unchanged, this is correct)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

AppContext.SetSwitch("Microsoft.IdentityModel.Logging.ShowPII", true);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // --- CORRECT SWAGGER MIDDLEWARE for Controllers ---
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        // This line tells the UI where to find the generated JSON file.
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Agentic AI API V1");

    });

    // REMOVED: app.MapOpenApi(); // This conflicts with UseSwagger/UseSwaggerUI
}

app.UseHttpsRedirection();

// Make sure CORS is used before Authorization and Controllers.
app.UseCors("AllowAllOrigins");

app.UseAuthorization();

app.MapControllers();

app.Run();
