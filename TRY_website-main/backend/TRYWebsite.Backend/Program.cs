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

// ── Razor Pages + Session (required for Admin panel) ──────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(o =>
{
	o.IdleTimeout = TimeSpan.FromMinutes(30);
	o.Cookie.HttpOnly = true;
	o.Cookie.IsEssential = true;
});

var app = builder.Build();

app.UseCors("Frontend");
app.UseRouting();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSession();

app.MapRazorPages();

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

app.MapGet("/api/activities", async (IConfiguration config) =>
{
	var connectionString = config.GetConnectionString("DefaultConnection");
	try
	{
		using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();

		var query = "SELECT id, title, description, icon_name FROM activities ORDER BY id";
		using var command = new SqlCommand(query, connection);
		using var reader = await command.ExecuteReaderAsync();

		var list = new List<object>();
		while (await reader.ReadAsync())
		{
			list.Add(new
			{
				id = reader.GetInt32(0),
				title = reader.GetString(1),
				description = reader.IsDBNull(2) ? "" : reader.GetString(2),
				icon_name = reader.IsDBNull(3) ? "" : reader.GetString(3)
			});
		}
		return Results.Ok(list);
	}
	catch (Exception)
	{
		return Results.Json(new { error = "Failed to load activities." }, statusCode: 500);
	}
});

app.MapGet("/api/news", async (IConfiguration config) =>
{
	var connectionString = config.GetConnectionString("DefaultConnection");
	try
	{
		using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();

		var query = "SELECT TOP 10 id, title, category, content, image_path, published_at FROM news WHERE is_published = 1 ORDER BY published_at DESC";
		using var command = new SqlCommand(query, connection);
		using var reader = await command.ExecuteReaderAsync();

		var list = new List<object>();
		while (await reader.ReadAsync())
		{
			list.Add(new
			{
				id = reader.GetInt32(0),
				title = reader.GetString(1),
				category = reader.IsDBNull(2) ? "" : reader.GetString(2),
				content = reader.IsDBNull(3) ? "" : reader.GetString(3),
				image_path = reader.IsDBNull(4) ? "" : reader.GetString(4),
				published_at = reader.IsDBNull(5) ? "" : reader.GetDateTime(5).ToString("dd MMM yyyy")
			});
		}
		return Results.Ok(list);
	}
	catch (Exception)
	{
		return Results.Json(new { error = "Failed to load news." }, statusCode: 500);
	}
});

app.MapGet("/api/gallery", async (IConfiguration config) =>
{
	var connectionString = config.GetConnectionString("DefaultConnection");
	try
	{
		using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();

		var query = "SELECT id, image_path, caption FROM gallery ORDER BY created_at DESC";
		using var command = new SqlCommand(query, connection);
		using var reader = await command.ExecuteReaderAsync();

		var list = new List<object>();
		while (await reader.ReadAsync())
		{
			list.Add(new
			{
				id = reader.GetInt32(0),
				image_path = reader.GetString(1),
				caption = reader.IsDBNull(2) ? "" : reader.GetString(2)
			});
		}
		return Results.Ok(list);
	}
	catch (Exception)
	{
		return Results.Json(new { error = "Failed to load gallery." }, statusCode: 500);
	}
});

app.MapGet("/api/testimonials", async (IConfiguration config) =>
{
	var connectionString = config.GetConnectionString("DefaultConnection");
	try
	{
		using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();

		var query = "SELECT id, quote_text, author_name, author_role FROM impact_stories ORDER BY created_at DESC";
		using var command = new SqlCommand(query, connection);
		using var reader = await command.ExecuteReaderAsync();

		var list = new List<object>();
		while (await reader.ReadAsync())
		{
			list.Add(new
			{
				id = reader.GetInt32(0),
				quote = reader.GetString(1),
				author = reader.GetString(2),
				role = reader.IsDBNull(3) ? "" : reader.GetString(3)
			});
		}
		return Results.Ok(list);
	}
	catch (Exception)
	{
		return Results.Json(new { error = "Failed to load testimonials." }, statusCode: 500);
	}
});

app.MapPost("/api/volunteers", async (VolunteerRequest request, IConfiguration config) =>
{
	if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
	{
		return Results.BadRequest(new { success = false, error = "Name and Email are required." });
	}

	var connectionString = config.GetConnectionString("DefaultConnection");
	try
	{
		using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();

		var query = "INSERT INTO volunteers (name, email, phone, submitted_at, status) VALUES (@Name, @Email, @Phone, GETDATE(), 'Pending')";
		using var command = new SqlCommand(query, connection);
		command.Parameters.AddWithValue("@Name", request.Name);
		command.Parameters.AddWithValue("@Email", request.Email);
		command.Parameters.AddWithValue("@Phone", request.Phone ?? (object)DBNull.Value);

		await command.ExecuteNonQueryAsync();
		return Results.Ok(new { success = true });
	}
	catch (Exception)
	{
		return Results.Json(new { success = false, error = "A database error occurred while registering." }, statusCode: 500);
	}
});

app.Run();

public record MessageRequest(string Name, string Email, string Subject, string Message);
public record VolunteerRequest(string Name, string Email, string? Phone);
