using ARM4.Domain.Common;
using System;

namespace Tests.Domain.Common
{
    public class TestTimeProvider : ITimeProvider
    {
        public DateTime UtcNow { get; set; }
        public TestTimeProvider(DateTime fixedTime) { UtcNow = fixedTime; }
    }
}
