using System;
using System.Collections.Generic;
using System.IO;
using HttpFileParser.Variables;

namespace VSEndpoint.Services.Variables
{
    /// <summary>
    /// Resolves variables in HTTP requests.
    /// Precedence: request-local → file-scoped → environment file → built-in functions.
    /// </summary>
    public class VariableResolver
    {
        private readonly Dictionary<string, string> _environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _manualVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private IRequestResponseProvider _requestResponseProvider;
        private string _currentEnvironment = "dev";

        /// <summary>
        /// Loads environment variables from http-client.env.json.
        /// </summary>
        public void LoadEnvironmentFile(string filePath)
        {
            _environmentVariables.Clear();

            if (!File.Exists(filePath))
                return;

            var json = File.ReadAllText(filePath);
            var envFile = global::HttpFileParser.HttpEnvironment.Parse(json, filePath);
            foreach (var kvp in envFile.GetMergedEnvironment(_currentEnvironment))
            {
                _environmentVariables[kvp.Key] = kvp.Value;
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
            _manualVariables[name] = value;
        }

        /// <summary>
        /// Sets request-response provider for chained request variable resolution.
        /// </summary>
        public void SetRequestResponseProvider(IRequestResponseProvider provider)
        {
            _requestResponseProvider = provider;
        }

        /// <summary>
        /// Resolves all {{variable}} placeholders in the input string.
        /// </summary>
        public string Resolve(string input, Dictionary<string, string> localVariables = null, Dictionary<string, string> fileVariables = null)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var context = new VariableContext();

            if (localVariables != null && localVariables.Count > 0)
            {
                context.AddResolver(new EnvironmentVariableResolver(localVariables));
            }

            if (fileVariables != null && fileVariables.Count > 0)
            {
                context.AddResolver(new EnvironmentVariableResolver(fileVariables));
            }

            if (_manualVariables.Count > 0)
            {
                context.AddResolver(new EnvironmentVariableResolver(_manualVariables));
            }

            if (_environmentVariables.Count > 0)
            {
                context.AddResolver(new EnvironmentVariableResolver(_environmentVariables));
            }

            if (_requestResponseProvider != null)
            {
                context.AddResolver(new RequestVariableResolver(_requestResponseProvider));
            }

            context.AddResolver(new DynamicVariableResolver());

            var expander = new VariableExpander(context);
            return expander.Expand(input);
        }
    }
}
