using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VSEndpoint.Commands;
using VSEndpoint.ToolWindows;

namespace VSEndpoint
{
    /// <summary>
    /// VS Endpoint package - HTTP request execution and response viewing for .http/.rest files.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(VSEndpointPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(ResponseViewerToolWindow),
        Style = VsDockStyle.Tabbed,
        Window = "DocumentWell",
        Orientation = ToolWindowOrientation.Right)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class VSEndpointPackage : AsyncPackage
    {
        /// <summary>
        /// Package GUID string.
        /// </summary>
        public const string PackageGuidString = "9c637616-a5b0-40cd-ac31-c087c0260cac";

        static VSEndpointPackage()
        {
            Debug.WriteLine("[VSEndpoint] Static constructor - Package type loaded");
        }

        /// <summary>
        /// Initializes the package asynchronously.
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Debug.WriteLine("[VSEndpoint] InitializeAsync starting");
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Initialize commands
            await VSEndpointCommandHandler.InitializeAsync(this);
            Debug.WriteLine("[VSEndpoint] InitializeAsync completed");
        }

        /// <summary>
        /// Gets a VS service asynchronously with type safety.
        /// </summary>
        public async Task<TInterface> GetServiceAsync<TService, TInterface>()
            where TInterface : class
        {
            return await base.GetServiceAsync(typeof(TService)) as TInterface;
        }

        /// <summary>
        /// Gets a VS service synchronously (must be on UI thread).
        /// </summary>
        public TInterface GetService<TService, TInterface>()
            where TInterface : class
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetService(typeof(TService)) as TInterface;
        }
    }
}
