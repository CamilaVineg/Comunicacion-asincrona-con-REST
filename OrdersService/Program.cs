using OrdersService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// URL del InventoryService.
//   - En Docker Compose: http://inventoryservice:80/  (lo define la variable INVENTORY_BASE_URL)
//   - Corriendo local:   http://localhost:8081/
var inventoryBaseUrl = Environment.GetEnvironmentVariable("INVENTORY_BASE_URL")
                       ?? "http://localhost:8081/";

// HttpClient tipado: lo registra DI y maneja el ciclo de vida por nosotros.
builder.Services.AddHttpClient<IInventoryClient, InventoryClient>(client =>
{
    client.BaseAddress = new Uri(inventoryBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

app.MapControllers();

app.Run();