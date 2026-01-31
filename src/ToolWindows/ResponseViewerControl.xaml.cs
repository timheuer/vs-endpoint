using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using VSEndpoint.Services.Execution;

namespace VSEndpoint.ToolWindows
{
    /// <summary>
    /// Interaction logic for ResponseViewerControl.xaml
    /// </summary>
    public partial class ResponseViewerControl : UserControl
    {
        private HttpExecutionResult _currentResult;
        private bool _isTreeViewMode;
        private JsonDocument _jsonDocument; // Keep alive for tree view expansion
        private FoldingManager _foldingManager;
        private BraceFoldingStrategy _foldingStrategy;

        public ResponseViewerControl()
        {
            InitializeComponent();

            // Configure AvalonEdit
            ConfigureEditor(BodyEditor);

            // Apply VS theme to AvalonEdit
            ApplyVsThemeToEditor(BodyEditor);

            // Subscribe to VS theme changes
            VSColorTheme.ThemeChanged += OnThemeChanged;
        }

        private void ConfigureEditor(TextEditor editor)
        {
            // Enable line highlighting
            editor.Options.EnableHyperlinks = false;
            editor.Options.EnableEmailHyperlinks = false;

            // Setup folding
            _foldingManager = FoldingManager.Install(editor.TextArea);
            _foldingStrategy = new BraceFoldingStrategy();
        }

        private void ApplyVsThemeToEditor(TextEditor editor)
        {
            // Get VS theme colors
            var bgColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            var fgColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);

            var bgBrush = new SolidColorBrush(Color.FromArgb(bgColor.A, bgColor.R, bgColor.G, bgColor.B));
            var fgBrush = new SolidColorBrush(Color.FromArgb(fgColor.A, fgColor.R, fgColor.G, fgColor.B));

            editor.Background = bgBrush;
            editor.Foreground = fgBrush;

            // Line number colors - muted foreground
            editor.LineNumbersForeground = new SolidColorBrush(Color.FromArgb(128, fgColor.R, fgColor.G, fgColor.B));

            // Current line highlight - detect dark vs light theme
            bool isDarkTheme = bgColor.R < 128 && bgColor.G < 128 && bgColor.B < 128;

