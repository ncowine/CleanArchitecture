using Common.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Golden
{
    /// <summary>
    /// Message bodies: UTF-8 JSON as Newtonsoft 12 writes it with default settings (ADR 0001, sections 3 and 4).
    /// Each case checks both directions: the payload writes the golden bytes, and the golden bytes read back to the payload.
    /// </summary>
    public class PayloadSerializationTests
    {
        [Fact]
        public void Employee()
        {
            AssertEmployeeGolden("employee.json", GoldenPayloads.Employee());
        }

        [Fact]
        public void Employee_UtcDateAndNulls()
        {
            AssertEmployeeGolden("employee-utc-nulls.json", GoldenPayloads.EmployeeUtcAndNulls());
        }

        [Fact]
        public void Employee_Escaping()
        {
            AssertEmployeeGolden("employee-escaping.json", GoldenPayloads.EmployeeEscaping());
        }

        [Fact]
        public void TypeCoverage()
        {
            GoldenPayload payload = GoldenPayloads.TypeCoverage();

            AssertGolden("type-coverage.json", payload);
            GoldenAssert.TypeCoverage(payload, GoldenFile.FromLegacyBody<GoldenPayload>(GoldenFile.Read("type-coverage.json")));
        }

        [Fact]
        public void CompatPayload()
        {
            // The broker tests compare what the service actually sends against this file.
            AssertGolden("compat-payload.json", CompatPayloads.Golden());
        }

        private static void AssertEmployeeGolden(string file, Employee employee)
        {
            AssertGolden(file, employee);
            GoldenAssert.Employee(employee, GoldenFile.FromLegacyBody<Employee>(GoldenFile.Read(file)));
        }

        private static void AssertGolden(string file, object payload)
        {
            Assert.Equal(GoldenFile.Read(file), GoldenFile.LegacyBody(payload));
        }
    }
}
