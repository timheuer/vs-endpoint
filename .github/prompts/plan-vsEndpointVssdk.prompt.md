# Plan: Visual Studio HTTP Response Viewer Extension (VSSDK)

**TL;DR**: Build "Endpoint for Visual Studio" using the **classic VS SDK (VSSDK)** with `AsyncPackage` targeting VS 2022 17.x+. The extension provides a document-side tool window with a native WPF response viewer (syntax highlighting via AvalonEdit, collapsible JSON tree, headers, metadata) that executes `.http` and `.rest` file requests via its own engine while maintaining compatibility with VS's environment files and request chaining.

---

## Steps

### 1. Restructure project for pure VSSDK
- Remove `Microsoft.VisualStudio.Extensibility.Sdk` and `Microsoft.VisualStudio.Extensibility.Build` packages from vs-endpoint.csproj
- Keep `Microsoft.VisualStudio.SDK` and add `Microsoft.VSSDK.BuildTools`
- Replace `ExtensionEntrypoint.cs` with an `AsyncPackage`-based package class
- Update source.extension.vsixmanifest to use `ExtensionType="VSSDK"` only
- Register package GUID and ProvideMenuResource attributes

### 2. Create AsyncPackage entry point
- Implement `VSEndpointPackage : AsyncPackage`
- Register `ProvideToolWindow` for the response viewer
- Register `ProvideAutoLoad` for `.http` and `.rest` file contexts using `UIContext` guids
- Use `GetServiceAsync` pattern for VS service access

### 3. Build the HTTP Parser service
- Parse `.http`/`.rest` file format: `###` delimiters, methods, headers, body
- Support `# @name` directive for named requests (chaining)
- Handle multiline bodies and `Content-Type` detection
- Implement variable extraction from `@name=value` declarations

### 4. Implement Environment/Variable resolution service
- Read `http-client.env.json` from project/solution root
- Resolve `{{variableName}}` placeholders with precedence: request-local → file-scoped → environment file
- Support built-in functions: `$datetime`, `$guid`, `$randomInt`, `$timestamp`
- Support `$processEnv` and `$dotenv` access

### 5. Build Request Chain Session Manager
- Store named request responses in session-scoped memory
- Resolve `{{requestName.response.body.path}}` and `{{requestName.response.headers.X-Header}}` syntax
- Clear session on file reload or explicit reset

### 6. Create HTTP Execution Service
- Use `HttpClient` with configurable timeout/redirects
- Support Bearer, Basic Auth, API Key authentication
- Automatic gzip/deflate decompression
- Capture timing metrics (DNS, connect, response)

### 7. Build Response Viewer Tool Window (VSSDK)
- Implement as `ToolWindowPane` with `[ProvideToolWindow(..., DocumentLikeTool = true)]`
- Create WPF `UserControl` with:
  - **Metadata bar**: Status code (colored badge using VS theme), response time, size
  - **TabControl**: Body / Headers / Raw tabs
  - **Body tab**: AvalonEdit for syntax highlighting + toggle to Tree View
  - **JSON Tree View**: WPF `TreeView` with lazy-load for large payloads, context menu (Copy Path, Copy Value)
  - **Headers tab**: `DataGrid` with key-value rows
  - **Raw tab**: Plain `TextBox`

### 8. Integrate VS Theme Support
- Use `VsColors` and `VsBrushes` via `Microsoft.VisualStudio.PlatformUI`
- Subscribe to `VSColorTheme.ThemeChanged` event
- Set `TextElement.Foreground` and background from VS resource keys

### 9. Register Commands via OleMenuCommandService
- Create `VSEndpointCommandSet` with command handlers
- Add "Send Request" command in Tools menu and context menu for `.http`/`.rest` editors
- Use `IVsTextManager` to get current document and cursor position
- Parse request block at cursor → execute → show in tool window

### 10. Implement CodeLens provider (optional, higher complexity)
- Create `ICodeLensCallbackListener` and `IAsyncCodeLensDataPointProvider`
- Register via MEF export for content type `http` and `rest`
- Add "Send Request" lens above `###` delimiters

### 11. Register content types for `.http` and `.rest`
- Export `ContentTypeDefinition` for "http" content type
- Export `FileExtensionToContentTypeDefinition` for `.http` → "http", `.rest` → "http"
- This enables editor features and command targeting

---

## Verification
- Create test `.http` and `.rest` files with multiple requests, variables, and chaining
- Execute requests and verify correct variable substitution, response highlighting, JSON tree
- Test in Light, Dark, and Blue VS themes
- Test with large JSON response (1MB+) — verify tree view performance
- Run in VS Experimental Instance via F5

---

## Decisions
- **VSSDK over VisualStudio.Extensibility**: Better access to editor APIs, CodeLens infrastructure, and mature documentation; in-process for lower latency
- **`.http` and `.rest` support**: Both extensions map to the same "http" content type
- **WPF Native**: Full VS theme integration with `VsColors`/`VsBrushes`
- **AvalonEdit for syntax highlighting**: Mature WPF library, easy to integrate
- **Own execution engine**: Full control over request handling and response capture
