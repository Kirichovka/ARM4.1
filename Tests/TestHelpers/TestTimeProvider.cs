using System;
using ARM4.Domain.Common;

namespace ARM4.Tests.TestHelpers
{
    /// <summary>
    /// Провайдер времени для тестов: всегда возвращает заранее заданное UtcNow.
    /// </summary>
    public class TestTimeProvider : ITimeProvider
    {
        private readonly DateTime _now;

        public TestTimeProvider(DateTime now)
        {
            _now = now;
        }

        public DateTime UtcNow => _now;
    }
}
