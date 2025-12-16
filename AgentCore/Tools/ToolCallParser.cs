using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AgentCore.Tools
{
    public sealed class InlineToolMatch
    {
        public int Start { get; }
        public int End { get; }
        public ToolCall Call { get; }

        public InlineToolMatch(int start, int end, ToolCall call)
        {
            Start = start;
            End = end;
            Call = call;
        }
    }

    public interface IToolCallParser
    {
        InlineToolMatch TryMatch(string content);
        object[] ParseToolParams(string toolName, JObject arguments);
    }

    public sealed class ToolCallParser : IToolCallParser
    {
        private IToolCatalog _toolCatalog;
        public ToolCallParser(IToolCatalog toolCatalog)
        {
            _toolCatalog = toolCatalog;
        }

        public InlineToolMatch? TryMatch(string content)
        {
            foreach (var (start, end, obj) in content.FindAllJsonObjects())
            {
                var name = obj["name"]?.ToString();
                var args = obj["arguments"] as JObject;
                if (name == null || args == null) continue;
                if (!_toolCatalog.Contains(name)) continue;

                var id = obj["id"]?.ToString() ?? Guid.NewGuid().ToString();

                var call = new ToolCall(
                    id,
                    name,
                    args
                );

                return new InlineToolMatch(start, end, call);
            }
            return null;
        }

        public object[] ParseToolParams(string toolName, JObject arguments)
        {
            var tool = _toolCatalog.Get(toolName);
            if (tool == null || tool.Function == null)
                throw new InvalidOperationException($"Tool '{toolName}' not registered or has no function.");

            var method = tool.Function.Method;
            var methodParams = method.GetParameters();
            var argsObj = arguments ?? throw new ArgumentException("Arguments null");

            if (methodParams.Length == 1 &&
                !methodParams[0].ParameterType.IsSimpleType() &&
                !argsObj.ContainsKey(methodParams[0].Name))
            {
                argsObj = new JObject { [methodParams[0].Name] = argsObj };
            }

            var paramValues = new List<object?>();
            foreach (var p in methodParams)
            {
                if (p.ParameterType == typeof(CancellationToken))
                    continue;

                var node = argsObj[p.Name];

                if (node == null)
                {
                    if (p.HasDefaultValue)
                        paramValues.Add(p.DefaultValue);
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
                    paramValues.Add(node.ToObject(p.ParameterType));
                }
                catch (Exception ex)
                {
                    throw new ToolValidationException(p.Name, ex.Message);
                }
            }

            return paramValues.ToArray();
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

