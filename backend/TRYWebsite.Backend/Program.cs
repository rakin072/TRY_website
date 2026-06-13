using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
	options.AddPolicy("Frontend", policy =>
	{
		policy.WithOrigins("http://localhost:5501", "https://localhost:5501")
			.AllowAnyHeader()
			.AllowAnyMethod();
	});
});

var app = builder.Build();

app.UseCors("Frontend");
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/site-info", () => Results.Ok(new
{
	name = "TRY - KUET Social Service Club",
	description = "ASP.NET Core backend for the TRY website",
	frontendOrigin = "http://localhost:5501"
}));

app.MapGet("/api/site-stats", async (IConfiguration config) =>
{
	var connectionString = config.GetConnectionString("DefaultConnection");
	try
	{
		using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();

		var query = "SELECT volunteers_count, projects_count, people_helped, years_active FROM site_stats WHERE id = 1";
		using var command = new SqlCommand(query, connection);
		using var reader = await command.ExecuteReaderAsync();

		if (await reader.ReadAsync())
		{
			return Results.Ok(new
			{
				volunteers = reader.GetInt32(0),
				projects = reader.GetInt32(1),
				peopleHelped = reader.GetInt32(2),
				yearsActive = reader.GetInt32(3)
			});
		}
		return Results.NotFound(new { error = "Stats record not found." });
	}
	catch (Exception ex)
	{
		app.Logger.LogError(ex, "Error fetching site stats");
		return Results.Json(new { error = "Failed to load statistics from database." }, statusCode: 500);
	}
});

app.MapPost("/api/messages", async (MessageRequest request, IConfiguration config) =>
{
	if (string.IsNullOrWhiteSpace(request.Name) ||
		string.IsNullOrWhiteSpace(request.Email) ||
		string.IsNullOrWhiteSpace(request.Message))
	{
		return Results.BadRequest(new { success = false, error = "Name, Email, and Message are required." });
	}

	var connectionString = config.GetConnectionString("DefaultConnection");
	try
	{
		using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();

		var query = "INSERT INTO messages (name, email, subject, message, received_at, is_read) VALUES (@Name, @Email, @Subject, @Message, GETDATE(), 0)";
		using var command = new SqlCommand(query, connection);
		command.Parameters.AddWithValue("@Name", request.Name);
		command.Parameters.AddWithValue("@Email", request.Email);
		command.Parameters.AddWithValue("@Subject", request.Subject ?? (object)DBNull.Value);
		command.Parameters.AddWithValue("@Message", request.Message);

		await command.ExecuteNonQueryAsync();
		return Results.Ok(new { success = true });
	}
	catch (Exception ex)
	{
		app.Logger.LogError(ex, "Error saving contact message");
		return Results.Json(new { success = false, error = "A database error occurred while saving your message." }, statusCode: 500);
	}
});

app.Run();

public record MessageRequest(string Name, string Email, string Subject, string Message);
