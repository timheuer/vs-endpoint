using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VSEndpoint.Services.Variables
{
    /// <summary>
    /// Resolves variables in HTTP requests.
    /// Precedence: request-local → file-scoped → environment file → built-in functions.
    /// </summary>
    public class VariableResolver
    {
        private static readonly Regex VariablePlaceholderRegex = new Regex(
            @"\{\{(?<name>[^}]+)\}\}",
            RegexOptions.Compiled);

        private static readonly Random RandomGenerator = new Random();

        private readonly Dictionary<string, string> _environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _currentEnvironment = "dev";

        /// <summary>
        /// Loads environment variables from http-client.env.json.
        /// </summary>
        public void LoadEnvironmentFile(string filePath)
        {
            _environmentVariables.Clear();

            if (!File.Exists(filePath))
                return;

            try
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);

                // Look for current environment section
                if (doc.RootElement.TryGetProperty(_currentEnvironment, out var envSection))
                {
                    LoadEnvironmentSection(envSection);
                }

                // Also load $shared section if exists
                if (doc.RootElement.TryGetProperty("$shared", out var sharedSection))
                {
                    LoadEnvironmentSection(sharedSection, overwrite: false);
                }
            }
            catch (JsonException)
            {
                // Invalid JSON - ignore
            }
        }

        private void LoadEnvironmentSection(JsonElement section, bool overwrite = true)
        {
            foreach (var property in section.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var key = property.Name;
                    var value = property.Value.GetString();
                    if (overwrite || !_environmentVariables.ContainsKey(key))
                    {
                        _environmentVariables[key] = value;
                    }
                }
            }
        }

        /// <summary>
        /// Sets the current environment (e.g., "dev", "prod").
        /// </summary>
        public void SetEnvironment(string environment)
        {
            _currentEnvironment = environment ?? "dev";
        }

        /// <summary>
        /// Adds or updates environment variables programmatically.
        /// </summary>
        public void SetVariable(string name, string value)
        {
            _environmentVariables[name] = value;
        }

        /// <summary>
        /// Resolves all {{variable}} placeholders in the input string.
        /// </summary>
        public string Resolve(string input, Dictionary<string, string> localVariables = null, Dictionary<string, string> fileVariables = null)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return VariablePlaceholderRegex.Replace(input, match =>
            {
                var variableName = match.Groups["name"].Value.Trim();
                return ResolveVariable(variableName, localVariables, fileVariables);
            });
        }

        private string ResolveVariable(string name, Dictionary<string, string> localVariables, Dictionary<string, string> fileVariables)
        {
            // Check for built-in functions first
            if (name.StartsWith("$"))
            {
                return ResolveBuiltInFunction(name);
            }

            // Precedence: local → file → environment
            if (localVariables != null && localVariables.TryGetValue(name, out var localValue))
            {
                return localValue;
            }

            if (fileVariables != null && fileVariables.TryGetValue(name, out var fileValue))
            {
                return fileValue;
            }

            if (_environmentVariables.TryGetValue(name, out var envValue))
            {
                return envValue;
            }

            // Return placeholder as-is if not found
            return $"{{{{{name}}}}}";
        }

        private string ResolveBuiltInFunction(string name)
        {
            // $datetime
            if (name.StartsWith("$datetime"))
            {
                var format = ExtractParameter(name, "$datetime");
                return string.IsNullOrEmpty(format)
                    ? DateTime.UtcNow.ToString("o")
                    : DateTime.UtcNow.ToString(format);
            }

            // $guid
            if (name == "$guid")
            {
                return Guid.NewGuid().ToString();
            }

            // $randomInt
            if (name.StartsWith("$randomInt"))
            {
                var param = ExtractParameter(name, "$randomInt");
                var parts = param.Split(',');
                int min = 0, max = int.MaxValue;
                if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out var p1))
                {
                    min = p1;
                }
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var p2))
                {
                    max = p2;
                }
                return RandomGenerator.Next(min, max).ToString();
            }

            // $timestamp
            if (name == "$timestamp")
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            }

            // $processEnv
            if (name.StartsWith("$processEnv"))
            {
                var envVar = ExtractParameter(name, "$processEnv");
                return Environment.GetEnvironmentVariable(envVar) ?? string.Empty;
            }

            // $dotenv
            if (name.StartsWith("$dotenv"))
            {
                var varName = ExtractParameter(name, "$dotenv");
                // Would need to load .env file - return empty for now
                return string.Empty;
            }

            return $"{{{{{name}}}}}";
        }

        private string ExtractParameter(string input, string functionName)
        {
            // Handles formats like: $datetime iso8601, $randomInt 1 100, $processEnv PATH
            if (input.Length <= functionName.Length)
                return string.Empty;

            var param = input.Substring(functionName.Length).Trim();
            return param;
        }
    }
}
