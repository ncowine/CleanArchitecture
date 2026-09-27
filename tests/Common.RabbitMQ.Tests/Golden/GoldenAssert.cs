using Xunit;

namespace Common.RabbitMQ.Tests.Golden
{
    /// <summary>Value comparisons for payloads read back from golden bytes.</summary>
    public static class GoldenAssert
    {
        public static void Employee(Common.Events.Employee expected, Common.Events.Employee actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Department, actual.Department);
            Assert.Equal(expected.UpdatedBy, actual.UpdatedBy);
            Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
            Assert.Equal(expected.UpdatedAt.Kind, actual.UpdatedAt.Kind);
        }

        public static void TypeCoverage(GoldenPayload expected, GoldenPayload actual)
        {
            Assert.Equal(expected.Amount, actual.Amount);
            Assert.Equal(expected.WholeAmount, actual.WholeAmount);
            Assert.Equal(expected.Ratio, actual.Ratio);
            Assert.Equal(expected.WholeRatio, actual.WholeRatio);
            Assert.Equal(expected.Color, actual.Color);
            Assert.Equal(expected.Key, actual.Key);
            Assert.Equal(expected.At, actual.At);
            Assert.Equal(expected.At.Offset, actual.At.Offset);
            Assert.Null(actual.Missing);
            Assert.Equal(expected.Tags, actual.Tags);
            Assert.Equal(expected.Data, actual.Data);
            Assert.Equal(expected.Flag, actual.Flag);
            Assert.Equal(expected.Big, actual.Big);
        }
    }
}
