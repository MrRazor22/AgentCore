using AgentCore.Chat;
using AgentCore.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentCore.Tools
{
    public sealed class InlineToolCall
    {
        public ToolCall? Call { get; }
        public string AssistantMessage { get; }

        public InlineToolCall(ToolCall? call, string assistantMessage)
        {
            Call = call;
            AssistantMessage = assistantMessage;
        }
    }

    public interface IToolCallParser
    {
        InlineToolCall ExtractInlineToolCall(string content, bool strict = false);
        object[] ParseToolParams(string toolName, JObject arguments);
    }

    public sealed class ToolCallParser : IToolCallParser
    {
        private const string NameTag = "name";
        private const string ArgumentsTag = "arguments";
        private const string AssistantMessageTag = "message";
        private const string IdTag = "id";

        private IToolCatalog _toolCatalog;
        public ToolCallParser(IToolCatalog toolCatalog)
        {
            _toolCatalog = toolCatalog;
        }

        public InlineToolCall ExtractInlineToolCall(string content, bool strict = false)
        {
            foreach (var (start, end, obj) in content.FindAllJsonObjects())
            {
                var name = obj[NameTag]?.ToString();
                var args = obj[ArgumentsTag] as JObject;

                if (name == null || args == null) continue;
                if (!_toolCatalog.Contains(name)) continue;

                var id = obj[IdTag]?.ToString() ?? Guid.NewGuid().ToString();
                var assistantMsg = obj[AssistantMessageTag]?.ToString();

                // RAW tool call: no param parsing here
                var rawCall = new ToolCall(
                    id,
                    name,
                    args,
                    parameters: null,
                    message: assistantMsg
                );

                var prefix = content.Substring(0, start).Trim();
                return new InlineToolCall(rawCall, prefix);
            }
            // no tool call found – treat entire text as assistant message
            return new InlineToolCall(null, content.Trim());
        }

        public object[] ParseToolParams(string toolName, JObject arguments)
        {
            var tool = _toolCatalog.Get(toolName);
            if (tool == null || tool.Function == null)
                throw new InvalidOperationException($"Tool '{toolName}' not registered or has no function.");

            var method = tool.Function.Method;
            var methodParams = method.GetParameters();
            var argsObj = arguments ?? throw new ArgumentException("Arguments null");

            // Wrap single complex param if needed
            if (methodParams.Length == 1 &&
                !methodParams[0].ParameterType.IsSimpleType() &&
                !argsObj.ContainsKey(methodParams[0].Name))
            {
                argsObj = new JObject { [methodParams[0].Name] = argsObj };
            }

            var paramValues = new object[methodParams.Length];
            for (int i = 0; i < methodParams.Length; i++)
            {
                var p = methodParams[i];
                var node = argsObj[p.Name];

                if (node == null)
                {
                    if (p.HasDefaultValue)
                        paramValues[i] = p.DefaultValue;
                    else if (!p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null)
                        paramValues[i] = null;
                    else
                        throw new ToolValidationException(p.Name, "Missing required parameter.");
                    continue;
                }

                // Validate against schema
                var schema = p.ParameterType.GetSchemaForType();
                var errors = schema.Validate(node, p.Name);
                if (errors.Any())
                    throw new ToolValidationAggregateException(errors);

                try
                {
                    paramValues[i] = node.ToObject(p.ParameterType);
                }
                catch (Exception ex)
                {
                    throw new ToolValidationException(p.Name, $"Invalid type for parameter. {ex.Message}");
                }
            }

            return paramValues;
        }
    }
}

//multiple parameters are wrong at the same time
public sealed class ToolValidationAggregateException : Exception
{
    public IReadOnlyList<SchemaValidationError> Errors { get; }

    public ToolValidationAggregateException(IEnumerable<SchemaValidationError> errors)
        : base("Tool validation failed") => Errors = errors.ToList();

    public override string ToString()
        => $"Validation failed for the following {Errors.Count} parameters:\n" +
        $" {string.Join(", ", Errors.Select(e => e.ToString()))}";
}
//on param wrong
public sealed class ToolValidationException : Exception
{
    public string ParamName { get; }

    public ToolValidationException(string param, string msg)
        : base($"Validation failed for parameter '{param}'. Details: '{msg}'") => ParamName = param;

    public override string ToString()
        => $"[{ParamName}] => {Message}";
}
