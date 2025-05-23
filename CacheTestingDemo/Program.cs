using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using System;
using System.Text.Json;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Redis distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
    options.InstanceName = "WeatherCache:";
});

// Register a typed client for weather API
builder.Services.AddHttpClient("WeatherApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("WeatherApi:BaseUrl") ?? "https://api.example.com/weather");
    // Add any default headers if needed
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

// Modified endpoint to use Redis cache
app.MapGet("/weatherforecast/{city}", async (string city, IDistributedCache cache, IHttpClientFactory clientFactory, IConfiguration configuration) =>
{
    // Create a cache key
    string cacheKey = city;
    
    // Try to get forecast from cache
    string? cachedForecast = await cache.GetStringAsync(cacheKey);
    
    if (!string.IsNullOrEmpty(cachedForecast))
    {
        // Return cached forecast if available
        return JsonSerializer.Deserialize<WeatherForecast[]>(cachedForecast);
    }
    
    // If not in cache, generate forecast (or fetch from external API)
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    
    // Get cache duration from configuration
    var cacheDuration = configuration.GetValue<TimeSpan>("Redis:CacheDuration"); 
    var jitter = configuration.GetValue<TimeSpan>("Redis:JitterDuration");
    
    var jitterDurationInSeconds = (int)jitter.TotalSeconds;
    var secondsToAdd = Random.Shared.Next(jitterDurationInSeconds);
    var totalDuration = cacheDuration.Add(TimeSpan.FromSeconds(secondsToAdd));
    
    // Create cache options with base duration plus jitter
    var cacheOptions = new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = totalDuration
    };
    
    await cache.SetStringAsync(
        cacheKey, 
        JsonSerializer.Serialize(forecast), 
        cacheOptions
    );
    
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
