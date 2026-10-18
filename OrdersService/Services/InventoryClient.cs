using System.Net;
using System.Net.Http.Json;

namespace OrdersService.Services;

public interface IInventoryClient
{
    Task<StockCheckResult> CheckStockAsync(string productId, int quantity, CancellationToken ct = default);

    Task<ReserveResult> ReserveStockAsync(string productId, int quantity, CancellationToken ct = default);
}

/// <summary>
/// Resultado de consultar al InventoryService. Distingue "no hay stock" de
/// "el servicio no respondio", porque las dos cosas llevan a decisiones distintas.
/// </summary>
public record StockCheckResult(
    bool ServiceReachable,
    bool ProductExists,
    bool Available,
    int Stock);

/// <summary>
/// Resultado de reservar stock. Solo Reserved == true descuenta unidades.
/// </summary>
public record ReserveResult(
    bool ServiceReachable,
    bool Reserved,
    int Remaining);

public class InventoryClient : IInventoryClient
{
    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient http, ILogger<InventoryClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<StockCheckResult> CheckStockAsync(string productId, int quantity, CancellationToken ct = default)
    {
        var response = await SendWithRetryAsync(
            $"check de stock de {productId}",
            () => _http.GetAsync($"api/inventory/check/{productId}/{quantity}", ct));

        if (response is null)
        {
            return new StockCheckResult(
                ServiceReachable: false, ProductExists: false, Available: false, Stock: 0);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new StockCheckResult(
                    ServiceReachable: true, ProductExists: false, Available: false, Stock: 0);
            }

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<StockCheckResponse>(cancellationToken: ct);

            return new StockCheckResult(
                ServiceReachable: true,
                ProductExists: true,
                Available: body?.Available ?? false,
                Stock: body?.Stock ?? 0);
        }
    }

    public async Task<ReserveResult> ReserveStockAsync(string productId, int quantity, CancellationToken ct = default)
    {
        var response = await SendWithRetryAsync(
            $"reserva de {quantity} unidades de {productId}",
            () => _http.PostAsync($"api/inventory/reserve/{productId}/{quantity}", content: null, ct));

        if (response is null)
        {
            return new ReserveResult(
                ServiceReachable: false, Reserved: false, Remaining: 0);
        }

        using (response)
        {
            // 404: el producto no existe. 409: otro pedido se llevo el stock entre el
            // check y esta reserva (carrera), y el body trae el stock que queda.
            var body = await response.Content.ReadFromJsonAsync<ReserveResponse>(cancellationToken: ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ReserveResult(ServiceReachable: true, Reserved: false, Remaining: 0);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new ReserveResult(
                    ServiceReachable: true, Reserved: false, Remaining: body?.Remaining ?? 0);
            }

            response.EnsureSuccessStatusCode();

            return new ReserveResult(
                ServiceReachable: true,
                Reserved: body?.Reserved ?? false,
                Remaining: body?.Remaining ?? 0);
        }
    }

    /// <summary>
    /// Envia la peticion reintentando ante fallos de red. Devuelve null si el
    /// servicio quedo inalcanzable, que es un caso distinto de "no hay stock".
    /// </summary>
    private async Task<HttpResponseMessage?> SendWithRetryAsync(
        string descripcion,
        Func<Task<HttpResponseMessage>> enviar)
    {
        for (int intento = 1; ; intento++)
        {
            try
            {
                return await enviar();
            }
            catch (HttpRequestException ex) when (intento < MaxAttempts)
            {
                _logger.LogWarning(ex,
                    "{Descripcion} fallo (intento {Intento}/{Max}). Reintentando...",
                    descripcion, intento, MaxAttempts);

                await Task.Delay(TimeSpan.FromMilliseconds(300 * intento));
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "{Descripcion} inalcanzable tras {Max} intentos.", descripcion, MaxAttempts);

                return null;
            }
        }
    }
}

public record StockCheckResponse(string ProductId, int RequestedQuantity, int Stock, bool Available);

public record ReserveResponse(string ProductId, int ReservedQuantity, int Remaining, bool Reserved);