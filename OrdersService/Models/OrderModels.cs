using System.ComponentModel.DataAnnotations;

namespace OrdersService.Models;

// OJO: en un record, ASP.NET Core exige que la validacion este en el PARAMETRO
// del constructor ([param:]), no en la propiedad ([property:]).
// Con [property:] el model binding tira InvalidOperationException y responde 500.
public record CreateOrderRequest(
    [param: Required] string CustomerId,
    [param: Required] string ProductId,
    [param: Range(1, 1000)] int Quantity);

public record OrderResponse(
    string OrderId,
    string CustomerId,
    string ProductId,
    int Quantity,
    string Status,
    string Message);

public static class OrderStatus
{
    public const string Confirmed = "Confirmed";
    public const string Rejected = "Rejected";
}