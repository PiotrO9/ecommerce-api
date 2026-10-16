var app = WebApplication.Create(args);

app.MapGet("/", () => new { message = "API działa" });

app.Run();