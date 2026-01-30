using System.ComponentModel.Composition;
using System.Diagnostics;
using Microsoft.VisualStudio.Utilities;

namespace VSEndpoint.ContentType
{
    /// <summary>
    /// Defines content types for .http and .rest files.
    /// MEF exports register these with the VS editor.
    /// </summary>
    public static class HttpContentTypeDefinition
    {
        public const string ContentTypeName = "http";

        static HttpContentTypeDefinition()
        {
            Debug.WriteLine("[VSEndpoint] Static constructor - HttpContentTypeDefinition loaded by MEF");
        }

        [Export]
        [Name(ContentTypeName)]
        [BaseDefinition("text")]
        public static ContentTypeDefinition HttpContentType { get; set; }

        [Export]
        [FileExtension(".http")]
        [ContentType(ContentTypeName)]
        public static FileExtensionToContentTypeDefinition HttpFileExtension { get; set; }

        [Export]
        [FileExtension(".rest")]
        [ContentType(ContentTypeName)]
        public static FileExtensionToContentTypeDefinition RestFileExtension { get; set; }
    }
}
