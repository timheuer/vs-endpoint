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
        private readonly VSEndpoint.Services.Parser.HttpFileParser _parser;
        private readonly string _filePath;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public SendRequestTagger(ITextBuffer buffer, string filePath)
        {
            _buffer = buffer;
            _parser = new VSEndpoint.Services.Parser.HttpFileParser();
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
                var glyphLineNumber = GetGlyphLineNumber(snapshot, request);
                if (glyphLineNumber <= 0 || glyphLineNumber > snapshot.LineCount)
                {
                    Debug.WriteLine($"[VSEndpoint Glyph] Invalid line {glyphLineNumber} for request");
                    continue;
                }

                var line = snapshot.GetLineFromLineNumber(glyphLineNumber - 1);
                var span = new SnapshotSpan(line.Start, line.Length);

                Debug.WriteLine($"[VSEndpoint Glyph] Yielding tag for line {glyphLineNumber}: {request.Method} {request.Url}");

                yield return new TagSpan<SendRequestTag>(
                    span,
                    new SendRequestTag(glyphLineNumber, request.Method, request.Url, request.Name));
            }
        }

        private static int GetGlyphLineNumber(ITextSnapshot snapshot, HttpRequestDefinition request)
        {
            var startLine = Math.Max(1, request.StartLine);
            var endLine = request.EndLine > 0 ? Math.Min(request.EndLine, snapshot.LineCount) : startLine;

            if (!string.IsNullOrWhiteSpace(request.Method))
            {
                for (int lineNumber = startLine; lineNumber <= endLine; lineNumber++)
                {
                    var lineText = snapshot.GetLineFromLineNumber(lineNumber - 1).GetText().TrimStart();
                    if (lineText.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (lineText.StartsWith(request.Method + " ", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(lineText, request.Method, StringComparison.OrdinalIgnoreCase))
                    {
                        return lineNumber;
                    }
                }
            }

            return startLine;
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

            bool isExecuting = false;

            container.MouseLeftButtonDown += (s, e) =>
            {
                // Prevent multiple clicks
                if (isExecuting)
                {
                    e.Handled = true;
                    return;
                }

                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        
                        // Set executing state
                        isExecuting = true;
                        container.Opacity = 0.5;
                        image.Cursor = Cursors.Wait;
                        container.Cursor = Cursors.Wait;

                        await ExecuteRequestAsync(sendTag.LineNumber);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error executing request: {ex.Message}");
                    }
                    finally
                    {
                        // Reset state
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        isExecuting = false;
                        container.Opacity = 1.0;
                        image.Cursor = Cursors.Hand;
                        container.Cursor = Cursors.Hand;
                    }
                });
                e.Handled = true;
            };

            // Hover effect - slight scale
            container.MouseEnter += (s, e) =>
            {
                if (!isExecuting)
                {
                    container.RenderTransform = new ScaleTransform(1.15, 1.15);
                    container.RenderTransformOrigin = new Point(0.5, 0.5);
                }
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

        private async System.Threading.Tasks.Task ExecuteRequestAsync(int lineNumber)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Move cursor to the request line
            try
            {
                var caretLine = _view.TextSnapshot.GetLineFromLineNumber(lineNumber - 1);
                _view.Caret.MoveTo(caretLine.Start);

                // Invoke the Send Request command
                if (Commands.VSEndpointCommandHandler.Instance != null)
                {
                    await Commands.VSEndpointCommandHandler.Instance.ExecuteRequestAtLineAsync(lineNumber);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error executing request: {ex.Message}");
                throw; // Re-throw to be caught by the caller for UI reset
            }
        }
    }
}
