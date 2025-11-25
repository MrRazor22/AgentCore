using AgentCore.Chat;
using AgentCore.JsonSchema;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
        List<ToolValidationError> ValidateAgainstSchema(JToken? node, JObject schema, string path = "");
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
        private IEnumerable<(int Start, int End, JObject Obj)> FindAllJsonObjects(string content)
        {
            var results = new List<(int, int, JObject)>();
            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }
                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }
                    if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        var jsonStr = content.Substring(start, i - start + 1);
                        try
                        {
                            var obj = JObject.Parse(jsonStr);
                            results.Add((start, i, obj));
                        }
                        catch { }
                        start = -1;
                    }
                }
            }

            return results;
        }

        public InlineToolCall ExtractInlineToolCall(string content, bool strict = false)
        {
            foreach (var (start, end, obj) in FindAllJsonObjects(content))
            {
                var name = obj[NameTag]?.ToString();
                var args = obj[ArgumentsTag] as JObject;

                if (name == null || args == null) continue;
                if (!_toolCatalog.Contains(name)) continue;

                var id = obj[IdTag]?.ToString() ?? Guid.NewGuid().ToString();
                var assistantMsg = obj[AssistantMessageTag]?.ToString();

                var parsed = new ToolCall(
                    id,
                    name,
                    args,
                    ParseToolParams(name, args),
                    assistantMsg
                );

                var prefix = content.Substring(0, start).Trim();
                return new InlineToolCall(parsed, prefix);
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
                var errors = ValidateAgainstSchema(node, schema, p.Name);
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

        public List<ToolValidationError> ValidateAgainstSchema(JToken? node, JObject schema, string path = "")
        {
            var errors = new List<ToolValidationError>();

            if (node == null)
            {
                if (schema["required"] is JArray arr && arr.Count > 0)
                    errors.Add(new ToolValidationError(path, path, "Value required but missing.", "missing"));
                return errors;
            }

            var type = schema["type"]?.ToString();
            switch (type)
            {
                case "string":
                    if (node.Type != JTokenType.String)
                        errors.Add(new ToolValidationError(path, path, "Expected string", "type_error"));
                    break;
                case "integer":
                    if (node.Type != JTokenType.Integer)
                        errors.Add(new ToolValidationError(path, path, "Expected integer", "type_error"));
                    break;
                case "number":
                    if (node.Type != JTokenType.Float && node.Type != JTokenType.Integer)
                        errors.Add(new ToolValidationError(path, path, "Expected number", "type_error"));
                    break;
                case "boolean":
                    if (node.Type != JTokenType.Boolean)
                        errors.Add(new ToolValidationError(path, path, "Expected boolean", "type_error"));
                    break;
                case "array":
                    if (node.Type != JTokenType.Array)
                    {
                        errors.Add(new ToolValidationError(path, path, "Expected array", "type_error"));
                    }
                    else if (schema["items"] is JObject itemSchema)
                    {
                        var arrNode = (JArray)node;
                        for (int idx = 0; idx < arrNode.Count; idx++)
                            errors.AddRange(ValidateAgainstSchema(arrNode[idx], itemSchema, $"{path}[{idx}]"));
                    }
                    break;
                case "object":
                    if (node.Type != JTokenType.Object)
                    {
                        errors.Add(new ToolValidationError(path, path, "Expected object", "type_error"));
                    }
                    else if (schema["properties"] is JObject propSchemas)
                    {
                        var objNode = (JObject)node;
                        foreach (var kvp in propSchemas)
                        {
                            var key = kvp.Key;
                            var propSchema = kvp.Value as JObject;
                            if (propSchema == null) continue;

                            if (!objNode.ContainsKey(key))
                            {
                                if (schema["required"] is JArray reqArr && reqArr.Any(r => r?.ToString() == key))
                                    errors.Add(new ToolValidationError(key, $"{path}.{key}".Trim('.'), $"Missing required field '{key}'", "missing"));
                            }
                            else
                            {
                                errors.AddRange(ValidateAgainstSchema(objNode[key], propSchema, $"{path}.{key}".Trim('.')));
                            }
                        }
                    }
                    break;
            }

            return errors;
        }

    }
}

public sealed class ToolValidationError
{
    public string Param { get; }
    public string? Path { get; }
    public string Message { get; }
    public string ErrorType { get; }

    public ToolValidationError(string param, string? path, string message, string errorType)
    {
        Param = param;
        Path = path;
        Message = message;
        ErrorType = errorType;
    }
}
//multiple parameters are wrong at the same time
public sealed class ToolValidationAggregateException : Exception
{
    public IReadOnlyList<ToolValidationError> Errors { get; }

    public ToolValidationAggregateException(IEnumerable<ToolValidationError> errors)
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
