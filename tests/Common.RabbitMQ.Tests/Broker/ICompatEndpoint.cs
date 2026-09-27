using System;
using Compat.Events;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>One running <c>RabbitMQService</c> of either build, joined to the test bus with <see cref="CompatEvent"/> registered.</summary>
    public interface ICompatEndpoint : IDisposable
    {
        CompatBuild Build { get; }

        string InstanceId { get; }

        /// <summary>The consumer queue name, as the service builds it.</summary>
        string QueueName { get; }

        bool IsConnected { get; }

        /// <summary>Every <see cref="CompatEvent"/> raised on this endpoint's event aggregator, local or remote.</summary>
        ReceivedPayloads Received { get; }

        /// <summary><c>GetEvent&lt;CompatEvent&gt;().PublishRemote(payload, service)</c>.</summary>
        void PublishRemote(CompatPayload payload);
    }
}
