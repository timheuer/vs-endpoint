using System.ComponentModel.Composition;
using System.Diagnostics;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace VSEndpoint.Diagnostics
{
    /// <summary>
    /// Simple MEF diagnostic - logs when ANY text view is created.
    /// This helps verify MEF component discovery is working.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    public sealed class MefDiagnosticListener : IWpfTextViewCreationListener
    {
        static MefDiagnosticListener()
        {
            Debug.WriteLine("[VSEndpoint MEF] *** STATIC CONSTRUCTOR - MefDiagnosticListener loaded ***");
        }

        public MefDiagnosticListener()
        {
            Debug.WriteLine("[VSEndpoint MEF] *** INSTANCE CONSTRUCTOR - MefDiagnosticListener created ***");
        }

        public void TextViewCreated(IWpfTextView textView)
        {
            Debug.WriteLine($"[VSEndpoint MEF] TextViewCreated - ContentType: {textView.TextBuffer.ContentType.TypeName}");
        }
    }
}
