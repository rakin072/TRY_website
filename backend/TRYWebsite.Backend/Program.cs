using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TRYWebsite.Backend;

var builder = WebApplication.CreateBuilder(args);

// ── CORS ────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
	options.AddPolicy("Frontend", policy =>
	{
		policy.WithOrigins("http://localhost:5501", "https://localhost:5501")
			.AllowAnyHeader()
			.AllowAnyMethod()
			.AllowCredentials();
	});
});

// ── Session ─────────────────────────────────────────────────────────────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
	options.IdleTimeout        = TimeSpan.FromMinutes(30);
	options.Cookie.HttpOnly    = true;
	options.Cookie.IsEssential = true;
	options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
	options.Cookie.SameSite    = SameSiteMode.Strict;
	options.Cookie.Name        = ".TRY.Session";
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorPages();

var app = builder.Build();

// ── Middleware (ORDER MATTERS) ──────────────────────────────────────────────
app.UseSession();
app.UseCors("Frontend");
app.UseDefaultFiles();
app.UseStaticFiles();

// ═══════════════════════════════════════════════════════════════════════════
//  HELPER FUNCTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Compute lowercase hex SHA-256 hash of a string.</summary>
static string HashSha256(string input)
{
	var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
	return Convert.ToHexString(bytes).ToLowerInvariant();
}

/// <summary>Generate a cryptographically random 32-byte token as a 64-char hex string.</summary>
static string GenerateSecureToken()
{
	var bytes = RandomNumberGenerator.GetBytes(32);
	return Convert.ToHexString(bytes).ToLowerInvariant();
}

/// <summary>Open and return a new SqlConnection from configuration.</summary>
static async Task<SqlConnection> GetDbAsync(IConfiguration config)
{
	var conn = new SqlConnection(config.GetConnectionString("DefaultConnection"));
	await conn.OpenAsync();
	return conn;
}

/// <summary>Write a one-time flash message cookie (60-second lifespan).</summary>
static void SetFlash(HttpContext ctx, string type, string message)
{
	ctx.Response.Cookies.Append(CookieNames.Flash, $"{type}|{message}", new CookieOptions
	{
		HttpOnly  = false,
		SameSite  = SameSiteMode.Strict,
		Expires   = DateTimeOffset.UtcNow.AddSeconds(60),
		Path      = "/"
	});
}

/// <summary>
/// Validate a remember-me cookie token against the DB.
/// If valid, restore the session and perform token rotation.
/// Returns the admin username on success, null on failure.
/// </summary>
static async Task<string?> ValidateRememberMeTokenAsync(string rawToken, HttpContext ctx, IConfiguration config)
{
	var tokenHash = HashSha256(rawToken);
	try
	{
		await using var conn = await GetDbAsync(config);

		// Look up the token
		var lookupSql = @"
			SELECT au.id, au.username, art.id AS token_id, art.expires_at
			FROM admin_remember_tokens art
			JOIN admin_users au ON au.id = art.admin_id
			WHERE art.token_hash = @hash AND art.expires_at > GETDATE()";

		await using var lookupCmd = new SqlCommand(lookupSql, conn);
		lookupCmd.Parameters.AddWithValue("@hash", tokenHash);
		await using var reader = await lookupCmd.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			// Invalid token — delete cookie
			ctx.Response.Cookies.Delete(CookieNames.RememberAdmin);
			return null;
		}

		var adminId   = reader.GetInt32(0);
		var username  = reader.GetString(1);
		var tokenDbId = reader.GetInt32(2);
		await reader.CloseAsync();

		// Restore session
		ctx.Session.SetString(SessionKeys.AdminUser, username);
		ctx.Session.SetString(SessionKeys.AdminLoginAt, DateTime.UtcNow.ToString("O"));

		// ── Token rotation: delete old, issue new ──
		await using var delCmd = new SqlCommand("DELETE FROM admin_remember_tokens WHERE id = @id", conn);
		delCmd.Parameters.AddWithValue("@id", tokenDbId);
		await delCmd.ExecuteNonQueryAsync();

		var newToken     = GenerateSecureToken();
		var newTokenHash = HashSha256(newToken);
		var newExpiry    = DateTime.UtcNow.AddDays(30);

		await using var insCmd = new SqlCommand(
			"INSERT INTO admin_remember_tokens (admin_id, token_hash, expires_at) VALUES (@aid, @hash, @exp)", conn);
		insCmd.Parameters.AddWithValue("@aid", adminId);
		insCmd.Parameters.AddWithValue("@hash", newTokenHash);
		insCmd.Parameters.AddWithValue("@exp", newExpiry);
		await insCmd.ExecuteNonQueryAsync();

		ctx.Response.Cookies.Append(CookieNames.RememberAdmin, newToken, new CookieOptions
		{
			HttpOnly  = true,
			Secure    = false, // set true in production (HTTPS)
			SameSite  = SameSiteMode.Strict,
			Expires   = DateTimeOffset.UtcNow.AddDays(30),
			Path      = "/"
		});

		return username;
	}
	catch
	{
		ctx.Response.Cookies.Delete(CookieNames.RememberAdmin);
		return null;
	}
}

