using System;
using ARM4.Domain.Common;

namespace ARM4.Tests.TestHelpers
{
    /// <summary>
    /// ��������� ������� ��� ������: ������ ���������� ������� �������� UtcNow.
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
