using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using VSEndpoint.Services.Execution;
using VSEndpoint.Services.Parser;
using VSEndpoint.Services.Session;
using VSEndpoint.Services.Variables;
using VSEndpoint.ToolWindows;

namespace VSEndpoint.Commands
{
    /// <summary>
    /// Handles all VS Endpoint commands.
    /// </summary>
    internal sealed class VSEndpointCommandHandler
    {
        public static readonly Guid CommandSetGuid = new Guid("d1e2f3a4-b5c6-7d8e-9f0a-1b2c3d4e5f6a");

        private const int SendRequestCommandId = 0x0100;
        private const int SendRequestContextCommandId = 0x0101;
        private const int ViewResponseCommandId = 0x0102;
        private const int ClearSessionCommandId = 0x0103;

        private readonly AsyncPackage _package;
        private readonly HttpFileParser _parser;
        private readonly VariableResolver _variableResolver;
        private readonly RequestChainSessionManager _sessionManager;
        private HttpExecutionService _executionService;

        public static VSEndpointCommandHandler Instance { get; private set; }

        private VSEndpointCommandHandler(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            _parser = new HttpFileParser();
            _variableResolver = new VariableResolver();
            _sessionManager = new RequestChainSessionManager();
            _executionService = new HttpExecutionService(_variableResolver, _sessionManager);

            // Register commands
            RegisterCommand(commandService, SendRequestCommandId, ExecuteSendRequest, OnBeforeQuerySendRequest);
            RegisterCommand(commandService, SendRequestContextCommandId, ExecuteSendRequest, OnBeforeQuerySendRequest);
            RegisterCommand(commandService, ViewResponseCommandId, ExecuteViewResponse);
            RegisterCommand(commandService, ClearSessionCommandId, ExecuteClearSession);
        }

        public static async System.Threading.Tasks.Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new VSEndpointCommandHandler(package, commandService);
        }

        private void RegisterCommand(OleMenuCommandService commandService, int commandId, EventHandler handler, EventHandler beforeQueryStatus = null)
        {
            var menuCommandId = new CommandID(CommandSetGuid, commandId);
            var menuItem = new OleMenuCommand(handler, menuCommandId);

            if (beforeQueryStatus != null)
            {
                menuItem.BeforeQueryStatus += beforeQueryStatus;
            }

            commandService.AddCommand(menuItem);
        }

        private void OnBeforeQuerySendRequest(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // Show command only for .http/.rest files
                var dte = _package.GetService<EnvDTE.DTE, EnvDTE.DTE>();
                var activeDoc = dte?.ActiveDocument;
                var filePath = activeDoc?.FullName;

                command.Visible = IsHttpFile(filePath);
                command.Enabled = command.Visible;
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

        private void ExecuteSendRequest(object sender, EventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ExecuteSendRequestAsync();
            });
        }

        /// <summary>
        /// Executes the request at a specific line number (called from glyph margin).
        /// </summary>
        public void ExecuteRequestAtLine(int lineNumber)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ExecuteSendRequestAsync(lineNumber);
            });
        }

        private async System.Threading.Tasks.Task ExecuteSendRequestAsync(int? specificLine = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Get current document and cursor position
                var textManager = (IVsTextManager)await _package.GetServiceAsync(typeof(SVsTextManager));
                if (textManager == null)
                {
                    ShowMessage("Could not access text manager.");
                    return;
                }

                textManager.GetActiveView(1, null, out IVsTextView textView);

                if (textView == null)
                {
                    ShowMessage("No active text view found.");
                    return;
                }

                int line;
                if (specificLine.HasValue)
                {
                    line = specificLine.Value - 1;
                }
                else
                {
                    textView.GetCaretPos(out line, out _);
                }

                textView.GetBuffer(out IVsTextLines buffer);
                if (buffer == null)
                {
                    ShowMessage("Could not access text buffer.");
                    return;
                }

                buffer.GetLineCount(out int lineCount);
                buffer.GetLengthOfLine(lineCount - 1, out int lastLineLength);
                buffer.GetLineText(0, 0, lineCount - 1, lastLineLength, out string content);

                // Get file path for environment resolution
                if (buffer is IPersistFileFormat persist)
                {
                    persist.GetCurFile(out string filePath, out _);
                    LoadEnvironmentFile(filePath);
                }

                // Parse and find request at cursor
                var parseResult = _parser.Parse(content);
                var request = _parser.FindRequestAtLine(content, line + 1);

                if (request == null)
                {
                    ShowMessage("No HTTP request found at cursor position. Place your cursor on a request line (e.g., GET https://...)");
                    return;
                }

                // Show tool window and loading state
                var toolWindow = await ShowResponseViewerAsync();
                toolWindow?.ViewerControl.ShowLoading();

                // Execute request
                var result = await _executionService.ExecuteAsync(request, parseResult.FileVariables);

                // Display result
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                toolWindow?.ViewerControl.DisplayResult(result);
            }
            catch (Exception ex)
            {
                ShowMessage($"Error executing request: {ex.Message}");
            }
        }

        private void LoadEnvironmentFile(string httpFilePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(httpFilePath))
                return;

            var directory = Path.GetDirectoryName(httpFilePath);
            var envFile = Path.Combine(directory, "http-client.env.json");

            if (File.Exists(envFile))
            {
                _variableResolver.LoadEnvironmentFile(envFile);
            }
            else
            {
                // Try solution root
                var dte = _package.GetService<EnvDTE.DTE, EnvDTE.DTE>();
                var solutionDir = Path.GetDirectoryName(dte?.Solution?.FullName ?? string.Empty);
                if (!string.IsNullOrEmpty(solutionDir))
                {
                    envFile = Path.Combine(solutionDir, "http-client.env.json");
                    if (File.Exists(envFile))
                    {
                        _variableResolver.LoadEnvironmentFile(envFile);
                    }
                }
            }
        }

        private void ExecuteViewResponse(object sender, EventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ShowResponseViewerAsync();
            });
        }

        private void ExecuteClearSession(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _sessionManager.ClearSession();
            ShowMessage("HTTP session cleared.");
        }

        private async System.Threading.Tasks.Task<ResponseViewerToolWindow> ShowResponseViewerAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var window = await _package.ShowToolWindowAsync(
                typeof(ResponseViewerToolWindow),
                0,
                create: true,
                cancellationToken: _package.DisposalToken);

            return window as ResponseViewerToolWindow;
        }

        private void ShowMessage(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            VsShellUtilities.ShowMessageBox(
                _package,
                message,
                "VS Endpoint",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
