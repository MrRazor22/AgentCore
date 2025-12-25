using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace AgentCore.Tools
{
    public interface IToolCallParser
    {
        ToolCall? TryMatch(string content);
        ToolCall ValidateToolCall(ToolCall toolCall);
    }

    public sealed class ToolCallParser : IToolCallParser
    {
        private IToolCatalog _toolCatalog;
        public ToolCallParser(IToolCatalog toolCatalog)
        {
            _toolCatalog = toolCatalog;
        }

        public ToolCall? TryMatch(string content)
        {
            foreach (var (start, _, obj) in content.FindAllJsonObjects())
            {
                var name = obj["name"]?.ToString();
                var args = obj["arguments"] as JObject;
                if (name == null || args == null) continue;
                if (!_toolCatalog.Contains(name)) continue;

                var id = obj["id"]?.ToString() ?? Guid.NewGuid().ToString();

                var prefix = start > 0
                    ? content.Substring(0, start)
                    : null;

                var call = new ToolCall(
                    id,
                    name,
                    args,
                    message: string.IsNullOrWhiteSpace(prefix) ? null : prefix
                );

                return call;
            }
            return null;
        }
        public ToolCall ValidateToolCall(ToolCall toolCall)
        {
            var tool = _toolCatalog.Get(toolCall.Name)
                ?? throw new ToolValidationException(toolCall.Name, "Tool not registered.");

            if (toolCall.Arguments == null)
                throw new ToolValidationException(toolCall.Name, "Arguments missing.");

            var parsed = ParseToolParams(
                tool.Function.Method,
                toolCall.Arguments
            );

            return new ToolCall(
                toolCall.Id,
                toolCall.Name,
                toolCall.Arguments,
                parsed
            );
        }
        private object[] ParseToolParams(MethodInfo method, JObject arguments)
        {
            var parameters = method.GetParameters();
            var argsObj = arguments;

            if (parameters.Length == 1 &&
                !parameters[0].ParameterType.IsSimpleType() &&
                !argsObj.ContainsKey(parameters[0].Name))
            {
                argsObj = new JObject { [parameters[0].Name] = argsObj };
            }

            var values = new List<object?>();

            foreach (var p in parameters)
            {
                if (p.ParameterType == typeof(CancellationToken))
                    continue;

                var node = argsObj[p.Name];

                if (node == null)
                {
                    if (p.HasDefaultValue)
                        values.Add(p.DefaultValue);
                    else
                        throw new ToolValidationException(p.Name, "Missing required parameter.");
                    continue;
                }

                var schema = p.ParameterType.GetSchemaForType();
                var errors = schema.Validate(node, p.Name);
                if (errors.Any())
                    throw new ToolValidationAggregateException(errors);

                try
                {
                    values.Add(node.ToObject(p.ParameterType));
                }
                catch (Exception ex)
                {
                    throw new ToolValidationException(p.Name, ex.Message);
                }
            }

            return values.ToArray();
        }
    }
}

//multiple parameters are wrong at the same time
public sealed class ToolValidationAggregateException : Exception
{
    public IReadOnlyList<SchemaValidationError> Errors { get; }

    public ToolValidationAggregateException(IEnumerable<SchemaValidationError> errors)
        : base("Tool validation failed")
    {
        Errors = errors.ToList();
    }

    public override string ToString()
        => Errors.Select(e => e.ToString()).ToJoinedString("; ");
}

//on param wrong
public sealed class ToolValidationException : Exception
{
    public string ParamName { get; }

    public ToolValidationException(string param, string msg)
        : base(msg)
    {
        ParamName = param;
    }

    public override string ToString()
        => $"{ParamName}: {Message}";
}

