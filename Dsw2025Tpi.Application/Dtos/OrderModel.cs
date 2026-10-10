using Dsw2025Tpi.Domain.Enums;
namespace Dsw2025Tpi.Application.Dtos;

public record OrderRequest(
    Guid CustomerId,
    string ShippingAddress,
    string BillingAddress,
    string? Notes,
    List<OrderItemRequest> Items
);

public record OrderResponse(
    Guid OrderId,
    string CustomerName,
    DateTime Date,
    string ShippingAddress,
    string BillingAddress,
    string? Notes,
    decimal TotalAmount,
    OrderStatus Status,
    List<OrderItemResponse> Items
);

public record responsePagination
(
    List<OrderResponse> Items,
    int TotalCount
);

