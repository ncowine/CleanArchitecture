namespace Common.RabbitMQ.Tests.Broker
{
    public enum CompatBuild
    {
        /// <summary>src/Common.RabbitMQ: the build being refactored.</summary>
        Current,

        /// <summary>
        /// tests/Fixtures/Common.RabbitMQ.Baseline: the original refactor, frozen. It routes by event name, unlike the real
        /// legacy library (which routes by application name), so it only takes part in checks of its own headers and body.
        /// </summary>
        Baseline,

        /// <summary>
        /// Messaging.RabbitMQ used directly, as the server will: no Prism, so publishing has no local subscribers.
        /// </summary>
        Core,
    }
}
