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

app.Run();
