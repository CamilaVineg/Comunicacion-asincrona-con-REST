var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

// Solo HTTP: en Docker no hay puerto HTTPS configurado.
app.MapControllers();

app.Run();