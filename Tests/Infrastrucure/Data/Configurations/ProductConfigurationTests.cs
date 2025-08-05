using System;
using System.Linq;
using ARM4.Domain.Entities;
using ARM4.Domain.ValueObjects;
using ARM4._1.Data.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace Tests.Infrastrucure.Data.Configurations
{
    public class ProductConfigurationTests
    {
        private readonly IMutableEntityType _entityType;

        public ProductConfigurationTests()
        {
            var modelBuilder = new ModelBuilder(new ConventionSet());
            new ProductConfiguration().Configure(modelBuilder.Entity<Product>());
            _entityType = modelBuilder.Model.FindEntityType(typeof(Product))
                         ?? throw new InvalidOperationException("Product EntityType not found");
        }

        // ---------- базовые проверки ---------------------------------------------------------

        [Fact]
        public void TableName_IsProducts() =>
            Assert.Equal("Products", _entityType.GetTableName());

        [Fact]
        public void HasCheckConstraint_PricesPositive()
        {
            var ck = _entityType.GetCheckConstraints()
                                .Single(cc => cc.Name == "CK_Product_Prices_Positive");
            Assert.Equal("[WholesalePrice] >= 0 AND [SalePrice] >= 0", ck.Sql);
        }

        [Fact]
        public void PrimaryKey_IsIdBackingField()
        {
            var pk = _entityType.FindPrimaryKey()!;
            Assert.Single(pk.Properties);
            var p = pk.Properties[0];
            Assert.Equal("id", p.Name);
            Assert.Equal("Id", p.GetColumnName());
        }

        // ---------- свойства ---------------------------------------------------------------

        [Theory]
        [InlineData("id", "Id", true, 64)]
        [InlineData("Name", "Name", true, 200)]
        [InlineData("Category", "Category", true, 100)]
        [InlineData("WholesalePrice", "WholesalePrice", true, null)]
        [InlineData("SalePrice", "SalePrice", true, null)]
        [InlineData("Quantity", "Quantity", true, null)]
        [InlineData("Barcode", "Barcode", false, 13)]
        [InlineData("Supplier", "Supplier", false, 100)]
        [InlineData("Manufacturer", "Manufacturer", false, 100)]
        [InlineData("ArrivalDate", "ArrivalDate", true, null)]
        [InlineData("ExpirationDate", "ExpirationDate", false, null)]
        [InlineData("IsDeleted", "IsDeleted", true, null)]
        [InlineData("IsArchived", "IsArchived", true, null)]
        [InlineData("DisplayCode", "DisplayCode", false, 32)]
        [InlineData("ProductUnit", "ProductUnit", false, 20)]
        public void PropertyConfiguration_Works(
            string clrName,
            string columnName,
            bool isRequired,
            int? maxLength)
        {
            var prop = _entityType.FindProperty(clrName)
                       ?? throw new InvalidOperationException($"Property '{clrName}' not found");

            Assert.Equal(columnName, prop.GetColumnName());
            Assert.Equal(isRequired, !prop.IsNullable);
            Assert.Equal(maxLength, prop.GetMaxLength());
        }

        [Fact]
        public void RowVersion_IsConcurrencyToken_AndGeneratedOnAddOrUpdate()
        {
            var prop = _entityType.FindProperty("RowVersion")!;
            Assert.True(prop.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, prop.ValueGenerated);
        }

        // ---------- конвертер для BarcodeVO -----------------------------------------------

        [Fact]
        public void BarcodeVO_Converter_CorrectlyConverts()
        {
            var prop = _entityType.GetProperties()
                                  .Single(p => p.GetColumnName() == "Barcode");

            var converter = prop.GetValueConverter();
            Assert.IsType<ValueConverter<BarcodeVO?, string?>>(converter);

            var toProviderFunc =
    (Func<BarcodeVO?, string?>)converter.ConvertToProviderExpression.Compile();

            var fromProviderFunc =
                (Func<string?, BarcodeVO?>)converter.ConvertFromProviderExpression.Compile();

            var vo = new BarcodeVO("1234567890123");

            // → база
            Assert.Equal("1234567890123", toProviderFunc(vo));
            Assert.Null(toProviderFunc(null));

            // ← CLR
            Assert.Equal(vo, fromProviderFunc("1234567890123"));
            Assert.Null(fromProviderFunc(string.Empty));
            Assert.Null(fromProviderFunc(null));
        }

        // ---------- индексы ---------------------------------------------------------------

        [Theory]
        [InlineData("IX_Products_Barcode", true)]
        [InlineData("IX_Products_DisplayCode", true)]
        [InlineData("IX_Products_Name", false)]
        [InlineData("IX_Products_Supplier", false)]
        [InlineData("IX_Products_Category", false)]
        [InlineData("IX_Products_Manufacturer", false)]
        public void Indexes_AreConfigured(string indexName, bool isUnique)
        {
            var idx = _entityType.GetIndexes()
                                 .Single(i => i.GetDatabaseName() == indexName);
            Assert.Equal(isUnique, idx.IsUnique);
        }

        // ---------- enum ProductUnit ------------------------------------------------------

        [Fact]
        public void ProductUnit_IsStoredAsString()
        {
            var prop = _entityType.FindProperty("ProductUnit")!;
            Assert.Equal(typeof(Product.Unit), prop.ClrType);

            var conv = prop.GetValueConverter()!;
            Assert.Equal(typeof(string), conv.ProviderClrType);
            Assert.Equal("Piece", conv.ConvertToProvider(Product.Unit.Piece));
        }
    }
}
