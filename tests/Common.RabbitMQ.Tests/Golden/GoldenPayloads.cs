using System;
using System.Collections.Generic;
using Common.Events;

namespace Common.RabbitMQ.Tests.Golden
{
    /// <summary>The objects behind the golden files. Shared by the Newtonsoft and System.Text.Json tests.</summary>
    public static class GoldenPayloads
    {
        /// <summary>Golden/employee.json</summary>
        public static Employee Employee()
        {
            return new Employee
            {
                Id = 7,
                Name = "Ada Lovelace",
                Department = "Engineering",
                UpdatedBy = "WpfApp.Net472",
                UpdatedAt = new DateTime(2026, 9, 26, 10, 15, 30, 123, DateTimeKind.Unspecified),
            };
        }

        /// <summary>Golden/employee-utc-nulls.json</summary>
        public static Employee EmployeeUtcAndNulls()
        {
            return new Employee
            {
                Id = 0,
                Name = null,
                Department = null,
                UpdatedBy = "",
                UpdatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            };
        }

        /// <summary>Golden/employee-escaping.json</summary>
        public static Employee EmployeeEscaping()
        {
            // Newtonsoft escapes quotes, backslashes and control characters only; non-ASCII and <&> are written as is.
            return new Employee
            {
                Id = 1,
                Name = "Zoë \"Z\" <&> \\ é\n\t",
                Department = "R&D",
                UpdatedBy = "a/b",
                UpdatedAt = new DateTime(2026, 9, 26),
            };
        }

        /// <summary>Golden/type-coverage.json (Newtonsoft) and Golden/type-coverage.stj.json (System.Text.Json).</summary>
        public static GoldenPayload TypeCoverage()
        {
            return new GoldenPayload
            {
                Amount = 12.50m,
                WholeAmount = 12m,
                Ratio = 1.5,
                WholeRatio = 2d,
                Color = GoldenColor.Blue,
                Key = new Guid("3f2504e0-4f89-11d3-9a0c-0305e82c3301"),
                At = new DateTimeOffset(2026, 9, 26, 10, 15, 30, TimeSpan.FromHours(2)),
                Missing = null,
                Tags = new List<string> { "a", "b" },
                Data = new byte[] { 1, 2, 3 },
                Flag = true,
                Big = 9007199254740993L,
            };
        }
    }
}
