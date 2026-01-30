using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VSEndpoint.Services.Parser;
using VSEndpoint.Services.Session;
using VSEndpoint.Services.Variables;

namespace VSEndpoint.Services.Execution
{
    /// <summary>
    /// Configuration for the HTTP execution service.
    /// </summary>
    public class HttpExecutionConfig
    {
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public bool FollowRedirects { get; set; } = true;
        public int MaxRedirects { get; set; } = 10;
        public bool AutoDecompress { get; set; } = true;
    }

    /// <summary>
    /// Executes HTTP requests with support for variable resolution, authentication, and timing metrics.
    /// </summary>
    public class HttpExecutionService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClientHandler _handler;
        private readonly VariableResolver _variableResolver;
        private readonly RequestChainSessionManager _sessionManager;
        private readonly HttpExecutionConfig _config;

        public HttpExecutionService(
            VariableResolver variableResolver,
            RequestChainSessionManager sessionManager,
            HttpExecutionConfig config = null)
        {
            _variableResolver = variableResolver ?? throw new ArgumentNullException(nameof(variableResolver));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _config = config ?? new HttpExecutionConfig();

            _handler = new HttpClientHandler
            {
                AllowAutoRedirect = _config.FollowRedirects,
                MaxAutomaticRedirections = _config.MaxRedirects,
                AutomaticDecompression = _config.AutoDecompress
                    ? DecompressionMethods.GZip | DecompressionMethods.Deflate
                    : DecompressionMethods.None
            };

            _httpClient = new HttpClient(_handler)
            {
                Timeout = _config.Timeout
            };
        }

        /// <summary>
        /// Executes an HTTP request definition.
        /// </summary>
        public async Task<HttpExecutionResult> ExecuteAsync(
            HttpRequestDefinition request,
            Dictionary<string, string> fileVariables,
            CancellationToken cancellationToken = default)
        {
            var result = new HttpExecutionResult
            {
                ExecutedAt = DateTime.UtcNow
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Resolve variables in URL, headers, and body
                var resolvedUrl = ResolveVariables(request.Url, request.LocalVariables, fileVariables);
                result.RequestUrl = resolvedUrl;
                result.RequestMethod = request.Method;

                // Create request message
                var httpRequest = new HttpRequestMessage(
                    new HttpMethod(request.Method),
                    resolvedUrl);

                // Add headers
                foreach (var header in request.Headers)
                {
                    var resolvedValue = ResolveVariables(header.Value, request.LocalVariables, fileVariables);
                    result.RequestHeaders[header.Key] = resolvedValue;

                    // Handle content headers separately
                    if (IsContentHeader(header.Key))
                    {
                        // Will be set on content
                        continue;
                    }

                    httpRequest.Headers.TryAddWithoutValidation(header.Key, resolvedValue);
                }

                // Add body
                if (!string.IsNullOrEmpty(request.Body))
                {
                    var resolvedBody = ResolveVariables(request.Body, request.LocalVariables, fileVariables);
                    result.RequestBody = resolvedBody;

                    var content = new StringContent(resolvedBody);

                    // Set content type if specified
                    if (request.Headers.TryGetValue("Content-Type", out var contentType))
                    {
                        var resolvedContentType = ResolveVariables(contentType, request.LocalVariables, fileVariables);
                        content.Headers.ContentType = MediaTypeHeaderValue.Parse(resolvedContentType);
                    }

                    httpRequest.Content = content;
                }

                // Execute request
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

                stopwatch.Stop();
                result.Timing.TotalTime = stopwatch.Elapsed;

                // Capture response
                result.StatusCode = (int)response.StatusCode;
                result.StatusDescription = response.ReasonPhrase;
                result.ContentType = response.Content.Headers.ContentType?.ToString();

                // Capture response headers
                foreach (var header in response.Headers)
                {
                    result.ResponseHeaders[header.Key] = string.Join(", ", header.Value);

                    // Parse Set-Cookie headers
                    if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var cookieValue in header.Value)
                        {
                            var cookie = ParseCookie(cookieValue);
                            if (cookie != null)
                            {
                                result.Cookies.Add(cookie);
                            }
                        }
                    }
                }
                foreach (var header in response.Content.Headers)
                {
                    result.ResponseHeaders[header.Key] = string.Join(", ", header.Value);
                }

