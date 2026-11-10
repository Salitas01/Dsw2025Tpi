using System;
using System.IO;
using System.Linq.Expressions;
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
    public class ProductsManagementServiceTests
    {
        // --------------------------------------------------------
        // TESTS DE CREACIÓN DE PRODUCTOS (ADMIN)
        // --------------------------------------------------------

        // Escenario 1: Camino feliz
        [Fact]
        public async Task AddProduct_WithValidData_ReturnsProductAndCallsRepositoryAdd()
        {
            // 1. ARRANGE
            var repoMock = new Mock<IRepository>();
            var loggerMock = new Mock<ILogger<ProductsManagementService>>();
            var service = new ProductsManagementService(repoMock.Object, loggerMock.Object);

            var request = new ProductModel.Request("SKU-123", "INT-123", "Teclado", "Teclado Mecánico", 1500.50m, 10);
            var productoEsperado = new Product("SKU-123", "Teclado", 1500.50m, 10, "Teclado Mecánico", "INT-123");

            // Simulamos que First devuelve null (No existe un producto con ese SKU)
            repoMock.Setup(r => r.First(It.IsAny<Expression<Func<Product, bool>>>()))
                    .ReturnsAsync((Product)null);

            // Simulamos que el Add se ejecuta correctamente
            repoMock.Setup(r => r.Add(It.IsAny<Product>()))
                    .ReturnsAsync(productoEsperado);

            // 2. ACT
            var resultado = await service.AddProduct(request);

            // 3. ASSERT
            Assert.NotNull(resultado);
            Assert.Equal(request.Sku, resultado.Sku);

            // Verificamos que se haya invocado a la función Add exactamente una vez
            repoMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Once);
        }

        // Escenario 2: Falla por SKU duplicado
        [Fact]
        public async Task AddProduct_WithDuplicateSku_ThrowsDuplicatedEntityException()
        {
            // 1. ARRANGE
            var repoMock = new Mock<IRepository>();
            var loggerMock = new Mock<ILogger<ProductsManagementService>>();
            var service = new ProductsManagementService(repoMock.Object, loggerMock.Object);

            var request = new ProductModel.Request("SKU-DUPLICADO", "INT-001", "Mouse", "Mouse Gamer", 2000m, 5);
            var productoExistente = new Product("SKU-DUPLICADO", "Otro Producto", 100m, 1, "Desc", "INT-999");

            // Configuramos el mock para que SIMULE que la búsqueda encuentra un producto (Ya existe)
            repoMock.Setup(r => r.First(It.IsAny<Expression<Func<Product, bool>>>()))
                    .ReturnsAsync(productoExistente);

            // 2. ACT
            Func<Task> act = async () => await service.AddProduct(request);

            // 3. ASSERT
            await Assert.ThrowsAsync<DuplicatedEntityException>(act);

            // Verificamos que NUNCA se llame a la función Add porque debe fallar antes
            repoMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        }

        // Escenario 3: Falla por reglas de dominio (Stock <= 0 o Precio < 0)
        [Theory]
        [InlineData(-10.0, 5)]   // Precio negativo
        [InlineData(100.0, 0)]   // Stock cero
        [InlineData(100.0, -5)]  // Stock negativo
        public async Task AddProduct_WithInvalidStockOrPrice_ThrowsInvalidDataException(decimal precioInvalido, int stockInvalido)
        {
            // 1. ARRANGE
            var repoMock = new Mock<IRepository>();
            var loggerMock = new Mock<ILogger<ProductsManagementService>>();
            var service = new ProductsManagementService(repoMock.Object, loggerMock.Object);

            var request = new ProductModel.Request("SKU-TEST", "INT-TEST", "Monitor", null, precioInvalido, stockInvalido);

            // 2. ACT
            Func<Task> act = async () => await service.AddProduct(request);

            // 3. ASSERT
            await Assert.ThrowsAsync<InvalidDataException>(act);

            // Verificamos que aborta de inmediato: No debe buscar duplicados ni intentar guardar
            repoMock.Verify(r => r.First(It.IsAny<Expression<Func<Product, bool>>>()), Times.Never);
            repoMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        }

        // --------------------------------------------------------
        // TESTS DE LISTADO DE PRODUCTOS (ADMIN / PÚBLICO)
        // --------------------------------------------------------

        // Escenario 1: Catálogo público excluye inactivos
        [Fact]
        public async Task GetProducts_WhenIncludeInactiveIsFalse_ReturnsOnlyActiveProducts()
        {
            // 1. ARRANGE
            var repoMock = new Mock<IRepository>();
            var loggerMock = new Mock<ILogger<ProductsManagementService>>();
            var service = new ProductsManagementService(repoMock.Object, loggerMock.Object);

            // Creamos una lista mixta
            var listaMixta = new List<Product>
            {
                new Product("SKU-1", "Prod 1", 100, 10, "", "") { Id = Guid.NewGuid(), IsActive = true },
                new Product("SKU-2", "Prod 2", 100, 10, "", "") { Id = Guid.NewGuid(), IsActive = false }, // Inactivo
                new Product("SKU-3", "Prod 3", 100, 10, "", "") { Id = Guid.NewGuid(), IsActive = true }
            };

            // Simulamos que el repositorio evalúa la expresión lambda (filtro) en nuestra lista en memoria
            repoMock.Setup(r => r.GetFiltered(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
                    .ReturnsAsync((Expression<Func<Product, bool>> predicate, string[] includes) =>
                    {
                        return listaMixta.AsQueryable().Where(predicate).ToList();
                    });

            // 2. ACT
            // Llamamos a GetProducts, que es el endpoint público
            var resultado = await service.GetProducts();

            // 3. ASSERT
            Assert.NotNull(resultado);
            var listaResultado = resultado.ToList();
            Assert.Equal(2, listaResultado.Count); // Solo deben quedar 2
            Assert.DoesNotContain(listaResultado, p => p.IsActive == false); // Ninguno debe ser inactivo
        }

        // Escenario 2: Paginación matemática
        [Fact]
        public async Task GetProducts_WithPaginationFilter_AppliesSkipAndTakeCorrectly()
        {
            // 1. ARRANGE
            var repoMock = new Mock<IRepository>();
            var loggerMock = new Mock<ILogger<ProductsManagementService>>();
            var service = new ProductsManagementService(repoMock.Object, loggerMock.Object);

            // Generamos 15 productos de prueba
            var listaQuince = new List<Product>();
            for (int i = 1; i <= 15; i++)
            {
                listaQuince.Add(new Product($"SKU-{i}", $"Prod {i}", 10, 5, "", $"INT-{i}") { Id = Guid.NewGuid(), IsActive = true });
            }

            repoMock.Setup(r => r.GetFiltered(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
                    .ReturnsAsync(listaQuince); // El repo devuelve los 15

            // Solicitamos Página 2, Tamaño 5
            var request = new ProductModel.FilterProduct(Status: null, SearchTerm: null, PageNumber: 2, PageSize: 5);

            // 2. ACT
            var resultado = await service.GetAuthProducts(request);

            // 3. ASSERT
            Assert.NotNull(resultado);
            Assert.Equal(15, resultado.TotalCount); // El total reportado debe ser 15
            Assert.Equal(5, resultado.Items.Count); // Solo debe devolver 5 elementos
            Assert.Equal("SKU-14", resultado.Items.First().Sku); // El primer elemento de la pág 2 (ordenado alfabéticamente)
        }

        // Escenario 3: Búsqueda por coincidencia parcial (SearchTerm)
        [Fact]
        public async Task GetProducts_WithSearchTerm_FiltersByNameSkuOrInternalCode()
        {
            // 1. ARRANGE
            var repoMock = new Mock<IRepository>();
            var loggerMock = new Mock<ILogger<ProductsManagementService>>();
            var service = new ProductsManagementService(repoMock.Object, loggerMock.Object);

            var listaPrueba = new List<Product>
            {
                new Product("SKU-MG", "Mouse Gamer", 100, 10, "", "INT-01") { Id = Guid.NewGuid(), IsActive = true },
                new Product("SKU-TM", "Teclado Mecánico", 100, 10, "", "INT-02") { Id = Guid.NewGuid(), IsActive = true },
                new Product("SKU-MO", "Monitor", 100, 10, "", "INT-03") { Id = Guid.NewGuid(), IsActive = true }
            };

            // Compilamos la lambda del servicio para filtrar la lista falsa
            repoMock.Setup(r => r.GetFiltered(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
                    .ReturnsAsync((Expression<Func<Product, bool>> predicate, string[] includes) =>
                    {
                        return listaPrueba.AsQueryable().Where(predicate).ToList();
                    });

            var request = new ProductModel.FilterProduct(Status: null, SearchTerm: "mouse", PageNumber: 1, PageSize: 10);

            // 2. ACT
            var resultado = await service.GetAuthProducts(request);

            // 3. ASSERT
            Assert.NotNull(resultado);
            Assert.Equal(1, resultado.Items.Count);

            // Verificamos que ignoró mayúsculas/minúsculas y trajo el correcto
            Assert.Equal("Mouse Gamer", resultado.Items.First().Name);
        }
    }
}