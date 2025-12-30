using AgentCore.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace AgentCore.Tools
{
    public interface IToolCatalog
    {
        IReadOnlyList<Tool> RegisteredTools { get; }
        Tool? Get(string toolName);
        bool Contains(string toolName);
    }

    public interface IToolRegistry
    {
        void Register(params Delegate[] funcs);
        void RegisterAll<T>();
        void RegisterAll<T>(T instance);
    }

    internal sealed class ToolRegistryCatalog : IToolRegistry, IToolCatalog
    {
        private readonly List<Tool> _registeredTools;

        public ToolRegistryCatalog(IEnumerable<Tool>? tools = null)
        {
            _registeredTools = tools != null
                ? new List<Tool>(tools)
                : new List<Tool>();
        }

        public IReadOnlyList<Tool> RegisteredTools => _registeredTools;

        public static implicit operator ToolRegistryCatalog(List<Tool> tools)
            => new ToolRegistryCatalog(tools);

        public void Register(params Delegate[] funcs)
        {
            if (funcs == null)
                throw new ArgumentNullException(nameof(funcs));

            foreach (var f in funcs)
            {
                if (f == null)
                    throw new ArgumentNullException(nameof(funcs), "Delegate cannot be null.");

                if (!IsMethodJsonCompatible(f.Method))
                    continue;

                var tool = CreateToolFromDelegate(f);

                if (_registeredTools.Any(t =>
                    t.Name.Equals(tool.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"Duplicate tool name exposed to LLM: '{tool.Name}'. " +
                        $"Tool names must be globally unique.");
                }

                _registeredTools.Add(tool);
            }
        }

        public void RegisterAll<T>()
        {
            var methods = typeof(T)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<ToolAttribute>() != null);

            foreach (var method in methods)
            {
                if (!IsMethodJsonCompatible(method))
                    continue;

                try
                {
                    var paramTypes = method.GetParameters()
                        .Select(p => p.ParameterType)
                        .Concat(new[] { method.ReturnType })
                        .ToArray();

                    var del = Delegate.CreateDelegate(
                        Expression.GetDelegateType(paramTypes),
                        method
                    );

                    Register(del);
                }
                catch
                {
                    // skip incompatible methods
                }
            }
        }

        public void RegisterAll<T>(T instance)
        {
            var methods = typeof(T)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<ToolAttribute>() != null);

            foreach (var method in methods)
            {
                if (!IsMethodJsonCompatible(method))
                    continue;

                try
                {
                    var paramTypes = method.GetParameters()
                        .Select(p => p.ParameterType)
                        .Concat(new[] { method.ReturnType })
                        .ToArray();

                    var del = Delegate.CreateDelegate(
                        Expression.GetDelegateType(paramTypes),
                        instance,
                        method,
                        throwOnBindFailure: false
                    );

                    if (del != null)
                        Register(del);
                }
                catch
                {
                    // skip incompatible methods
                }
            }
        }

        public bool Contains(string toolName)
            => _registeredTools.Any(t =>
                t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));

        public Tool? Get(string toolName)
            => _registeredTools.FirstOrDefault(t =>
                t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));

        private static Tool CreateToolFromDelegate(Delegate func)
        {
            var method = func.Method;

            var declaringType = method.DeclaringType
                ?? throw new InvalidOperationException("Tool method has no declaring type.");

            // globally unique name
            var toolName = $"{declaringType.Name}.{method.Name}";

            var description =
                method.GetCustomAttribute<ToolAttribute>()?.Description
                ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? toolName;

            var properties = new JObject();
            var required = new JArray();

            foreach (var param in method.GetParameters())
            {
                if (param.ParameterType == typeof(CancellationToken))
                    continue;

                var name = param.Name!;
                var schema = param.ParameterType.GetSchemaForType();

                var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
                schema[JsonSchemaConstants.DescriptionKey] ??= desc;

                properties[name] = schema;

                if (!param.IsOptional)
                    required.Add(name);
            }

            var schemaObject = new JsonSchemaBuilder()
                .Type<object>()
                .Properties(properties)
                .Required(required)
                .Build();

            return new Tool
            {
                Name = toolName,
                Description = description,
                ParametersSchema = schemaObject,
                Function = func
            };
        }

        private static bool IsMethodJsonCompatible(MethodInfo m)
        {
            if (m.ContainsGenericParameters)
                return false;

            if (m.ReturnType.ContainsGenericParameters)
                return false;

            foreach (var p in m.GetParameters())
            {
                var t = p.ParameterType;

                if (t.IsByRef) return false;
                if (t.IsPointer) return false;
                if (t.ContainsGenericParameters) return false;
            }

            return true;
        }
    }
}
