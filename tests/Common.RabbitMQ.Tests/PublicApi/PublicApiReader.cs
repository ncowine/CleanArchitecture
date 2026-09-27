using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Common.RabbitMQ.Tests.PublicApi
{
    /// <summary>
    /// Lists an assembly's public and protected API as one line per type and member. Type names leave out the
    /// assembly, so two builds with the same namespaces compare equal.
    /// </summary>
    public static class PublicApiReader
    {
        private const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static HashSet<string> Read(Assembly assembly)
        {
            HashSet<string> api = new HashSet<string>(StringComparer.Ordinal);

            foreach (Type type in assembly.GetExportedTypes())
            {
                api.Add(DescribeType(type));

                // One line per interface, so implementing an extra interface reads as an addition, not a change.
                foreach (Type implemented in type.GetInterfaces())
                {
                    api.Add($"implements {Name(type)} : {Name(implemented)}");
                }

                foreach (ConstructorInfo constructor in type.GetConstructors(Declared).Where(c => IsVisible(c)))
                {
                    api.Add($"ctor {Name(type)}({Parameters(constructor)})");
                }

                foreach (MethodInfo method in type.GetMethods(Declared).Where(m => IsVisible(m) && !m.IsSpecialName))
                {
                    string modifiers = (method.IsStatic ? "static " : "") + (method.IsAbstract ? "abstract " : method.IsVirtual && !method.IsFinal ? "virtual " : "");
                    string generics = method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(Name)) + ">" : "";
                    api.Add($"method {modifiers}{Name(type)}.{method.Name}{generics}({Parameters(method)}) : {Name(method.ReturnType)}");
                }

                foreach (PropertyInfo property in type.GetProperties(Declared))
                {
                    string accessors = Accessor("get", property.GetMethod) + Accessor("set", property.SetMethod);
                    if (accessors.Length > 0)
                    {
                        api.Add($"property {Name(type)}.{property.Name} : {Name(property.PropertyType)} {{{accessors} }}");
                    }
                }

                foreach (EventInfo evt in type.GetEvents(Declared).Where(e => IsVisible(e.AddMethod)))
                {
                    api.Add($"event {Name(type)}.{evt.Name} : {Name(evt.EventHandlerType)}");
                }

                foreach (FieldInfo field in type.GetFields(Declared).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                {
                    string value = field.IsLiteral ? " = " + field.GetRawConstantValue() : "";
                    api.Add($"field {(field.IsStatic ? "static " : "")}{Name(type)}.{field.Name} : {Name(field.FieldType)}{value}");
                }
            }

            return api;
        }

        private static string DescribeType(Type type)
        {
            string kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class";
            string modifiers = type.IsAbstract && type.IsSealed ? "static " : type.IsAbstract && !type.IsInterface ? "abstract " : type.IsSealed && !type.IsValueType ? "sealed " : "";
            List<string> bases = new List<string>();
            if (type.BaseType != null && type.BaseType != typeof(object) && !type.IsValueType)
            {
                bases.Add(Name(type.BaseType));
            }

            string attributes = string.Join("", type.GetCustomAttributesData().Select(a => $"[{Name(a.AttributeType)}({string.Join(", ", a.ConstructorArguments.Select(c => c.ToString()))})]"));

            return $"{attributes}{modifiers}{kind} {Name(type)}" + (bases.Count > 0 ? " : " + string.Join(", ", bases) : "");
        }

        private static string Accessor(string name, MethodInfo accessor)
        {
            return accessor != null && IsVisible(accessor) ? " " + name + ";" : "";
        }

        private static bool IsVisible(MethodBase method)
        {
            return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
        }

        private static string Parameters(MethodBase method)
        {
            return string.Join(", ", method.GetParameters().Select(p =>
                (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + Name(p.ParameterType) + (p.HasDefaultValue ? " = " + (p.DefaultValue ?? "null") : "")));
        }

        private static string Name(Type type)
        {
            if (type.IsByRef)
            {
                return Name(type.GetElementType());
            }

            if (type.IsArray)
            {
                return Name(type.GetElementType()) + "[]";
            }

            if (type.IsGenericParameter)
            {
                return type.Name;
            }

            string name = type.IsNested ? Name(type.DeclaringType) + "." + type.Name : type.Namespace + "." + type.Name;
            if (!type.IsGenericType)
            {
                return name;
            }

            int tick = name.IndexOf('`');
            return (tick < 0 ? name : name.Substring(0, tick)) + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
        }
    }
}
