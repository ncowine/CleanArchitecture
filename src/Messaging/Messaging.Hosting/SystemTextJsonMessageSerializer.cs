using System;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Messaging.Hosting
{
    /// <summary>
    /// The server's serializer: System.Text.Json set up so legacy apps (Newtonsoft 12, default settings) read what it
    /// writes and it reads what they write (ADR 0001, section 4). PascalCase names, case-insensitive reads, ISO 8601
    /// dates, numeric enums. Golden tests prove both directions.
    /// </summary>
    public sealed class SystemTextJsonMessageSerializer : IMessageSerializer
    {
        public static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();

        private readonly JsonSerializerOptions options;

        public SystemTextJsonMessageSerializer()
            : this(DefaultOptions)
        {
        }

        public SystemTextJsonMessageSerializer(JsonSerializerOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public string ContentType => "application/json";

        public byte[] Serialize(object message)
        {
            return message == null
                ? JsonSerializer.SerializeToUtf8Bytes<object>(null, options)
                : JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), options);
        }

        public object Deserialize(byte[] body, Type type)
        {
            return JsonSerializer.Deserialize(body, type, options);
        }

        private static JsonSerializerOptions CreateDefaultOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                // Newtonsoft's defaults: names as declared, enums as numbers, nulls written.
                PropertyNamingPolicy = null,
                PropertyNameCaseInsensitive = true,

                // Newtonsoft only escapes quotes, backslashes and control characters. The relaxed encoder keeps
                // non-ASCII text and <&> readable; "unsafe" refers to embedding in HTML, which message bodies never are.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }
    }
}
