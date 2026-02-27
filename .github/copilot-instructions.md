# VS Endpoint Copilot Instructions

## Build, test, and lint commands

```powershell
# Build (VS 2022 MSBuild)
& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" src/vs-endpoint.csproj /t:Build /p:Configuration=Debug

# Build (VS 18 Canary MSBuild)
& "C:\Program Files\Microsoft Visual Studio\18\Canary\MSBuild\Current\Bin\MSBuild.exe" src/vs-endpoint.csproj /t:Build /p:Configuration=Debug

# Clean rebuild
& "C:\Program Files\Microsoft Visual Studio\18\Canary\MSBuild\Current\Bin\MSBuild.exe" src/vs-endpoint.csproj /t:Rebuild /p:Configuration=Debug /v:minimal
```

- Automated test suite: none defined in this repository.
- Single-test command: not applicable (no test project currently present).
- Lint command: none defined in this repository.
- Manual validation flow: run the extension in Experimental Instance (`/rootsuffix Exp`), open a `.http`/`.rest` file, and execute a request via glyph or context menu.

## High-level architecture

- `src/vs_endpointPackage.cs` is the async package entry point; it registers menu resources, `ResponseViewerToolWindow`, and initializes `VSEndpointCommandHandler`.
- Command execution flow:
  1. `VSEndpointCommandHandler` gets the active editor buffer and current line.
  2. `Services/Parser/HttpFileParser` parses file requests and finds the request at the cursor.
  3. `Services/Variables/VariableResolver` resolves placeholders using file/request/env/session context.
  4. `Services/Execution/HttpExecutionService` sends the HTTP request and captures timing, headers, cookies, and body.
  5. `ToolWindows/ResponseViewerControl` renders response metadata and tabs (Body, Headers, Cookies, Raw).
- Adornment path: `Adornments/SendRequestGlyphProvider` tags request lines and renders the run glyph in the editor margin; glyph click delegates to `ExecuteRequestAtLineAsync`.
- Session chaining path: `Services/Session/RequestChainSessionManager` stores named responses and enables `{{requestName.response.body...}}` / header references.

## Key conventions for this repo

- **MEF exports must be `public`** for discovery (especially `ITaggerProvider`, `IGlyphFactoryProvider`, and other exported components).
- Content type setup is intentionally split:
  - `ContentType/HttpContentTypeDefinition` maps `.http` and `.rest` files to `"http"`.
  - Glyph/diagnostic listeners use `[ContentType("text")]` and then filter by file extension at runtime.
- Request execution is UI-thread aware: command handlers switch to main thread via `ThreadHelper.JoinableTaskFactory` before interacting with VS services and tool windows.
- Environment resolution convention:
  - Look for `http-client.env.json` next to the active `.http` file first.
  - Fallback to solution root `http-client.env.json`.
- Variable precedence in `VariableResolver.Resolve` is ordered and significant: request-local -> file-scoped -> manual -> environment file -> request-response provider -> dynamic resolver.
- Request chaining depends on named requests (`@name`) and stores only named responses in session state.
- Theme integration in the response viewer uses `VSColorTheme.ThemeChanged` and `EnvironmentColors` to keep AvalonEdit aligned with the active VS theme.
