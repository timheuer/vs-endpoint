# VS Endpoint Extension - Development Instructions

## Project Overview

This is a Visual Studio extension (VSSDK) for executing HTTP requests from `.http` and `.rest` files. It uses the classic VS SDK with `AsyncPackage`, targeting VS 2022 17.x+.

## Build Instructions

### MSBuild Command

```powershell
# VS 2022
& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" src/vs-endpoint.csproj /t:Build /p:Configuration=Debug

# VS 18 Canary
& "C:\Program Files\Microsoft Visual Studio\18\Canary\MSBuild\Current\Bin\MSBuild.exe" src/vs-endpoint.csproj /t:Build /p:Configuration=Debug
```

### Rebuild with Clean

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Canary\MSBuild\Current\Bin\MSBuild.exe" src/vs-endpoint.csproj /t:Rebuild /p:Configuration=Debug /v:minimal
```

## Critical: MEF Component Discovery

**All MEF-exported types MUST be `public`**, not `internal`. VS MEF discovery fails silently for internal types.

If components aren't loading:

```powershell
Remove-Item "$env:LOCALAPPDATA\Microsoft\VisualStudio\*Exp\ComponentModelCache" -Recurse -Force
```

## Key Architecture Decisions

### Content Type
- The VS built-in HTTP editor uses content type `"Rest"` (not `"http"` or `"text"`)
- Our extension registers `"http"` content type for file extensions `.http` and `.rest`
- Glyph providers use `[ContentType("text")]` to work with all text files, then filter by file extension

### Glyph Margin
- Uses `IGlyphFactoryProvider` and `ITaggerProvider` (both must be public)
- Uses `KnownMonikers.Run` for the play button icon via `CrispImage`
- Requires `Microsoft.VisualStudio.Imaging` and `Microsoft.VisualStudio.Imaging.Interop` namespaces

### Tool Window
- Registered with `[ProvideToolWindow]` attribute on the package
- Uses WPF `UserControl` for the response viewer
- AvalonEdit for syntax highlighting with custom VS Code Dark+/Light+ themes

### VS Theme Integration
- Use `VsColors` and `VsBrushes` from `Microsoft.VisualStudio.PlatformUI`
- Subscribe to `VSColorTheme.ThemeChanged` for dynamic updates
- Map VS theme colors to AvalonEdit syntax highlighting

## Project Structure

```
src/
├── Adornments/SendRequestGlyphProvider.cs  # Glyph margin buttons
├── Commands/VSEndpointCommandHandler.cs    # Menu commands
├── ContentType/HttpContentTypeDefinition.cs # Content type registration
├── Diagnostics/MefDiagnostics.cs           # MEF loading diagnostics
├── Services/
│   ├── Execution/HttpExecutionService.cs   # HTTP client
│   ├── Parser/HttpFileParser.cs            # .http file parser
│   ├── Session/RequestChainSessionManager.cs
│   └── Variables/VariableResolver.cs
├── ToolWindows/
│   ├── ResponseViewerControl.xaml          # WPF response viewer
│   └── ResponseViewerToolWindow.cs         # Tool window pane
├── vs_endpointPackage.cs                   # AsyncPackage entry point
└── VSEndpointPackage.vsct                  # Command table
```

## NuGet Packages

- `Microsoft.VisualStudio.SDK` 17.0.32112.339 - Core VS SDK
- `Microsoft.VSSDK.BuildTools` 17.14.2120 - Build tools
- `AvalonEdit` 6.3.0.90 - Syntax highlighting editor
- `System.Text.Json` 8.0.5 - JSON parsing

## References Required

The csproj must include these framework references:
- `System.Xaml` - Required for WPF
- `PresentationCore`, `PresentationFramework`, `WindowsBase` - WPF
- `System.Net.Http` - HTTP client

## Common Issues

### MEF Components Not Loading
1. Ensure all exported types are `public`
2. Clear ComponentModelCache (see above)
3. Check Output > ActivityLog for MEF errors

### VSCT Build Errors
- Use standard VS icon GUIDs from `guidOfficeIcon` or `ImageCatalogGuid`
- Don't use icon IDs that don't exist in the catalog

### CodeLens API Limitations
- `IAsyncCodeLensDataPointProvider` requires out-of-proc components
- VS's REST language service (`Microsoft.WebTools.Languages.Rest.VS.dll`) doesn't emit CodeLens descriptors for HTTP requests
- Use glyph margin or text adornments instead for custom indicators

## Testing

1. Build the VSIX
2. Launch VS Experimental Instance (F5 or `/rootsuffix Exp`)
3. Open a `.http` file
4. Verify glyph margin play buttons appear on request lines
5. Click to execute and view response in tool window
