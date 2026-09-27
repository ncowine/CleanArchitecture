using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Common.RabbitMQ.Tests.Golden
{
    /// <summary>
    /// Golden wire-format files: the exact bytes the current build puts on the wire. They are the frozen contract
    /// (ADR 0001, section 3). Never regenerate them to make a test pass.
    /// </summary>
    public static class GoldenFile
    {
        public static byte[] Read(string name)
        {
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", name));
        }

        /// <summary>The body exactly as <c>RabbitMQService.Publish</c> builds it.</summary>
        public static byte[] LegacyBody(object payload)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
        }

        /// <summary>The payload exactly as <c>RabbitMQService</c> reads it on receive.</summary>
        public static T FromLegacyBody<T>(byte[] body)
        {
            return (T)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body), typeof(T));
        }
    }
}
