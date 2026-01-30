# Endpoint for Visual Studio

A Visual Studio extension for testing REST APIs directly from `.http` and `.rest` files.

## Features

- **Execute HTTP Requests**: Click the play button in the glyph margin or use the context menu to send requests
- **Response Viewer**: Document-side tool window with:
  - Status code with colored badge
  - Response time and size metrics
  - Tabbed interface: Body, Headers, Cookies, Raw
  - JSON syntax highlighting with VS Code Dark+/Light+ themes (via AvalonEdit)
  - Collapsible JSON tree view with copy path/value support
- **Variable Support**: Use `{{variableName}}` placeholders resolved from environment files
- **Request Chaining**: Reference responses from named requests using `@name` directive
- **VS Theme Integration**: Automatically adapts to Light, Dark, and Blue themes

## Requirements

- Visual Studio 2022 17.0+ (amd64)
- .NET Framework 4.7.2

## Building

### Prerequisites

- Visual Studio 2022 with VSSDK workload
- MSBuild 17.0+

### Build Commands

```powershell
# Build with MSBuild (VS 2022)
& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" src/vs-endpoint.csproj /t:Build /p:Configuration=Debug

# Or VS 18 Canary
& "C:\Program Files\Microsoft Visual Studio\18\Canary\MSBuild\Current\Bin\MSBuild.exe" src/vs-endpoint.csproj /t:Build /p:Configuration=Debug
```

### Debugging

1. Set `src/vs-endpoint.csproj` as startup project
2. Press F5 to launch VS Experimental Instance with `/rootsuffix Exp`
3. Open a `.http` or `.rest` file to test

### Clearing MEF Cache

If MEF components aren't loading, clear the ComponentModelCache:

```powershell
Remove-Item "$env:LOCALAPPDATA\Microsoft\VisualStudio\*Exp\ComponentModelCache" -Recurse -Force
```

## Project Structure

```
src/
├── Adornments/           # Glyph margin play buttons
├── Commands/             # Menu command handlers
├── ContentType/          # HTTP content type definitions
├── Diagnostics/          # MEF diagnostics
├── Services/
│   ├── Execution/        # HTTP client and result types
│   ├── Parser/           # .http file parser
│   ├── Session/          # Request chain session manager
│   └── Variables/        # Variable resolution
├── ToolWindows/          # Response viewer WPF control
├── vs_endpointPackage.cs # AsyncPackage entry point
└── VSEndpointPackage.vsct # Command table
```

## Usage

1. Create a `.http` or `.rest` file:

```http
@baseUrl = https://api.example.com

### Get users
# @name getUsers
GET {{baseUrl}}/users

### Get specific user
GET {{baseUrl}}/users/{{getUsers.response.body.$[0].id}}
Authorization: Bearer {{token}}
```

2. Click the play button (▶) in the glyph margin next to a request
3. View the response in the Endpoint Response Viewer tool window

## Key Dependencies

- `Microsoft.VisualStudio.SDK` 17.0.32112.339
- `Microsoft.VSSDK.BuildTools` 17.14.2120
- `AvalonEdit` 6.3.0.90 - Syntax highlighting
- `System.Text.Json` 8.0.5 - JSON parsing

## License

MIT

## Author

Tim Heuer
