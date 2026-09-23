using Common.RabbitMQ;
using Xunit;

namespace CleanArch.UnitTests;

public class RabbitMqRetryBackoffTests
{
    [Fact]
    public void First_attempt_waits_exactly_the_base_delay()
    {
        var delay = RetryBackoff.Compute(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Fact]
    public void Delay_doubles_each_attempt_until_the_cap()
    {
        var baseDelay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromSeconds(60);

        Assert.Equal(TimeSpan.FromSeconds(2), RetryBackoff.Compute(1, baseDelay, maxDelay));
        Assert.Equal(TimeSpan.FromSeconds(4), RetryBackoff.Compute(2, baseDelay, maxDelay));
        Assert.Equal(TimeSpan.FromSeconds(8), RetryBackoff.Compute(3, baseDelay, maxDelay));
        Assert.Equal(TimeSpan.FromSeconds(16), RetryBackoff.Compute(4, baseDelay, maxDelay));
    }

    [Fact]
    public void Delay_never_exceeds_the_cap()
    {
        var delay = RetryBackoff.Compute(50, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(60), delay);
    }

    [Fact]
    public void An_attempt_number_below_one_is_treated_as_the_first_attempt()
    {
        var delay = RetryBackoff.Compute(0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Fact]
    public void A_very_large_attempt_number_does_not_overflow_or_throw()
    {
        var delay = RetryBackoff.Compute(int.MaxValue, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(60), delay);
    }
}
