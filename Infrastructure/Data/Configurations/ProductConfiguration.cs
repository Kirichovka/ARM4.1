using ARM4.Domain.Entities;
using ARM4.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ARM4._1.Data.Configurations
{
    public class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.ToTable("Products", t =>
            {
                t.HasCheckConstraint("CK_Product_Prices_Positive", "[WholesalePrice] >= 0 AND [SalePrice] >= 0");
            });
            builder.Property<string>("id")
                .HasColumnName("Id")
                .IsRequired()
                .HasMaxLength(64);
            builder.HasKey("id");

            builder.Property(p => p.Name)
                .HasColumnName("Name")
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(p => p.Category)
                .HasColumnName("Category")
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(p => p.GetWholesalePrice())
                .HasColumnName("WholesalePrice")
                .IsRequired();

            builder.Property(p => p.GetSalePrice())
                .HasColumnName("SalePrice")
                .IsRequired();

            builder.Property(p => p.GetQuantity())
                .HasColumnName("Quantity")
                .IsRequired();

            var barcodeConverter = new ValueConverter<BarcodeVO?, string?>(
                vo => vo != null ? vo.Value : null,
                s => string.IsNullOrWhiteSpace(s) ? null : new BarcodeVO(s)
            );
            builder.Property(p => p.GetBarcode())
                .HasColumnName("Barcode")
                .HasMaxLength(13)
                .HasConversion(barcodeConverter);

            builder.Property(p => p.GetSupplier())
                .HasColumnName("Supplier")
                .HasMaxLength(100);

            builder.Property(p => p.GetManufacturer())
                .HasColumnName("Manufacturer")
                .HasMaxLength(100);

            builder.Property(p => p.GetArrivalDate())
                .HasColumnName("ArrivalDate")
                .IsRequired();

            builder.Property(p => p.GetExpirationDate())
                .HasColumnName("ExpirationDate");

            builder.Property(p => p.GetIsDeleted())
                .HasColumnName("IsDeleted")
                .IsRequired();

            builder.Property(p => p.GetIsArchived())
                .HasColumnName("IsArchived")
                .IsRequired();

            builder.Property(p => p.GetDisplayCode())
                .HasColumnName("DisplayCode")
                .HasMaxLength(32);

            builder.Property(p => p.GetProductUnit())
                .HasColumnName("ProductUnit")
                .HasConversion<string>()
                .HasMaxLength(20);
            builder.Property(p => p.RowVersion)
            .HasColumnName("RowVersion")
            .IsRowVersion()            // <-- делает поле concurrency token
            .IsConcurrencyToken();

            builder.HasIndex(p => p.GetBarcode()).HasDatabaseName("IX_Products_Barcode").IsUnique();
            builder.HasIndex(p => p.GetDisplayCode()).HasDatabaseName("IX_Products_DisplayCode").IsUnique();
            builder.HasIndex(p => p.GetName()).HasDatabaseName("IX_Products_Name");
            builder.HasIndex(p => p.GetSupplier()).HasDatabaseName("IX_Products_Supplier");
            builder.HasIndex(p => p.GetCategory()).HasDatabaseName("IX_Products_Category");
            builder.HasIndex(p => p.GetManufacturer()).HasDatabaseName("IX_Products_Manufacturer");
        }
    }
}
