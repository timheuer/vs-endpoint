using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using VSEndpoint.Services.Parser;

namespace VSEndpoint.Adornments
{
    /// <summary>
    /// Tag representing a "Send Request" action link location.
    /// </summary>
    public class SendRequestTag : IGlyphTag
    {
        public int LineNumber { get; }
        public string RequestName { get; }
        public string Method { get; }
        public string Url { get; }

        public SendRequestTag(int lineNumber, string method, string url, string requestName)
        {
            LineNumber = lineNumber;
            Method = method;
            Url = url;
            RequestName = requestName;
        }
    }

    /// <summary>
    /// Tagger that identifies HTTP request starting lines.
    /// </summary>
    public class SendRequestTagger : ITagger<SendRequestTag>
    {
        private readonly ITextBuffer _buffer;
        private readonly HttpFileParser _parser;
        private readonly string _filePath;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public SendRequestTagger(ITextBuffer buffer, string filePath)
        {
            _buffer = buffer;
            _parser = new HttpFileParser();
            _filePath = filePath;
            _buffer.Changed += OnBufferChanged;
            Debug.WriteLine($"[VSEndpoint Glyph] SendRequestTagger created for: {filePath}");
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (e.Changes.Count > 0)
            {
                var span = new SnapshotSpan(_buffer.CurrentSnapshot, 0, _buffer.CurrentSnapshot.Length);
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(span));
            }
        }

        public IEnumerable<ITagSpan<SendRequestTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            Debug.WriteLine($"[VSEndpoint Glyph] GetTags called. FilePath={_filePath}, IsHttpFile={IsHttpFile(_filePath)}");

            // Only process .http/.rest files
            if (!IsHttpFile(_filePath))
            {
                Debug.WriteLine($"[VSEndpoint Glyph] Skipping non-HTTP file: {_filePath}");
                yield break;
            }

            if (spans.Count == 0)
            {
                Debug.WriteLine($"[VSEndpoint Glyph] No spans to process");
                yield break;
            }

            var snapshot = spans[0].Snapshot;
            var content = snapshot.GetText();
            var parseResult = _parser.Parse(content);

            Debug.WriteLine($"[VSEndpoint Glyph] Parsed {parseResult.Requests.Count} requests");

            foreach (var request in parseResult.Requests)
            {
                if (request.StartLine <= 0 || request.StartLine > snapshot.LineCount)
                {
                    Debug.WriteLine($"[VSEndpoint Glyph] Invalid line {request.StartLine} for request");
                    continue;
                }

                var line = snapshot.GetLineFromLineNumber(request.StartLine - 1);
                var span = new SnapshotSpan(line.Start, 0);

                Debug.WriteLine($"[VSEndpoint Glyph] Yielding tag for line {request.StartLine}: {request.Method} {request.Url}");

                yield return new TagSpan<SendRequestTag>(
                    span,
                    new SendRequestTag(request.StartLine, request.Method, request.Url, request.Name));
            }
        }

        private static bool IsHttpFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;
            var ext = Path.GetExtension(filePath);
            return ext.Equals(".http", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".rest", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Provider for the SendRequest tagger.
    /// </summary>
    [Export(typeof(ITaggerProvider))]
    [ContentType("text")]
    [TagType(typeof(SendRequestTag))]
    public class SendRequestTaggerProvider : ITaggerProvider
    {
        static SendRequestTaggerProvider()
        {
            Debug.WriteLine("[VSEndpoint Glyph] Static constructor - TaggerProvider type loaded by MEF");
        }

        [Import]
        public ITextDocumentFactoryService TextDocumentFactoryService { get; set; }

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            Debug.WriteLine($"[VSEndpoint Glyph] TaggerProvider.CreateTagger called");

            string filePath = null;
            if (TextDocumentFactoryService.TryGetTextDocument(buffer, out var textDocument))
            {
                filePath = textDocument.FilePath;
                Debug.WriteLine($"[VSEndpoint Glyph] Document path: {filePath}");
            }
            else
            {
                Debug.WriteLine($"[VSEndpoint Glyph] Could not get document for buffer");
            }

            // Get content type info for debugging
            var contentType = buffer.ContentType;
            Debug.WriteLine($"[VSEndpoint Glyph] Buffer ContentType: {contentType.TypeName}, BaseTypes: {string.Join(", ", contentType.BaseTypes.Select(t => t.TypeName))}");

            return buffer.Properties.GetOrCreateSingletonProperty(
                typeof(SendRequestTagger),
                () => new SendRequestTagger(buffer, filePath)) as ITagger<T>;
        }
    }

    /// <summary>
    /// Glyph factory creating "Send" buttons in the margin.
    /// </summary>
    [Export(typeof(IGlyphFactoryProvider))]
    [Name("SendRequestGlyph")]
    [Order(After = "VsTextMarker")]
    [ContentType("text")]
    [TagType(typeof(SendRequestTag))]
    public class SendRequestGlyphFactoryProvider : IGlyphFactoryProvider
    {
        static SendRequestGlyphFactoryProvider()
        {
            Debug.WriteLine("[VSEndpoint Glyph] Static constructor - GlyphFactoryProvider type loaded by MEF");
        }

        public IGlyphFactory GetGlyphFactory(IWpfTextView view, IWpfTextViewMargin margin)
        {
            Debug.WriteLine($"[VSEndpoint Glyph] GlyphFactoryProvider.GetGlyphFactory called");
            return new SendRequestGlyphFactory(view);
        }
    }

    /// <summary>
    /// Factory that creates the visual glyph (play button) for each request.
    /// </summary>
    public class SendRequestGlyphFactory : IGlyphFactory
    {
        private readonly IWpfTextView _view;

        public SendRequestGlyphFactory(IWpfTextView view)
        {
            _view = view;
        }

        public UIElement GenerateGlyph(IWpfTextViewLine line, IGlyphTag tag)
        {
            if (!(tag is SendRequestTag sendTag))
                return null;

            // Use CrispImage with KnownMonikers for VS-themed icon
            var image = new CrispImage
            {
                Moniker = KnownMonikers.Run,  // Green play button
                Width = 16,
                Height = 16,
                ToolTip = $"Send Request: {sendTag.Method} {TruncateUrl(sendTag.Url)}",
                Cursor = Cursors.Hand
            };

            // Wrap in a border for click handling and hover effects
            var container = new Border
            {
                Child = image,
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = image.ToolTip
            };

            container.MouseLeftButtonDown += (s, e) =>
            {
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ExecuteRequest(sendTag.LineNumber);
                });
                e.Handled = true;
            };

            // Hover effect - slight scale
            container.MouseEnter += (s, e) =>
            {
                container.RenderTransform = new ScaleTransform(1.15, 1.15);
                container.RenderTransformOrigin = new Point(0.5, 0.5);
            };
            container.MouseLeave += (s, e) =>
            {
                container.RenderTransform = null;
            };

            return container;
        }

        private static string TruncateUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;
            return url.Length > 50 ? url.Substring(0, 47) + "..." : url;
        }

        private void ExecuteRequest(int lineNumber)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Move cursor to the request line and execute send command
            try
            {
                var caretLine = _view.TextSnapshot.GetLineFromLineNumber(lineNumber - 1);
                _view.Caret.MoveTo(caretLine.Start);

                // Invoke the Send Request command
                Commands.VSEndpointCommandHandler.Instance?.ExecuteRequestAtLine(lineNumber);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error executing request: {ex.Message}");
            }
        }
    }
}
