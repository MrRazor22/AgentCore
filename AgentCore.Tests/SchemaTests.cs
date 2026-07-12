using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Xunit;
using AgentCore.Schema;

namespace AgentCore.Tests;

public class SchemaTests
{
    [Theory]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(long), "integer")]
    [InlineData(typeof(short), "integer")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(char), "string")]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(decimal), "number")]
    [InlineData(typeof(void), "null")]
    [InlineData(typeof(int[]), "array")]
    [InlineData(typeof(List<string>), "array")]
    [InlineData(typeof(DayOfWeek), "string")]
    [InlineData(typeof(object), "object")]
    public void MapClrTypeToJsonType_ReturnsExpectedJsonType(Type clrType, string expectedJsonType)
    {
        var result = clrType.MapClrTypeToJsonType();
        Assert.Equal(expectedJsonType, result);
    }

    private enum TestEnum
    {
        [Description("First option")]
        Alpha,
        Beta
    }

    private class NestedObject
    {
        public string Value { get; set; } = "";
    }

    private class SampleModel
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string? Nickname { get; set; }
        public TestEnum Status { get; set; }
        public NestedObject Child { get; set; } = new();
    }

    private class CircularModel
    {
        public CircularModel? Parent { get; set; }
    }

    [Fact]
    public void GetSchemaForType_GeneratesExpectedSchema()
    {
        // 1. Complex Object Validation
        var schema = typeof(SampleModel).GetSchemaForType();
        var node = schema.ToJsonNode().AsObject();

        Assert.Equal("object", node["type"]?.ToString());
        var properties = node["properties"]?.AsObject();
        Assert.NotNull(properties);
        Assert.Equal("string", properties["Name"]?["type"]?.ToString());
        Assert.Equal("integer", properties["Age"]?["type"]?.ToString());

        // 2. Nullable / optional parameter (Nullable reference types/Value types)
        var required = node["required"]?.AsArray().Select(r => r?.ToString()).ToList();
        Assert.NotNull(required);
        Assert.Contains("Name", required);
        Assert.Contains("Age", required);
        Assert.Contains("Child", required);
        Assert.DoesNotContain("Nickname", required); // Nullable Reference Type -> optional

        // 3. Enum validation
        var statusProp = properties["Status"]?.AsObject();
        Assert.NotNull(statusProp);
        Assert.Equal("string", statusProp["type"]?.ToString());
        var enumVals = statusProp["enum"]?.AsArray().Select(v => v?.ToString()).ToList();
        Assert.NotNull(enumVals);
        Assert.Contains("Alpha", enumVals);
        Assert.Contains("Beta", enumVals);

        // 4. Circular Reference Handling (should compile without infinite looping)
        var circularSchema = typeof(CircularModel).GetSchemaForType();
        var circularNode = circularSchema.ToJsonNode().AsObject();
        Assert.Equal("object", circularNode["type"]?.ToString());
    }

    [Fact]
    public void Validate_DetectsViolationsCorrectly()
    {
        var schema = typeof(SampleModel).GetSchemaForType();

        // Valid Object
        var validObj = new JsonObject
        {
            ["Name"] = "John",
            ["Age"] = 30,
            ["Status"] = "Alpha",
            ["Child"] = new JsonObject { ["Value"] = "Inner" }
        };
        var validErrors = schema.Validate(validObj);
        Assert.Empty(validErrors);

        // Missing required field "Name"
        var missingObj = new JsonObject
        {
            ["Age"] = 30,
            ["Status"] = "Alpha",
            ["Child"] = new JsonObject { ["Value"] = "Inner" }
        };
        var missingErrors = schema.Validate(missingObj);
        Assert.Single(missingErrors);
        Assert.Contains("Missing required parameter 'Name'", missingErrors[0]);

        // Wrong type for "Age"
        var wrongTypeObj = new JsonObject
        {
            ["Name"] = "John",
            ["Age"] = "thirty",
            ["Status"] = "Alpha",
            ["Child"] = new JsonObject { ["Value"] = "Inner" }
        };
        var typeErrors = schema.Validate(wrongTypeObj);
        Assert.Single(typeErrors);
        Assert.Contains("Expected integer at 'Age'", typeErrors[0]);

        // Unknown properties when AdditionalProperties = false
        var extraObj = new JsonObject
        {
            ["Name"] = "John",
            ["Age"] = 30,
            ["Status"] = "Alpha",
            ["Child"] = new JsonObject { ["Value"] = "Inner" },
            ["UnknownField"] = 123
        };
        var extraErrors = schema.Validate(extraObj);
        Assert.Single(extraErrors);
        Assert.Contains("Unknown parameter 'UnknownField'", extraErrors[0]);
    }
}
