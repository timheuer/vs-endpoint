using System;
using System.Collections.Generic;

namespace VSEndpoint.Services.Execution
{
    /// <summary>
    /// Represents timing metrics for an HTTP request.
    /// </summary>
    public class RequestTimingMetrics
    {
        public TimeSpan DnsResolution { get; set; }
        public TimeSpan ConnectionEstablishment { get; set; }
        public TimeSpan TlsHandshake { get; set; }
        public TimeSpan TimeToFirstByte { get; set; }
        public TimeSpan ContentDownload { get; set; }
        public TimeSpan TotalTime { get; set; }
    }

    /// <summary>
    /// Represents the result of an HTTP request execution.
    /// </summary>
    public class HttpExecutionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        // Request details (after variable substitution)
        public string RequestMethod { get; set; }
        public string RequestUrl { get; set; }
        public Dictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string RequestBody { get; set; }

        // Response details
        public int StatusCode { get; set; }
        public string StatusDescription { get; set; }
        public Dictionary<string, string> ResponseHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<CookieInfo> Cookies { get; set; } = new List<CookieInfo>();
        public string ResponseBody { get; set; }
        public byte[] ResponseBodyBytes { get; set; }

        // Metadata
        public long ResponseSizeBytes { get; set; }
        public string ContentType { get; set; }
        public RequestTimingMetrics Timing { get; set; } = new RequestTimingMetrics();
        public DateTime ExecutedAt { get; set; }

        /// <summary>
        /// Formatted response size (e.g., "1.5 KB", "2.3 MB").
        /// </summary>
        public string FormattedSize
        {
            get
            {
                if (ResponseSizeBytes < 1024)
                    return $"{ResponseSizeBytes} B";
                if (ResponseSizeBytes < 1024 * 1024)
                    return $"{ResponseSizeBytes / 1024.0:F1} KB";
                return $"{ResponseSizeBytes / (1024.0 * 1024.0):F1} MB";
            }
        }

        /// <summary>
        /// Formatted response time (e.g., "150 ms", "1.2 s").
        /// </summary>
        public string FormattedTime
        {
            get
            {
                var ms = Timing.TotalTime.TotalMilliseconds;
                if (ms < 1000)
                    return $"{ms:F0} ms";
                return $"{ms / 1000.0:F2} s";
            }
        }

        /// <summary>
        /// Indicates if the status code represents success (2xx).
        /// </summary>
        public bool IsSuccessStatusCode => StatusCode >= 200 && StatusCode < 300;

        /// <summary>
        /// Indicates if the response content is JSON.
        /// </summary>
        public bool IsJson => ContentType?.Contains("application/json") == true || ContentType?.Contains("+json") == true;

        /// <summary>
        /// Indicates if the response content is XML.
        /// </summary>
        public bool IsXml => ContentType?.Contains("application/xml") == true || ContentType?.Contains("+xml") == true || ContentType?.Contains("text/xml") == true;

        /// <summary>
        /// Indicates if the response content is HTML.
        /// </summary>
        public bool IsHtml => ContentType?.Contains("text/html") == true;

        /// <summary>
        /// Indicates if the response has cookies.
        /// </summary>
        public bool HasCookies => Cookies != null && Cookies.Count > 0;
    }

    /// <summary>
    /// Represents a cookie from an HTTP response.
    /// </summary>
    public class CookieInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Domain { get; set; }
        public string Path { get; set; }
        public DateTime? Expires { get; set; }
        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }
        public string SameSite { get; set; }

        public string RawValue { get; set; } // Full Set-Cookie header value
    }
}
