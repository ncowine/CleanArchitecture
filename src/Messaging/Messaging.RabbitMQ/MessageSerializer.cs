using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Messaging.RabbitMQ
{
    /// <summary>
    /// The one serializer, for legacy and modern apps alike: System.Text.Json set up so apps still on the old library
    /// (Newtonsoft 12, default settings) read what it writes and it reads what they write (ADR 0001, section 4).
    /// PascalCase names, case-insensitive reads, ISO 8601 dates, numeric enums. Golden tests prove both directions.
    /// </summary>
    /// <remarks>
    /// Payloads are classes with public <c>{ get; set; }</c> properties, or primitives (int, Guid, bool, string, enums,
    /// dates). Tuples are rejected (<see cref="EnsureNoTuples"/>): their items are fields, which System.Text.Json skips,
    /// so they would arrive empty.
    /// </remarks>
    public static class MessageSerializer
    {
        /// <summary>Sent as the content-type property of every message.</summary>
        public const string ContentType = "application/json";

        private static readonly JsonSerializerOptions options = CreateOptions();

        /// <summary>Serializes <paramref name="message"/> using its runtime type.</summary>
        public static byte[] Serialize(object message)
        {
            return message == null
                ? JsonSerializer.SerializeToUtf8Bytes<object>(null, options)
                : JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), options);
        }

        public static object Deserialize(byte[] body, Type type)
        {
            return JsonSerializer.Deserialize(body, type, options);
        }

        /// <summary>
        /// Throws when <paramref name="payloadType"/> is a tuple or holds one: in a property, a collection or a generic
        /// argument. Called when a message type is first used, so a tuple fails at once instead of arriving empty.
        /// </summary>
        public static void EnsureNoTuples(Type payloadType)
        {
            if (payloadType == null)
            {
                throw new ArgumentNullException(nameof(payloadType));
            }

            Type tuple = FindTuple(payloadType, new HashSet<Type>());
            if (tuple != null)
            {
                string where = tuple == payloadType ? "is" : "contains";
                throw new InvalidOperationException(
                    $"{payloadType.FullName} {where} a tuple ({tuple.Name}). Tuple items are fields, which System.Text.Json " +
                    "doesn't serialize, so they would arrive empty. Use a class with { get; set; } properties.");
            }
        }

        private static Type FindTuple(Type type, HashSet<Type> seen)
        {
            if (!seen.Add(type))
            {
                return null;
            }

            if (IsTuple(type))
            {
                return type;
            }

            if (type.IsArray)
            {
                return FindTuple(type.GetElementType(), seen);
            }

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    Type found = FindTuple(argument, seen);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            // The framework's own types (string, DateTime, List<T>...) hold none of your tuples.
            if (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal))
            {
                return null;
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Type found = FindTuple(property.PropertyType, seen);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool IsTuple(Type type)
        {
            if (!type.IsGenericType)
            {
                return false;
            }

            string name = type.GetGenericTypeDefinition().FullName;
            return name.StartsWith("System.ValueTuple`", StringComparison.Ordinal) || name.StartsWith("System.Tuple`", StringComparison.Ordinal);
        }

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions
            {
                // Newtonsoft's defaults: names as declared, enums as numbers, nulls written.
                PropertyNamingPolicy = null,
                PropertyNameCaseInsensitive = true,

                // Newtonsoft only escapes quotes, backslashes and control characters. The relaxed encoder keeps
                // non-ASCII text and <&> readable; "unsafe" refers to embedding in HTML, which message bodies never are.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            jsonOptions.MakeReadOnly(populateMissingResolver: true);
            return jsonOptions;
        }
    }
}