            // Set current line background
            var lineHighlightColor = isDarkTheme
                ? Color.FromArgb(40, 255, 255, 255)  // Subtle white overlay for dark
                : Color.FromArgb(30, 0, 0, 0);       // Subtle black overlay for light
            editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(lineHighlightColor);
            editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(60, fgColor.R, fgColor.G, fgColor.B)), 1);

            // Apply theme-aware syntax highlighting
            ApplyThemeAwareSyntaxHighlighting(editor, isDarkTheme);
        }

        private void ApplyThemeAwareSyntaxHighlighting(TextEditor editor, bool isDarkTheme)
        {
            // No custom highlighting needed - we'll use AvalonEdit's built-in definitions
            // which already have good theme support
        }

        private IHighlightingDefinition GetHighlightingForContentType(HttpExecutionResult result)
        {
            if (result.IsJson)
            {
                // JavaScript highlighting works well for JSON
                return HighlightingManager.Instance.GetDefinition("JavaScript");
            }
            else if (result.IsXml)
            {
                return HighlightingManager.Instance.GetDefinition("XML");
            }
            else if (result.IsHtml)
            {
                return HighlightingManager.Instance.GetDefinition("HTML");
            }
            
            return null;
        }

        private void UpdateFolding()
        {
            if (_foldingManager != null && _foldingStrategy != null)
            {
                _foldingStrategy.UpdateFoldings(_foldingManager, BodyEditor.Document);
            }
        }

        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
            // Refresh theme on editor
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ApplyVsThemeToEditor(BodyEditor);
                InvalidateVisual();
            });
        }

        /// <summary>
        /// Displays an HTTP execution result in the viewer.
        /// </summary>
        public void DisplayResult(HttpExecutionResult result)
        {
            _currentResult = result;

            // Hide empty state, show content
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            BodyTab.IsChecked = true;
            BodyContent.Visibility = Visibility.Visible;

            // Update metadata bar
            UpdateMetadataBar(result);

            // Update body tab
            UpdateBodyTab(result);

            // Update headers tab
            UpdateHeadersTab(result);

            // Update cookies tab
            UpdateCookiesTab(result);

            // Update raw tab
            UpdateRawTab(result);

            // Update status bar
            if (result.Success)
            {
                ContentTypeText.Text = result.ContentType ?? "";
            }
            else
            {
                ContentTypeText.Text = "Error";
            }
        }

        /// <summary>
        /// Shows the loading state.
        /// </summary>
        public void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            StatusBadge.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Hides the loading state.
        /// </summary>
        public void HideLoading()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Clears the viewer to empty state.
        /// </summary>
        public void Clear()
        {
            _currentResult = null;
            _isTreeViewMode = false;
            DisposeJsonDocument();
            EmptyStatePanel.Visibility = Visibility.Visible;
            HideAllContent();
            StatusBadge.Visibility = Visibility.Collapsed;
            ResponseTimeText.Text = "--";
            ResponseSizeText.Text = "--";
            BodyEditor.Text = string.Empty;
            BodyEditor.SyntaxHighlighting = null;
            RawTextBox.Text = string.Empty;
            HeadersGrid.ItemsSource = null;
            CookiesGrid.ItemsSource = null;
            CookiesTab.Visibility = Visibility.Collapsed;
            JsonTreeView.Items.Clear();
            TreeViewToggle.IsChecked = false;
            TreeViewToggleText.Text = "Tree View";
            BodyTab.IsChecked = true;
            ContentTypeText.Text = "";
        }

        private void HideAllContent()
        {
            // Guard against calls during InitializeComponent
            if (BodyContent == null) return;
            
            BodyContent.Visibility = Visibility.Collapsed;
            HeadersGrid.Visibility = Visibility.Collapsed;
            CookiesGrid.Visibility = Visibility.Collapsed;
            RawContent.Visibility = Visibility.Collapsed;
        }

        private void ViewTab_Checked(object sender, RoutedEventArgs e)
        {
            // Guard against calls during InitializeComponent
            if (BodyContent == null) return;
            
            if (sender is RadioButton tab)
            {
                HideAllContent();

                if (tab == BodyTab)
                {
                    BodyContent.Visibility = Visibility.Visible;
                }
                else if (tab == HeadersTab)
                {
                    HeadersGrid.Visibility = Visibility.Visible;
                }
                else if (tab == CookiesTab)
                {
                    CookiesGrid.Visibility = Visibility.Visible;
                }
                else if (tab == RawTab)
                {
                    RawContent.Visibility = Visibility.Visible;
                }
            }
        }

        private void DisposeJsonDocument()
        {
            _jsonDocument?.Dispose();
            _jsonDocument = null;
        }

        private void UpdateMetadataBar(HttpExecutionResult result)
        {
            HideLoading();

            if (result.Success)
            {
                // Status code badge
                StatusBadge.Visibility = Visibility.Visible;
                StatusCodeText.Text = $"{result.StatusCode} {result.StatusDescription}";
                StatusBadge.Background = GetStatusCodeBrush(result.StatusCode);

                // Time and size
                ResponseTimeText.Text = result.FormattedTime;
                ResponseSizeText.Text = result.FormattedSize;

                // Show tree view toggle for JSON and reset its state
                TreeViewToggle.Visibility = result.IsJson ? Visibility.Visible : Visibility.Collapsed;
                if (result.IsJson)
                {
                    _isTreeViewMode = false;
                    TreeViewToggle.IsChecked = false;
                    TreeViewToggleText.Text = "Tree View";
                }
            }
            else
            {
                StatusBadge.Visibility = Visibility.Visible;
                StatusCodeText.Text = "Error";
                StatusBadge.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                TreeViewToggle.Visibility = Visibility.Collapsed;
            }
        }

        private static Brush GetStatusCodeBrush(int statusCode)
        {
            if (statusCode >= 200 && statusCode < 300)
                return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            if (statusCode >= 300 && statusCode < 400)
                return new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue
            if (statusCode >= 400 && statusCode < 500)
                return new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
            if (statusCode >= 500)
                return new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red

            return new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
        }

        private void UpdateBodyTab(HttpExecutionResult result)
        {
            if (!result.Success)
            {
                BodyEditor.Text = result.ErrorMessage;
                BodyEditor.SyntaxHighlighting = null;
                BodyEditor.Visibility = Visibility.Visible;
                JsonTreeView.Visibility = Visibility.Collapsed;
                return;
            }

            var body = result.ResponseBody ?? string.Empty;

            // Set syntax highlighting based on content type
            BodyEditor.SyntaxHighlighting = GetHighlightingForContentType(result);
            
            if (result.IsJson)
            {
                // Pretty-print JSON
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    body = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                }
                catch
                {
                    // Not valid JSON despite content type
                }
            }

            BodyEditor.Text = body;

            // Update code folding
            UpdateFolding();

            // Build tree view for JSON (lazy load for performance)
            if (result.IsJson)
            {
                BuildJsonTreeView(result.ResponseBody);
            }
            else
            {
                JsonTreeView.Items.Clear();
            }

            // Show appropriate view
            BodyEditor.Visibility = _isTreeViewMode && result.IsJson ? Visibility.Collapsed : Visibility.Visible;
            JsonTreeView.Visibility = _isTreeViewMode && result.IsJson ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildJsonTreeView(string json)
        {
            JsonTreeView.Items.Clear();
            DisposeJsonDocument();

            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                // Keep the document alive - do not dispose until new result or clear
                _jsonDocument = JsonDocument.Parse(json);
                var rootItem = CreateTreeViewItem(_jsonDocument.RootElement, "root", "$");
                JsonTreeView.Items.Add(rootItem);
                rootItem.IsExpanded = true;
            }
            catch
            {
                // Invalid JSON
                DisposeJsonDocument();
            }
        }

        private void OnTreeItemExpanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.Tag is JsonNodeInfo info)
            {
                item.Expanded -= OnTreeItemExpanded;
                item.Items.Clear();

                if (info.Element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in info.Element.EnumerateObject())
                    {
                        var childPath = $"{info.Path}.{property.Name}";
                        item.Items.Add(CreateTreeViewItem(property.Value, property.Name, childPath));
                    }
                }
                else if (info.Element.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (var element in info.Element.EnumerateArray())
                    {
                        var childPath = $"{info.Path}[{index}]";
                        item.Items.Add(CreateTreeViewItem(element, $"[{index}]", childPath));
                        index++;
                    }
                }
            }
        }

        private TreeViewItem CreateTreeViewItem(JsonElement element, string name, string path)
        {
            var item = new TreeViewItem
            {
                Tag = new JsonNodeInfo { Path = path, Element = element }
            };

            // Use theme-friendly colors
            var stringColor = new SolidColorBrush(Color.FromRgb(206, 145, 120));   // Soft orange for strings
            var numberColor = new SolidColorBrush(Color.FromRgb(181, 206, 168));   // Light green for numbers
            var boolColor = new SolidColorBrush(Color.FromRgb(86, 156, 214));      // Blue for booleans
            var nullColor = new SolidColorBrush(Color.FromRgb(128, 128, 128));     // Gray for null
            var countColor = new SolidColorBrush(Color.FromRgb(128, 128, 128));    // Gray for counts

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    item.Header = CreateHeader(name, element.EnumerateObject().Count() + " properties", countColor);
                    if (element.EnumerateObject().Any())
                    {
                        item.Items.Add(new TreeViewItem { Header = "Loading..." });
                        item.Expanded += OnTreeItemExpanded;
                    }
                    break;

                case JsonValueKind.Array:
                    item.Header = CreateHeader(name, $"[{element.GetArrayLength()} items]", countColor);
                    if (element.GetArrayLength() > 0)
                    {
                        item.Items.Add(new TreeViewItem { Header = "Loading..." });
                        item.Expanded += OnTreeItemExpanded;
                    }
                    break;

                case JsonValueKind.String:
                    item.Header = CreateHeader(name, $"\"{element.GetString()}\"", stringColor);
                    break;

                case JsonValueKind.Number:
                    item.Header = CreateHeader(name, element.GetRawText(), numberColor);
                    break;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    item.Header = CreateHeader(name, element.GetRawText(), boolColor);
                    break;

                case JsonValueKind.Null:
                    item.Header = CreateHeader(name, "null", nullColor);
                    break;
            }

            return item;
        }

        private static StackPanel CreateHeader(string name, string value, Brush valueBrush)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var nameBlock = new TextBlock { Text = name + ": ", FontWeight = FontWeights.SemiBold };
            nameBlock.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            panel.Children.Add(nameBlock);

            panel.Children.Add(new TextBlock { Text = value, Foreground = valueBrush });
            return panel;
        }

        private void UpdateHeadersTab(HttpExecutionResult result)
        {
            if (!result.Success)
            {
                HeadersGrid.ItemsSource = null;
                return;
            }

            var headers = result.ResponseHeaders
                .Select(h => new KeyValuePair<string, string>(h.Key, h.Value))
                .OrderBy(h => h.Key)
                .ToList();

            HeadersGrid.ItemsSource = headers;
        }

        private void UpdateCookiesTab(HttpExecutionResult result)
        {
            if (!result.Success || !result.HasCookies)
            {
                CookiesTab.Visibility = Visibility.Collapsed;
                CookiesGrid.ItemsSource = null;
                return;
            }

            CookiesTab.Visibility = Visibility.Visible;
            CookiesGrid.ItemsSource = result.Cookies;
        }

        private void UpdateRawTab(HttpExecutionResult result)
        {
            if (!result.Success)
            {
                RawTextBox.Text = result.ErrorMessage;
                return;
            }

            var sb = new StringBuilder();

            // Request info
            sb.AppendLine("=== REQUEST ===");
            sb.AppendLine($"{result.RequestMethod} {result.RequestUrl}");
            foreach (var header in result.RequestHeaders)
            {
                sb.AppendLine($"{header.Key}: {header.Value}");
            }
            if (!string.IsNullOrEmpty(result.RequestBody))
            {
                sb.AppendLine();
                sb.AppendLine(result.RequestBody);
            }

            sb.AppendLine();
            sb.AppendLine("=== RESPONSE ===");
            sb.AppendLine($"HTTP/1.1 {result.StatusCode} {result.StatusDescription}");
            foreach (var header in result.ResponseHeaders)
            {
                sb.AppendLine($"{header.Key}: {header.Value}");
            }
            sb.AppendLine();
            sb.AppendLine(result.ResponseBody);

            RawTextBox.Text = sb.ToString();
        }

        private void TreeViewToggle_Click(object sender, RoutedEventArgs e)
        {
            _isTreeViewMode = TreeViewToggle.IsChecked == true;

            // Update button text based on state
            TreeViewToggleText.Text = _isTreeViewMode ? "Text View" : "Tree View";

            if (_currentResult != null && _currentResult.IsJson)
            {
                BodyEditor.Visibility = _isTreeViewMode ? Visibility.Collapsed : Visibility.Visible;
                JsonTreeView.Visibility = _isTreeViewMode ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (JsonTreeView.SelectedItem is TreeViewItem item && item.Tag is JsonNodeInfo info)
            {
                Clipboard.SetText(info.Path);
            }
        }

        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            if (JsonTreeView.SelectedItem is TreeViewItem item && item.Tag is JsonNodeInfo info)
            {
                var value = info.Element.ValueKind switch
                {
                    JsonValueKind.String => info.Element.GetString(),
                    _ => info.Element.GetRawText()
                };
                Clipboard.SetText(value ?? string.Empty);
            }
        }

        private class JsonNodeInfo
        {
            public string Path { get; set; }
            public JsonElement Element { get; set; }
        }
    }

    /// <summary>
    /// Folding strategy for JSON/XML that folds on matching braces and brackets.
    /// </summary>
    internal class BraceFoldingStrategy
    {
        public void UpdateFoldings(FoldingManager manager, ICSharpCode.AvalonEdit.Document.TextDocument document)
        {
            var foldings = CreateNewFoldings(document);
            manager.UpdateFoldings(foldings, -1);
        }

        private IEnumerable<NewFolding> CreateNewFoldings(ICSharpCode.AvalonEdit.Document.TextDocument document)
        {
            var foldings = new List<NewFolding>();
            var openBraces = new Stack<int>();
            var openBrackets = new Stack<int>();

            var text = document.Text;
            bool inString = false;
            char prevChar = '\0';

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // Track string state to avoid folding inside strings
                if (c == '"' && prevChar != '\\')
                {
                    inString = !inString;
                }

                if (!inString)
                {
                    switch (c)
                    {
                        case '{':
                            openBraces.Push(i);
                            break;
                        case '}':
                            if (openBraces.Count > 0)
                            {
                                int startOffset = openBraces.Pop();
                                // Only fold if there are multiple lines
                                if (HasMultipleLines(text, startOffset, i))
                                {
                                    foldings.Add(new NewFolding(startOffset, i + 1) { Name = "{ ... }" });
                                }
                            }
                            break;
                        case '[':
                            openBrackets.Push(i);
                            break;
                        case ']':
                            if (openBrackets.Count > 0)
                            {
                                int startOffset = openBrackets.Pop();
                                if (HasMultipleLines(text, startOffset, i))
                                {
                                    foldings.Add(new NewFolding(startOffset, i + 1) { Name = "[ ... ]" });
                                }
                            }
                            break;
                    }
                }

                prevChar = c;
            }

            foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
            return foldings;
        }

        private static bool HasMultipleLines(string text, int start, int end)
        {
            for (int i = start; i < end && i < text.Length; i++)
            {
                if (text[i] == '\n')
                    return true;
            }
            return false;
        }
    }
}
