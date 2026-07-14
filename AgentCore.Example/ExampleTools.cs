using System.ComponentModel;
using AgentCore.Tools;

namespace AgentCore.Example;

public class ExampleTools
{
    private static readonly HttpClient _httpClient = new();

    [Tool]
    [Description("Evaluates mathematical expressions using a web api. E.g., '2 + 2 * 3'.")]
    public async Task<string> EvaluateMath(
        [Description("The mathematical expression to evaluate.")] string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return "Error: Empty expression.";

        try
        {
            var encoded = Uri.EscapeDataString(expression.Trim());
            var url = $"https://api.mathjs.org/v4/?expr={encoded}";
            using var response = await _httpClient.GetAsync(url);
            var result = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode ? result.Trim() : $"Error: {result}";
        }
        catch (Exception ex)
        {
            return $"Error evaluating math: {ex.Message}";
        }
    }

    [Tool]
    [Description("Get weather forecast info for a given city location.")]
    public string GetWeather(
        [Description("The location/city name, e.g. London, Tokyo.")] string location)
    {
        var temp = new Random().Next(15, 32);
        var conditions = new[] { "Sunny", "Cloudy", "Rainy", "Windy" }[new Random().Next(0, 4)];
        return $"The weather in {location} is currently {temp}°C, conditions: {conditions}.";
    }

    [Tool]
    [Description("Reads content from a local text file.")]
    public string ReadTextFile(
        [Description("Absolute or relative path to the text file.")] string path)
    {
        try
        {
            if (!File.Exists(path)) return $"Error: File '{path}' does not exist.";
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    [Tool]
    [Description("Writes text content to a local text file.")]
    public string WriteTextFile(
        [Description("Path to write to.")] string path, 
        [Description("The content to write.")] string content)
    {
        try
        {
            File.WriteAllText(path, content);
            return $"Successfully wrote to file '{path}'.";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }
}
