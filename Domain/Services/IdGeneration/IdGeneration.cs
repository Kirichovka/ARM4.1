using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARM4.Infrastructure.Data;

namespace Domain.Services.IdGeneration
{
    public interface IIdGenerator
    {
        Task<long> GenerateAsync(IdRegister register, CancellationToken ct = default);
    }

    public class SqlServerIdGenerator : IIdGenerator
    {
        private readonly ARM4DbContext _db;
        public SqlServerIdGenerator(ARM4DbContext db) => _db = db;

        public async Task<long> GenerateAsync(IdRegister register, CancellationToken ct = default)
        {
            var seqName = register switch
            {
                IdRegister.Product => "ProductSeq",
                IdRegister.Category => "CategorySeq",
                IdRegister.Supplier => "SupplierSeq",
                IdRegister.Manufacturer => "ManufacturerSeq",
                _ => throw new ArgumentOutOfRangeException(nameof(register))
            };

            // Выполняем SQL: SELECT NEXT VALUE FOR [seqName]
            var raw = await _db.Database
                .SqlQuery<long>($"SELECT NEXT VALUE FOR [{seqName}]")
                .FirstAsync(ct);

            return raw;
        }
    }

}
