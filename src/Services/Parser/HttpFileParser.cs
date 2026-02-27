using System;
using System.Collections.Generic;
using System.Linq;
using HttpFileParser.Model;

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
    /// Parses .http/.rest file format using the HttpFileParser library.
    /// </summary>
    public class HttpFileParser
    {
        /// <summary>
        /// Parses the content of a .http/.rest file.
        /// </summary>
        public HttpFileParseResult Parse(string content)
        {
            var document = global::HttpFileParser.HttpFile.Parse(content ?? string.Empty);
            var result = new HttpFileParseResult();

            foreach (var variable in document.Variables)
            {
                result.FileVariables[variable.Name] = variable.RawValue;
            }

            foreach (var request in document.Requests)
            {
                var mappedRequest = new HttpRequestDefinition
                {
                    Name = request.Name,
                    Method = request.Method,
                    Url = request.RawUrl,
                    StartLine = request.Span.StartLine,
                    EndLine = request.Span.EndLine,
                    Body = GetBodyContent(request.Body)
                };

                foreach (var header in request.Headers)
                {
                    mappedRequest.Headers[header.Name] = header.RawValue;
                }

                result.Requests.Add(mappedRequest);
            }

            foreach (var diagnostic in document.Diagnostics)
            {
                result.Errors.Add(diagnostic.ToString());
            }

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

        private static string GetBodyContent(HttpRequestBody body)
        {
            if (body is TextBody textBody)
            {
                return textBody.Content;
            }

            if (body is FileReferenceBody fileReferenceBody)
            {
                return fileReferenceBody.ProcessVariables ? "<@ " + fileReferenceBody.FilePath : "< " + fileReferenceBody.FilePath;
            }

            return null;
        }
    }
}