                // Read response body
                result.ResponseBodyBytes = await response.Content.ReadAsByteArrayAsync();
                result.ResponseSizeBytes = result.ResponseBodyBytes.Length;

                // Try to decode as string
                var encoding = GetEncodingFromContentType(result.ContentType) ?? Encoding.UTF8;
                result.ResponseBody = encoding.GetString(result.ResponseBodyBytes);

                result.Success = true;

                // Store in session for chaining
                if (!string.IsNullOrEmpty(request.Name))
                {
                    _sessionManager.StoreResponse(request.Name, new StoredResponse
                    {
                        StatusCode = result.StatusCode,
                        Headers = new Dictionary<string, string>(result.ResponseHeaders, StringComparer.OrdinalIgnoreCase),
                        Body = result.ResponseBody
                    });
                }
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                result.Timing.TotalTime = stopwatch.Elapsed;
                result.Success = false;
                result.ErrorMessage = "Request was cancelled.";
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                result.Timing.TotalTime = stopwatch.Elapsed;
                result.Success = false;
                result.ErrorMessage = $"Request timed out after {_config.Timeout.TotalSeconds}s.";
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                result.Timing.TotalTime = stopwatch.Elapsed;
                result.Success = false;
                result.ErrorMessage = $"HTTP error: {ex.Message}";
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Timing.TotalTime = stopwatch.Elapsed;
                result.Success = false;
                result.ErrorMessage = $"Error: {ex.Message}";
            }

            return result;
        }

        private string ResolveVariables(string input, Dictionary<string, string> localVariables, Dictionary<string, string> fileVariables)
        {
            // First resolve session chain references
            var resolved = _sessionManager.ResolveChainReferences(input);
            // Then resolve regular variables
            return _variableResolver.Resolve(resolved, localVariables, fileVariables);
        }

        private static bool IsContentHeader(string headerName)
        {
            return headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("Content-Language", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("Content-Location", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("Content-MD5", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("Content-Range", StringComparison.OrdinalIgnoreCase) ||
                   headerName.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase);
        }

        private static Encoding GetEncodingFromContentType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return null;

            try
            {
                var parts = contentType.Split(';');
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (trimmed.StartsWith("charset=", StringComparison.OrdinalIgnoreCase))
                    {
                        var charset = trimmed.Substring(8).Trim().Trim('"');
                        return Encoding.GetEncoding(charset);
                    }
                }
            }
            catch
            {
                // Invalid encoding - fall back to default
            }

            return null;
        }

        private static CookieInfo ParseCookie(string setCookieHeader)
        {
            if (string.IsNullOrEmpty(setCookieHeader))
                return null;

            var cookie = new CookieInfo { RawValue = setCookieHeader };

            var parts = setCookieHeader.Split(';');
            if (parts.Length == 0)
                return null;

            // First part is name=value
            var nameValue = parts[0].Split(new[] { '=' }, 2);
            if (nameValue.Length < 2)
                return null;

            cookie.Name = nameValue[0].Trim();
            cookie.Value = nameValue[1].Trim();

            // Parse attributes
            for (int i = 1; i < parts.Length; i++)
            {
                var attr = parts[i].Trim();
                var attrParts = attr.Split(new[] { '=' }, 2);
                var attrName = attrParts[0].Trim();
                var attrValue = attrParts.Length > 1 ? attrParts[1].Trim() : "";

                if (attrName.Equals("Domain", StringComparison.OrdinalIgnoreCase))
                    cookie.Domain = attrValue;
                else if (attrName.Equals("Path", StringComparison.OrdinalIgnoreCase))
                    cookie.Path = attrValue;
                else if (attrName.Equals("Expires", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTime.TryParse(attrValue, out var expires))
                        cookie.Expires = expires;
                }
                else if (attrName.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase))
                    cookie.HttpOnly = true;
                else if (attrName.Equals("Secure", StringComparison.OrdinalIgnoreCase))
                    cookie.Secure = true;
                else if (attrName.Equals("SameSite", StringComparison.OrdinalIgnoreCase))
                    cookie.SameSite = attrValue;
            }

            return cookie;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _handler?.Dispose();
        }
    }
}
