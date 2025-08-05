using ARM4.Domain.Builders;
using ARM4.Domain.Common;
using ARM4.Domain.Entities;
using ARM4.Domain.Interfaces;
using ARM4.Infrastructure.Data;
using ARM4.Infrastructure.Repositories;
using ARM4.Logging.InfraErrorCodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ARM4.Tests.Repositories
{
    public class ProductRepositoryTests : IClassFixture<ServiceProviderFixture>
    {
        protected readonly ServiceProviderFixture Fixture;
        public ProductRepositoryTests(ServiceProviderFixture fixture)
        {
            Fixture = fixture;
        }
    }
}
