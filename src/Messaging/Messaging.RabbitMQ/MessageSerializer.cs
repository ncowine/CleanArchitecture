using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Messaging.RabbitMQ
{
    internal static class MessageSerializer
    {
        public const string ContentType = "application/json";

        private static readonly JsonSerializerOptions options = CreateOptions();

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
                PropertyNamingPolicy = null,
                PropertyNameCaseInsensitive = true,

                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            jsonOptions.MakeReadOnly(populateMissingResolver: true);
            return jsonOptions;
        }
    }
}
