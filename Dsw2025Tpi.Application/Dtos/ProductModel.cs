

namespace Dsw2025Tpi.Application.Dtos
{
    public record ProductModel
    {
        public record Request(string Sku, string InternalCode, string Name, string? Descripcion, decimal CurrentUnitPrice, int StockQuantity);

        public record Response(Guid Id, string Sku, String InternalCode, string Name, string? Descripcion, decimal CurrentUnitPrice, int StockQuantity, bool IsActive);

        public record ResponsePagination(List<Response> Items, int TotalCount);

        public record FilterProduct(string? Status, string? SearchTerm, int? PageNumber, int? PageSize);

    }
}

