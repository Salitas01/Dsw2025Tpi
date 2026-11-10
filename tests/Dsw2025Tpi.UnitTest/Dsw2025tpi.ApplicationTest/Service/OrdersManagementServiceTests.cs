using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Domain.Interfaces;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Application.Exceptions;

namespace Dsw2025Tpi.UnitTest.Application.Services
{
    public class OrdersManagementServiceTests
    {
        // Escenario 1: Camino feliz
        [Fact]
        public async Task CreateOrder_WithValidItemsAndStock_CalculatesTotalAndDecrementsProductStock()
        {
            // 1. ARRANGE
            var repoMock = new Mock<IRepository>();
            var loggerMock = new Mock<ILogger<OrdersManagementService>>();
            var service = new OrdersManagementService(repoMock.Object, loggerMock.Object);

            Guid customerId = Guid.NewGuid();
            Guid productId = Guid.NewGuid();

            var request = new OrderRequest(
                customerId,
                "Calle Falsa 123",
                "Calle Falsa 123",
                "Nota test",
                new List<OrderItemRequest> { new OrderItemRequest(productId, 3) }
            );

            // Simulamos que el cliente existe
            var mockCustomer = new Customer { Id = customerId, Name = "Juan Perez" };
            repoMock.Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
                    .ReturnsAsync(mockCustomer);

            // Simulamos que el producto existe, está activo y tiene stock suficiente (Stock: 10, Precio: 100)
            var mockProduct = new Product("SKU-1", "Mouse", 100m, 10, "Desc", "INT-1") { Id = productId, IsActive = true };
            repoMock.Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
                    .ReturnsAsync(mockProduct);

            // Configuramos las respuestas esperadas para el Update y el Add
            repoMock.Setup(r => r.Update(It.IsAny<Product>())).ReturnsAsync(mockProduct);
            repoMock.Setup(r => r.Add(It.IsAny<Order>())).ReturnsAsync((Order o) => o);

            // 2. ACT
            var resultado = await service.CreateOrder(request);

            // 3. ASSERT
            // Comprobar que se descuente el stock del producto (Teníamos 10, pedimos 3, quedan 7)
            Assert.Equal(7, mockProduct.StockQuantity);

            // Comprobar que el monto total sea la suma exacta (3 * 100 = 300)
            Assert.Equal(300m, resultado.TotalAmount);

            // Verificar invocaciones: 1 al Update del producto y 1 al Add de la orden
            repoMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Once);
            repoMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Once);
        }

        // Escenario 2: Falla por insuficiencia de stock
        [Fact]
        public async Task CreateOrder_WhenProductStockIsInsufficient_ThrowsInsufficientStockException()
        {
            // 1. ARRANGE
            var repoMock = new Mock<IRepository>();
            var loggerMock = new Mock<ILogger<OrdersManagementService>>();
            var service = new OrdersManagementService(repoMock.Object, loggerMock.Object);

            Guid customerId = Guid.NewGuid();
            Guid productId = Guid.NewGuid();

            // Solicitud de compra: Quantity = 5
            var request = new OrderRequest(
                customerId,
                "Dir 1",
                "Dir 1",
                "",
                new List<OrderItemRequest> { new OrderItemRequest(productId, 5) }
            );

            // El cliente existe
            repoMock.Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
                    .ReturnsAsync(new Customer { Id = customerId });

            // El producto tiene StockQuantity = 2
            var mockProduct = new Product("SKU", "Teclado", 50m, 2, "Desc", "INT-1") { Id = productId, IsActive = true };
            repoMock.Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
                    .ReturnsAsync(mockProduct);

            // 2. ACT
            Func<Task> act = async () => await service.CreateOrder(request);

            // 3. ASSERT
            await Assert.ThrowsAsync<InsufficientStockException>(act);

            // Verificar que no se persista la orden ni se altere el producto (no llamar a Add ni Update)
            repoMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
            repoMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        }

        // Escenario 3: Falla por cliente no encontrado
        [Fact]
        public async Task CreateOrder_WhenCustomerDoesNotExist_ThrowsCustomerNotFoundException()
        {
            // 1. ARRANGE
            var repoMock = new Mock<IRepository>();
            var loggerMock = new Mock<ILogger<OrdersManagementService>>();
            var service = new OrdersManagementService(repoMock.Object, loggerMock.Object);

            Guid customerId = Guid.NewGuid();

            var request = new OrderRequest(
                customerId,
                "Dir 1",
                "Dir 1",
                "",
                new List<OrderItemRequest> { new OrderItemRequest(Guid.NewGuid(), 1) }
            );

            // Configuramos el mock para que obtenga un cliente como null
            repoMock.Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
                    .ReturnsAsync((Customer)null);

            // 2. ACT
            Func<Task> act = async () => await service.CreateOrder(request);

            // 3. ASSERT
            await Assert.ThrowsAsync<CustomerNotFoundException>(act);

            // Comprobar que aborta de inmediato sin modificar productos ni órdenes
            repoMock.Verify(r => r.GetById<Product>(It.IsAny<Guid>(), It.IsAny<string[]>()), Times.Never);
            repoMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
            repoMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        }
    }
}