/// <summary>
/// Check if the admin is authenticated via session or remember-me cookie.
/// Returns the admin username on success, null if unauthenticated.
/// </summary>
static async Task<string?> GetAdminSessionAsync(HttpContext ctx, IConfiguration config)
{
	var user = ctx.Session.GetString(SessionKeys.AdminUser);
	if (user != null) return user;

	var token = ctx.Request.Cookies[CookieNames.RememberAdmin];
	if (string.IsNullOrWhiteSpace(token)) return null;

	return await ValidateRememberMeTokenAsync(token, ctx, config);
}


// ═══════════════════════════════════════════════════════════════════════════
//  EXISTING ENDPOINTS (unchanged)
// ═══════════════════════════════════════════════════════════════════════════

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


// ═══════════════════════════════════════════════════════════════════════════
//  ADMIN AUTH ENDPOINTS
// ═══════════════════════════════════════════════════════════════════════════

// POST /api/admin/login
app.MapPost("/api/admin/login", async (LoginRequest request, HttpContext ctx, IConfiguration config) =>
{
	if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
		return Results.BadRequest(new { success = false, error = "Username and password are required." });

	var passwordHash = HashSha256(request.Password);

	try
	{
		await using var conn = await GetDbAsync(config);

		// Authenticate
		await using var cmd = new SqlCommand(
			"SELECT id, username FROM admin_users WHERE username = @u AND password_hash = @h", conn);
		cmd.Parameters.AddWithValue("@u", request.Username);
		cmd.Parameters.AddWithValue("@h", passwordHash);
		await using var reader = await cmd.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
			return Results.Json(new { success = false, error = "Invalid credentials." }, statusCode: 401);

		var adminId  = reader.GetInt32(0);
		var username = reader.GetString(1);
		await reader.CloseAsync();

		// Set session
		ctx.Session.SetString(SessionKeys.AdminUser, username);
		ctx.Session.SetString(SessionKeys.AdminLoginAt, DateTime.UtcNow.ToString("O"));

		// Cleanup expired tokens (opportunistic)
		await using var cleanupCmd = new SqlCommand(
			"DELETE FROM admin_remember_tokens WHERE expires_at < GETDATE()", conn);
		await cleanupCmd.ExecuteNonQueryAsync();

		// Remember Me cookie
		if (request.RememberMe)
		{
			var rawToken  = GenerateSecureToken();
			var tokenHash = HashSha256(rawToken);
			var expiresAt = DateTime.UtcNow.AddDays(30);

			await using var insCmd = new SqlCommand(
				"INSERT INTO admin_remember_tokens (admin_id, token_hash, expires_at) VALUES (@aid, @hash, @exp)", conn);
			insCmd.Parameters.AddWithValue("@aid", adminId);
			insCmd.Parameters.AddWithValue("@hash", tokenHash);
			insCmd.Parameters.AddWithValue("@exp", expiresAt);
			await insCmd.ExecuteNonQueryAsync();

			ctx.Response.Cookies.Append(CookieNames.RememberAdmin, rawToken, new CookieOptions
			{
				HttpOnly  = true,
				Secure    = false, // set true in production (HTTPS)
				SameSite  = SameSiteMode.Strict,
				Expires   = DateTimeOffset.UtcNow.AddDays(30),
				Path      = "/"
			});
		}

		return Results.Ok(new { success = true, username });
	}
	catch (Exception ex)
	{
		app.Logger.LogError(ex, "Error during admin login");
		return Results.Json(new { success = false, error = "A server error occurred." }, statusCode: 500);
	}
});

