using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VSEndpoint.Services.Parser
{
    /// <summary>
    /// Represents a single HTTP request parsed from .http/.rest file.
    /// </summary>
    public class HttpRequestDefinition
    {
        public string Name { get; set; }
        public string Method { get; set; } = "GET";
        public string Url { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public Dictionary<string, string> LocalVariables { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Represents parsed content from a .http/.rest file.
    /// </summary>
    public class HttpFileParseResult
    {
        public List<HttpRequestDefinition> Requests { get; set; } = new List<HttpRequestDefinition>();
        public Dictionary<string, string> FileVariables { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Parses .http/.rest file format.
    /// Supports: request delimiters (###), @name directives, variable declarations, headers, and body.
    /// </summary>
    public class HttpFileParser
    {
        private static readonly Regex RequestLineRegex = new Regex(
            @"^(?<method>GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS|TRACE|CONNECT)\s+(?<url>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex VariableDeclarationRegex = new Regex(
            @"^@(?<name>[a-zA-Z_][a-zA-Z0-9_]*)\s*=\s*(?<value>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex NameDirectiveRegex = new Regex(
            @"^#\s*@name\s+(?<name>\S+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HeaderRegex = new Regex(
            @"^(?<name>[^:]+):\s*(?<value>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex RequestDelimiterRegex = new Regex(
            @"^###.*$",
            RegexOptions.Compiled);

        /// <summary>
        /// Parses the content of a .http/.rest file.
        /// </summary>
        public HttpFileParseResult Parse(string content)
        {
            var result = new HttpFileParseResult();
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            ParseFileContent(lines, result);

            return result;
        }

        /// <summary>
        /// Finds the request at the specified line number (1-indexed).
        /// </summary>
        public HttpRequestDefinition FindRequestAtLine(string content, int lineNumber)
        {
            var parseResult = Parse(content);
            return parseResult.Requests.FirstOrDefault(r => lineNumber >= r.StartLine && lineNumber <= r.EndLine);
        }

        private void ParseFileContent(string[] lines, HttpFileParseResult result)
        {
            var currentRequest = new HttpRequestDefinition();
            var requestStarted = false;
            var inBody = false;
            var bodyLines = new List<string>();
            var pendingName = (string)null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineNumber = i + 1;

                // Skip empty lines at start
                if (!requestStarted && string.IsNullOrWhiteSpace(line))
                    continue;

                // Request delimiter
                if (RequestDelimiterRegex.IsMatch(line))
                {
                    if (requestStarted)
                    {
                        FinishRequest(currentRequest, bodyLines, lineNumber - 1, result);
                    }
                    currentRequest = new HttpRequestDefinition();
                    requestStarted = false;
                    inBody = false;
                    bodyLines.Clear();
                    pendingName = null;
                    continue;
                }

                // Name directive (# @name)
                var nameMatch = NameDirectiveRegex.Match(line);
                if (nameMatch.Success)
                {
                    pendingName = nameMatch.Groups["name"].Value;
                    continue;
                }

                // Variable declaration (@name=value)
                var varMatch = VariableDeclarationRegex.Match(line);
                if (varMatch.Success)
                {
                    var name = varMatch.Groups["name"].Value;
                    var value = varMatch.Groups["value"].Value.Trim();

                    if (!requestStarted)
                    {
                        result.FileVariables[name] = value;
                    }
                    else
                    {
                        currentRequest.LocalVariables[name] = value;
                    }
                    continue;
                }

                // Skip comment lines (not # @name)
                if (line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith("//"))
                    continue;

                // Request line (METHOD URL)
                var requestMatch = RequestLineRegex.Match(line);
                if (requestMatch.Success && !requestStarted)
                {
                    if (pendingName != null)
                    {
                        currentRequest.Name = pendingName;
                    }
                    currentRequest.Method = requestMatch.Groups["method"].Value.ToUpperInvariant();
                    currentRequest.Url = requestMatch.Groups["url"].Value.Trim();
                    currentRequest.StartLine = lineNumber;
                    requestStarted = true;
                    continue;
                }

                if (!requestStarted)
                    continue;

                // Empty line transitions to body
                if (string.IsNullOrWhiteSpace(line) && !inBody)
                {
                    inBody = true;
                    continue;
                }

                // Header line
                if (!inBody)
                {
                    var headerMatch = HeaderRegex.Match(line);
                    if (headerMatch.Success)
                    {
                        var headerName = headerMatch.Groups["name"].Value.Trim();
                        var headerValue = headerMatch.Groups["value"].Value.Trim();
                        currentRequest.Headers[headerName] = headerValue;
                    }
                    continue;
                }

                // Body line
                bodyLines.Add(line);
            }

            // Finish last request if any
            if (requestStarted)
            {
                FinishRequest(currentRequest, bodyLines, lines.Length, result);
            }
        }

        private void FinishRequest(HttpRequestDefinition request, List<string> bodyLines, int endLine, HttpFileParseResult result)
        {
            request.EndLine = endLine;

            if (bodyLines.Count > 0)
            {
                // Trim trailing empty lines from body
                while (bodyLines.Count > 0 && string.IsNullOrWhiteSpace(bodyLines[bodyLines.Count - 1]))
                {
                    bodyLines.RemoveAt(bodyLines.Count - 1);
                }
                request.Body = string.Join(Environment.NewLine, bodyLines);
            }

            if (!string.IsNullOrEmpty(request.Url))
            {
                result.Requests.Add(request);
            }
        }
    }
}
