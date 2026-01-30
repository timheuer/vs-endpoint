using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace VSEndpoint.ToolWindows
{
    /// <summary>
    /// HTTP Response Viewer tool window pane.
    /// Configured as a document-like tool window that can be positioned near the editor.
    /// </summary>
    [Guid(WindowGuidString)]
    public class ResponseViewerToolWindow : ToolWindowPane
    {
        public const string WindowGuidString = "e8f71c2d-7a4b-4e9f-8c3d-5a6b7c8d9e0f";
        public static readonly Guid WindowGuid = new Guid(WindowGuidString);

        private readonly ResponseViewerControl _control;

        public ResponseViewerToolWindow() : base(null)
        {
            Caption = "HTTP Response";
            BitmapResourceID = 301;
            BitmapIndex = 0;

            _control = new ResponseViewerControl();
            Content = _control;
        }

        /// <summary>
        /// Gets the response viewer control for external access.
        /// </summary>
        public ResponseViewerControl ViewerControl => _control;
    }
}
