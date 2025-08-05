using ARM4.Domain.Entities;
using Domain.entities.Category;
using Domain.entities.Manufacturer;
using Domain.entities.Supplier;
using Domain.Services.IdGeneration;
using Microsoft.EntityFrameworkCore;

namespace ARM4.Infrastructure.Data
{
    public class ARM4DbContext : DbContext
    {
        public ARM4DbContext(DbContextOptions<ARM4DbContext> options) : base(options) { }

        public DbSet<Product> Products { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Manufacturer> Manufacturers { get; set; }
        // ...другие DbSet

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1) Применяем все конфигурации из сборки
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ARM4DbContext).Assembly);

            // 2) Регистрируем SQL-sequences для генерации ID
            modelBuilder.HasSequence<long>("ProductSeq")
                .StartsAt((long)IdRegister.Product * 1_000_000)
                .IncrementsBy(1);

            modelBuilder.HasSequence<long>("CategorySeq")
                .StartsAt((long)IdRegister.Category * 1_000_000)
                .IncrementsBy(1);

            modelBuilder.HasSequence<long>("SupplierSeq")
                .StartsAt((long)IdRegister.Supplier * 1_000_000)
                .IncrementsBy(1);

            modelBuilder.HasSequence<long>("ManufacturerSeq")
                .StartsAt((long)IdRegister.Manufacturer * 1_000_000)
                .IncrementsBy(1);

            // 3) Дополнительные индивидуальные настройки, если нужны
        }
    }
}