// POST /api/admin/logout
app.MapPost("/api/admin/logout", async (HttpContext ctx, IConfiguration config) =>
{
	var username = ctx.Session.GetString(SessionKeys.AdminUser);

	// Delete all remember-me tokens for this admin
	if (username != null)
	{
		try
		{
			await using var conn = await GetDbAsync(config);
			await using var cmd = new SqlCommand(
				"DELETE FROM admin_remember_tokens WHERE admin_id = (SELECT id FROM admin_users WHERE username = @u)", conn);
			cmd.Parameters.AddWithValue("@u", username);
			await cmd.ExecuteNonQueryAsync();
		}
		catch (Exception ex)
		{
			app.Logger.LogError(ex, "Error deleting remember-me tokens during logout");
		}
	}

	ctx.Session.Clear();
	ctx.Response.Cookies.Delete(CookieNames.RememberAdmin);

	return Results.Ok(new { success = true });
});

// GET /api/admin/me
app.MapGet("/api/admin/me", async (HttpContext ctx, IConfiguration config) =>
{
	var username = await GetAdminSessionAsync(ctx, config);
	if (username == null)
		return Results.Json(new { authenticated = false }, statusCode: 401);

	var loginAt = ctx.Session.GetString(SessionKeys.AdminLoginAt);
	return Results.Ok(new { authenticated = true, username, loginAt });
});


// ═══════════════════════════════════════════════════════════════════════════
//  VISITOR TRACKING ENDPOINTS
// ═══════════════════════════════════════════════════════════════════════════

// GET /api/visitor/identify
app.MapGet("/api/visitor/identify", (HttpContext ctx) =>
{
	var existing = ctx.Request.Cookies[CookieNames.VisitorId];
	var isNew = false;

	if (string.IsNullOrWhiteSpace(existing) || !Guid.TryParse(existing, out _))
	{
		existing = Guid.NewGuid().ToString();
		isNew = true;
	}

	ctx.Response.Cookies.Append(CookieNames.VisitorId, existing, new CookieOptions
	{
		HttpOnly  = false,
		Secure    = false,
		SameSite  = SameSiteMode.Lax,
		Expires   = DateTimeOffset.UtcNow.AddYears(1),
		Path      = "/"
	});

	return Results.Ok(new { visitorId = existing, isNew });
});

// POST /api/visitor/log
app.MapPost("/api/visitor/log", async (VisitorLogRequest request, HttpContext ctx, IConfiguration config) =>
{
	if (string.IsNullOrWhiteSpace(request.VisitorId) || !Guid.TryParse(request.VisitorId, out _))
		return Results.BadRequest(new { success = false, error = "Invalid visitor ID." });

	try
	{
		await using var conn = await GetDbAsync(config);
		var userAgent = ctx.Request.Headers.UserAgent.ToString();
		var page = request.Page ?? "/";

		// Deduplicate: ignore same (visitor_id, page) within 5 minutes
		var sql = @"
			IF NOT EXISTS (
				SELECT 1 FROM visitor_log
				WHERE visitor_id = @vid AND page = @page
				AND visited_at > DATEADD(MINUTE, -5, GETDATE())
			)
			INSERT INTO visitor_log (visitor_id, page, user_agent, visited_at)
			VALUES (@vid, @page, @ua, GETDATE())";

		await using var cmd = new SqlCommand(sql, conn);
		cmd.Parameters.AddWithValue("@vid", request.VisitorId);
		cmd.Parameters.AddWithValue("@page", page);
		cmd.Parameters.AddWithValue("@ua", userAgent.Length > 500 ? userAgent[..500] : userAgent);
		await cmd.ExecuteNonQueryAsync();

		return Results.Ok(new { success = true });
	}
	catch (Exception ex)
	{
		app.Logger.LogError(ex, "Error logging visitor");
		return Results.Json(new { success = false, error = "Database error." }, statusCode: 500);
	}
});


// ═══════════════════════════════════════════════════════════════════════════
//  VOLUNTEER ENDPOINTS
// ═══════════════════════════════════════════════════════════════════════════

