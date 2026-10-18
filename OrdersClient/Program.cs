using System.Net.Http.Json;
using System.Text.Json;

var baseUrl = Environment.GetEnvironmentVariable("ORDERS_BASE_URL") ?? "http://localhost:8080/";

using var client = new HttpClient
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(30)
};

Console.WriteLine($"OrdersClient consumiendo {client.BaseAddress}");
Console.WriteLine(new string('-', 70));

// Espera a que OrdersService responda (el healthcheck de compose ya ordena,
// esto es defensa extra).
if (!await WaitForService())
{
    Console.WriteLine("OrdersService no esta disponible. Fin del cliente.");
    return;
}

// 1) Pedido con stock suficiente -> se confirma (201)
await PostOrder("CLI-001", "P001", 3, "stock suficiente");

// 2) Pedido que excede el stock -> se rechaza (409)
await PostOrder("CLI-002", "P002", 999, "stock insuficiente");

// 3) Producto inexistente -> se rechaza (404)
await PostOrder("CLI-003", "NOPE", 1, "producto inexistente");

async Task PostOrder(string customerId, string productId, int quantity, string escenario)
{
    Console.WriteLine();
    Console.WriteLine($"> POST api/orders  [{escenario}]  cliente={customerId} producto={productId} cantidad={quantity}");

    using var response = await client.PostAsJsonAsync("api/orders", new
    {
        CustomerId = customerId,
        ProductId = productId,
        Quantity = quantity
    });

    // Leemos el body como string y recien ahi deserializamos: un error del servidor
    // puede devolver un body vacio y ReadFromJsonAsync tiraria JsonException.
    var raw = await response.Content.ReadAsStringAsync();
    OrderResponse? body = null;

    if (!string.IsNullOrWhiteSpace(raw))
    {
        body = JsonSerializer.Deserialize<OrderResponse>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    Console.WriteLine($"  HTTP {(int)response.StatusCode} {response.StatusCode}");
    Console.WriteLine($"  Status : {body?.Status ?? "(sin cuerpo)"}");
    Console.WriteLine($"  Mensaje: {body?.Message ?? (string.IsNullOrWhiteSpace(raw) ? "(vacio)" : raw)}");
}

async Task<bool> WaitForService()
{
    for (int intento = 1; intento <= 15; intento++)
    {
        try
        {
            // Cualquier respuesta HTTP (incluso 404) significa que el servicio esta vivo.
            // Un GET no muta el stock, a diferencia de un POST de prueba.
            using var probe = await client.GetAsync("api/orders/health-probe");
            return true;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine($"  intento {intento}: OrdersService todavia no responde...");
            await Task.Delay(1000);
        }
    }

    return false;
}

public record OrderResponse(
    string OrderId,
    string CustomerId,
    string ProductId,
    int Quantity,
    string Status,
    string Message);