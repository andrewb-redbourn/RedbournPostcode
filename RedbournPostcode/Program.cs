using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();

app.MapGet("/api/LatLong", async (string postcode, string user) =>
{
    if (Malformed(postcode))
    {
        logger.LogInformation("Malformed postcode.");
        return Results.BadRequest($"Malformed postcode: {postcode}. Format expected: A1 1AA");
    }
    logger.LogInformation($"Postcode received {postcode}");
    // Do we have a valid user, with credits, that isn't disabled?
    await using var conuser = new SqliteConnection(@"Data Source=.\users.sqlite;");
    await conuser.OpenAsync();
    var trans = conuser.BeginTransaction();

    try
    {
        var (remaining, disabled) = await GetUserDataAsync(user, conuser, trans);

        if (disabled)
        {
            logger.LogInformation("User account is disabled.");
            return Results.Forbid();
        }

        if (remaining <= 0)
        {
            logger.LogInformation("User exceeded their limit.");
            return Results.Forbid();
        }

        await UpdateRemainingAsync(user, conuser, trans);
    }
    finally
    {
        trans?.CommitAsync().Wait();
        await conuser.CloseAsync();
    }

    const string CONNECTION_STRING = @"Data Source=.\postcodelatlong.sqlite;Mode=ReadOnly;";
    var (found, lat, lon) = await GetPositionAsync(postcode, CONNECTION_STRING, logger);
    var wkt = $"POINT ({lon} {lat})";
    return found
        ? Results.Ok(new LatLongPostcode(postcode, lat, lon, wkt))
        : Results.NotFound(new LatLongPostcode( postcode, lat = 0, lon = 0, wkt));
}).WithName("GetLatLongForPostcode")
  .WithOpenApi();

app.Run();

async Task<(bool found, double lat, double lon)> GetPositionAsync(string postcode, string connectionString, ILogger logger)
{
    await using var con = new SqliteConnection(connectionString);
    await con.OpenAsync();

    var sql = "SELECT lat, lon FROM postcodell WHERE postcode = @postcode";
    await using var cmd = new SqliteCommand(sql, con);
    cmd.Parameters.AddWithValue("@postcode", postcode);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (reader.Read())
    {
        var lat = reader.GetDouble(0);
        var lon = reader.GetDouble(1);
        return (true, lat, lon);
    }
    else
    {
        logger.LogInformation("Failed to find information for the postcode.");
        return (false, 0, 0);
    }
}

async Task<(int remaining, bool disabled)> GetUserDataAsync(string user, SqliteConnection conuser, SqliteTransaction trans)
{
    var sqluser = "SELECT * FROM users WHERE email = @user";
    using var cmduser = new SqliteCommand(sqluser, conuser, trans);
    cmduser.Parameters.AddWithValue("@user", user);
    using var readeruser = await cmduser.ExecuteReaderAsync();
    if (!readeruser.Read())
    {
        logger.LogInformation("Creating new user and providing small amount of credits.");
        await InsertNewUserAsync(user, conuser, trans);

        await readeruser.CloseAsync();
        await using var readerNewUser = await cmduser.ExecuteReaderAsync();
        await readerNewUser.ReadAsync();
        return (readerNewUser.GetInt32(1), readerNewUser.GetBoolean(2));
    }
    else
    {
        return (readeruser.GetInt32(1), readeruser.GetBoolean(2));
    }
}

async Task UpdateRemainingAsync(string user, SqliteConnection conuser, SqliteTransaction trans)
{
    await using var cmduserupdate =
        new SqliteCommand("UPDATE users SET remaining = remaining - 1 WHERE email = @user", conuser, trans);
    cmduserupdate.Parameters.AddWithValue("@user", user);
    await cmduserupdate.ExecuteNonQueryAsync();
}

async Task InsertNewUserAsync(string user, SqliteConnection conuser, SqliteTransaction trans)
{
    await using var cmduserinsert =
        new SqliteCommand("INSERT INTO users (email, remaining,disabled) VALUES (@user, 100, 0)", conuser, trans);
    cmduserinsert.Parameters.AddWithValue("@user", user);
    await cmduserinsert.ExecuteNonQueryAsync();
}

bool Malformed(string postcode) => !new Regex(@"^[A-Z]{1,2}\d{1,2}[A-Z]?\s\d[A-Z]{2}$", RegexOptions.IgnoreCase).IsMatch(postcode);

record LatLongPostcode(
    string postcode
  , double lat
  , double lon
  , string wkt);
