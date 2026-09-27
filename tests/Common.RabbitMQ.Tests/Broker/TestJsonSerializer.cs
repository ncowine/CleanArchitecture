using System;
using System.Text;
using Messaging;
using Newtonsoft.Json;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>Newtonsoft with default settings, the same body format as the legacy builds.</summary>
    public sealed class TestJsonSerializer : IMessageSerializer
    {
        public static readonly TestJsonSerializer Instance = new TestJsonSerializer();

        public string ContentType => "application/json";

        public byte[] Serialize(object message)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
        }

        public object Deserialize(byte[] body, Type type)
        {
            return JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body), type);
        }
    }
}
