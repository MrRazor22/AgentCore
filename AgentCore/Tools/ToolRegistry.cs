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
        Tool Get(string toolName); // return null if not found  
        bool Contains(string toolName);
    }

    internal interface IToolRegistry
    {
        void Register(params Delegate[] funcs);
        void RegisterAll<T>();
        void RegisterAll<T>(T instance);
    }

    internal class ToolRegistryCatalog : IToolRegistry, IToolCatalog
    {
        private readonly List<Tool> _registeredTools;

        public ToolRegistryCatalog(IEnumerable<Tool> tools = null)
        {
            _registeredTools = tools != null ? new List<Tool>(tools) : new List<Tool>();
        }

        public IReadOnlyList<Tool> RegisteredTools => _registeredTools;

        public static implicit operator ToolRegistryCatalog(List<Tool> tools)
        {
            return new ToolRegistryCatalog(tools);
        }
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
                    // skip if not compatible
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
                    // skip if not compatible
                }
            }
        }

        public bool Contains(string toolName) => _registeredTools.Any(t =>
                t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        public Tool? Get(string toolName)
            => _registeredTools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.InvariantCultureIgnoreCase));
        private Tool CreateToolFromDelegate(Delegate func)
        {
            var method = func.Method;
            var description =
                 method.GetCustomAttribute<ToolAttribute>()?.Description
                 ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description
                 ?? method.Name;
            var parameters = method.GetParameters();

            var properties = new JObject();
            var required = new JArray();

            foreach (var param in parameters)
            {
                if (param.ParameterType == typeof(CancellationToken))
                    continue; // skip it completely

                var name = param.Name!;
                var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
                var typeSchema = param.ParameterType.GetSchemaForType();
                typeSchema[JsonSchemaConstants.DescriptionKey] = typeSchema[JsonSchemaConstants.DescriptionKey] ?? desc;

                properties[name] = typeSchema;
                if (!param.IsOptional) required.Add(name);
            }

            var schema = new JsonSchemaBuilder()
                .Type<object>()
                .Properties(properties)
                .Required(required)
                .Build();

            return new Tool
            {
                Name = method.Name,
                Description = description,
                ParametersSchema = schema, // already JObject
                Function = func
            };
        }
        private static bool IsMethodJsonCompatible(MethodInfo m)
        {
            // reject open generic methods
            if (m.ContainsGenericParameters)
                return false;

            // reject open generic return type
            if (m.ReturnType.ContainsGenericParameters)
                return false;

            foreach (var p in m.GetParameters())
            {
                var t = p.ParameterType;

                if (t.IsByRef) return false;          // ref / out
                if (t.IsPointer) return false;        // unsafe pointer
                if (t.ContainsGenericParameters) return false;  // open generic param
            }

            return true;
        }

    }
}