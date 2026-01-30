using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VSEndpoint.Services.Session
{
    /// <summary>
    /// Represents a response stored in the session for request chaining.
    /// </summary>
    public class StoredResponse
    {
        public int StatusCode { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body { get; set; }
        public JsonDocument ParsedBody { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Manages request chain sessions for resolving cross-request references.
    /// Supports {{requestName.response.body.path}} and {{requestName.response.headers.X-Header}} syntax.
    /// </summary>
    public class RequestChainSessionManager
    {
        private static readonly Regex ChainReferenceRegex = new Regex(
            @"\{\{(?<request>[a-zA-Z_][a-zA-Z0-9_]*)\.response\.(?<type>body|headers)(?:\.(?<path>[^}]+))?\}\}",
            RegexOptions.Compiled);

        private readonly Dictionary<string, StoredResponse> _sessionResponses = new Dictionary<string, StoredResponse>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        /// <summary>
        /// Stores a response for a named request.
        /// </summary>
        public void StoreResponse(string requestName, StoredResponse response)
        {
            if (string.IsNullOrEmpty(requestName))
                return;

            lock (_lock)
            {
                // Try to parse body as JSON
                if (!string.IsNullOrEmpty(response.Body))
                {
                    try
                    {
                        response.ParsedBody = JsonDocument.Parse(response.Body);
                    }
                    catch (JsonException)
                    {
                        // Not JSON - that's fine
                    }
                }

                response.Timestamp = DateTime.UtcNow;
                _sessionResponses[requestName] = response;
            }
        }

        /// <summary>
        /// Resolves request chain references in the input string.
        /// </summary>
        public string ResolveChainReferences(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return ChainReferenceRegex.Replace(input, match =>
            {
                var requestName = match.Groups["request"].Value;
                var type = match.Groups["type"].Value;
                var path = match.Groups["path"].Value;

                return ResolveReference(requestName, type, path) ?? match.Value;
            });
        }

        private string ResolveReference(string requestName, string type, string path)
        {
            lock (_lock)
            {
                if (!_sessionResponses.TryGetValue(requestName, out var response))
                {
                    return null;
                }

                if (type.Equals("headers", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(path) && response.Headers.TryGetValue(path, out var headerValue))
                    {
                        return headerValue;
                    }
                    return null;
                }

                if (type.Equals("body", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        return response.Body;
                    }

                    // Navigate JSON path
                    if (response.ParsedBody != null)
                    {
                        return NavigateJsonPath(response.ParsedBody.RootElement, path);
                    }
                }

                return null;
            }
        }

        private string NavigateJsonPath(JsonElement element, string path)
        {
            var segments = path.Split('.');
            var current = element;

            foreach (var segment in segments)
            {
                // Handle array index
                if (segment.Contains("["))
                {
                    var bracketIndex = segment.IndexOf('[');
                    var propertyName = segment.Substring(0, bracketIndex);
                    var indexStr = segment.Substring(bracketIndex + 1).TrimEnd(']');

                    if (!string.IsNullOrEmpty(propertyName))
                    {
                        if (!current.TryGetProperty(propertyName, out current))
                            return null;
                    }

                    if (int.TryParse(indexStr, out var index) && current.ValueKind == JsonValueKind.Array)
                    {
                        var length = current.GetArrayLength();
                        if (index < 0 || index >= length)
                            return null;
                        current = current[index];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    if (!current.TryGetProperty(segment, out current))
                        return null;
                }
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => current.GetRawText()
            };
        }

        /// <summary>
        /// Clears all stored responses in the session.
        /// </summary>
        public void ClearSession()
        {
            lock (_lock)
            {
                foreach (var response in _sessionResponses.Values)
                {
                    response.ParsedBody?.Dispose();
                }
                _sessionResponses.Clear();
            }
        }

        /// <summary>
        /// Gets the names of all stored requests.
        /// </summary>
        public IEnumerable<string> GetStoredRequestNames()
        {
            lock (_lock)
            {
                return new List<string>(_sessionResponses.Keys);
            }
        }
    }
}