// POST /api/volunteers
app.MapPost("/api/volunteers", async (VolunteerRequest request, HttpContext ctx, IConfiguration config) =>
{
	var errors = new List<string>();

	if (string.IsNullOrWhiteSpace(request.Name))
		errors.Add("Name is required.");
	else if (request.Name.Length > 100)
		errors.Add("Name must be 100 characters or fewer.");

	if (string.IsNullOrWhiteSpace(request.Email))
		errors.Add("Email is required.");
	else if (!Regex.IsMatch(request.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
		errors.Add("Email format is invalid.");

	if (!string.IsNullOrWhiteSpace(request.Phone) && request.Phone.Length > 50)
		errors.Add("Phone must be 50 characters or fewer.");

	if (errors.Count > 0)
	{
		// Save draft to session so the frontend can restore on retry
		var draft = JsonSerializer.Serialize(new { name = request.Name, email = request.Email, phone = request.Phone });
		ctx.Session.SetString(SessionKeys.VolunteerForm, draft);
		return Results.BadRequest(new { success = false, errors });
	}

	try
	{
		await using var conn = await GetDbAsync(config);
		await using var cmd = new SqlCommand(
			"INSERT INTO volunteers (name, email, phone, submitted_at, status) VALUES (@name, @email, @phone, GETDATE(), 'Pending')", conn);
		cmd.Parameters.AddWithValue("@name", request.Name);
		cmd.Parameters.AddWithValue("@email", request.Email);
		cmd.Parameters.AddWithValue("@phone", (object?)request.Phone ?? DBNull.Value);
		await cmd.ExecuteNonQueryAsync();

		// Clear draft from session
		ctx.Session.Remove(SessionKeys.VolunteerForm);

		SetFlash(ctx, "success", "Thank you! Your volunteer application has been submitted.");
		return Results.Ok(new { success = true });
	}
	catch (Exception ex)
	{
		app.Logger.LogError(ex, "Error saving volunteer application");
		return Results.Json(new { success = false, error = "A database error occurred." }, statusCode: 500);
	}
});

// GET /api/volunteers/draft
app.MapGet("/api/volunteers/draft", (HttpContext ctx) =>
{
	var draftJson = ctx.Session.GetString(SessionKeys.VolunteerForm);
	if (draftJson == null)
		return Results.Ok(new { draft = (object?)null });

	var draft = JsonSerializer.Deserialize<object>(draftJson);
	return Results.Ok(new { draft });
});


// ═══════════════════════════════════════════════════════════════════════════
//  FLASH MESSAGE ENDPOINT
// ═══════════════════════════════════════════════════════════════════════════

app.MapGet("/api/flash", (HttpContext ctx) =>
{
	var flash = ctx.Request.Cookies[CookieNames.Flash];
	if (flash == null)
		return Results.Ok(new { type = (string?)null, message = (string?)null });

	ctx.Response.Cookies.Delete(CookieNames.Flash);

	var parts = flash.Split('|', 2);
	return Results.Ok(new
	{
		type    = parts[0],
		message = parts.Length > 1 ? parts[1] : ""
	});
});


// ═══════════════════════════════════════════════════════════════════════════
//  COOKIE CONSENT ENDPOINTS
// ═══════════════════════════════════════════════════════════════════════════

app.MapPost("/api/cookies/accept", (HttpContext ctx) =>
{
	ctx.Response.Cookies.Append(CookieNames.ConsentGiven, "true", new CookieOptions
	{
		HttpOnly  = false,
		SameSite  = SameSiteMode.Strict,
		Expires   = DateTimeOffset.UtcNow.AddYears(1),
		Path      = "/"
	});
	return Results.Ok(new { accepted = true });
});

app.MapGet("/api/cookies/status", (HttpContext ctx) =>
{
	var consent = ctx.Request.Cookies[CookieNames.ConsentGiven];
	return Results.Ok(new { consentGiven = consent == "true" });
});


app.MapRazorPages();
app.Run();

// ═══════════════════════════════════════════════════════════════════════════
//  RECORD TYPES
// ═══════════════════════════════════════════════════════════════════════════

public record MessageRequest(string Name, string Email, string? Subject, string Message);
public record LoginRequest(string Username, string Password, bool RememberMe);
public record VolunteerRequest(string Name, string Email, string? Phone);
public record VisitorLogRequest(string VisitorId, string? Page);
