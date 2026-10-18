using System.Net;
using System.Net.Http.Json;

namespace OrdersService.Services;

public interface IInventoryClient
{
    Task<StockCheckResult> CheckStockAsync(string productId, int quantity, CancellationToken ct = default);
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
        for (int intento = 1; ; intento++)
        {
            try
            {
                using var response = await _http.GetAsync($"api/inventory/check/{productId}/{quantity}", ct);

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
            catch (HttpRequestException ex) when (intento < MaxAttempts)
            {
                _logger.LogWarning(ex,
                    "InventoryService no respondio (intento {Intento}/{Max}). Reintentando...",
                    intento, MaxAttempts);

                await Task.Delay(TimeSpan.FromMilliseconds(300 * intento), ct);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "InventoryService inalcanzable tras {Max} intentos.", MaxAttempts);

                return new StockCheckResult(
                    ServiceReachable: false, ProductExists: false, Available: false, Stock: 0);
            }
        }
    }
}

public record StockCheckResponse(string ProductId, int RequestedQuantity, int Stock, bool Available);