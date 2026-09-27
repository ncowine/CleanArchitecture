using System.Text;
using Common.Events;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Messaging.Hosting;
using Xunit;
using ServerEmployeeUpdated = Employees.Contracts.EmployeeUpdated;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// The server's System.Text.Json serializer against the legacy Newtonsoft 12 golden files, in both directions
    /// (ADR 0001, section 4). Employee bodies are byte-identical. Whole decimals and doubles are the one known
    /// difference (12 vs 12.0), pinned in type-coverage.stj.json; Newtonsoft reads them back unchanged.
    /// </summary>
    public class SystemTextJsonGoldenTests
    {
        private static readonly SystemTextJsonMessageSerializer serializer = new SystemTextJsonMessageSerializer();

        [Theory]
        [InlineData("employee.json")]
        [InlineData("employee-utc-nulls.json")]
        [InlineData("employee-escaping.json")]
        public void Employee_WritesTheLegacyBytes_AndReadsThem(string file)
        {
            Employee employee = GoldenEmployee(file);

            Assert.Equal(GoldenFile.Read(file), serializer.Serialize(employee));
            GoldenAssert.Employee(employee, (Employee)serializer.Deserialize(GoldenFile.Read(file), typeof(Employee)));
        }

        [Fact]
        public void ServerContract_WritesTheLegacyEventsBytes()
        {
            // The server never references Common.Events; its own contract must produce the same body.
            Employee employee = GoldenPayloads.Employee();
            ServerEmployeeUpdated message = new ServerEmployeeUpdated
            {
                Id = employee.Id,
                Name = employee.Name,
                Department = employee.Department,
                UpdatedBy = employee.UpdatedBy,
                UpdatedAt = employee.UpdatedAt,
            };

            Assert.Equal(GoldenFile.Read("employee.json"), serializer.Serialize(message));
        }

        [Fact]
        public void CompatPayload_WritesTheLegacyBytes()
        {
            CompatPayload payload = CompatPayloads.Golden();
            CompatMessage message = new CompatMessage { Id = payload.Id, Name = payload.Name, UpdatedAt = payload.UpdatedAt };

            Assert.Equal(GoldenFile.Read("compat-payload.json"), serializer.Serialize(message));
        }

        [Fact]
        public void TypeCoverage_BothDirections()
        {
            GoldenPayload payload = GoldenPayloads.TypeCoverage();

            // Server → legacy: the pinned System.Text.Json bytes, read by Newtonsoft 12.
            byte[] written = serializer.Serialize(payload);
            Assert.Equal(GoldenFile.Read("type-coverage.stj.json"), written);
            GoldenAssert.TypeCoverage(payload, GoldenFile.FromLegacyBody<GoldenPayload>(written));

            // Legacy → server: Newtonsoft 12's bytes, read by System.Text.Json.
            GoldenAssert.TypeCoverage(payload, (GoldenPayload)serializer.Deserialize(GoldenFile.Read("type-coverage.json"), typeof(GoldenPayload)));
        }

        [Fact]
        public void Reads_CaseInsensitively()
        {
            byte[] body = Encoding.UTF8.GetBytes("{\"id\":7,\"NAME\":\"Ada\"}");

            Employee employee = (Employee)serializer.Deserialize(body, typeof(Employee));

            Assert.Equal(7, employee.Id);
            Assert.Equal("Ada", employee.Name);
        }

        private static Employee GoldenEmployee(string file)
        {
            switch (file)
            {
                case "employee.json":
                    return GoldenPayloads.Employee();
                case "employee-utc-nulls.json":
                    return GoldenPayloads.EmployeeUtcAndNulls();
                default:
                    return GoldenPayloads.EmployeeEscaping();
            }
        }
    }
}
