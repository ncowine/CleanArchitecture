using System;
using System.Text;
using Messaging;
using Newtonsoft.Json;

namespace Common.RabbitMQ
{
    /// <summary>
    /// UTF-8 JSON exactly as the legacy apps write and read it: Newtonsoft with default settings (ADR 0001, section 3).
    /// </summary>
    internal sealed class NewtonsoftMessageSerializer : IMessageSerializer
    {
        public static readonly NewtonsoftMessageSerializer Instance = new NewtonsoftMessageSerializer();

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
