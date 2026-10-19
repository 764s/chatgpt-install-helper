using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace ChatGPTInstaller
{
    internal sealed class PowerShellBridge : IDisposable
    {
        private readonly Runspace runspace;
        private readonly Action<int, string> progress;

        public PowerShellBridge(Action<int, string> progress)
        {
            this.progress = progress;
            runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault());
            runspace.Open();
        }

        private Collection<PSObject> Run(string command, params object[] parameters)
        {
            using (var shell = PowerShell.Create())
            {
                shell.Runspace = runspace;
                shell.Streams.Progress.DataAdded += delegate(object sender, DataAddedEventArgs args)
                {
                    var record = shell.Streams.Progress[args.Index];
                    if (progress != null) progress(record.PercentComplete, record.StatusDescription);
                };
                shell.AddCommand(command);
                for (int i = 0; i < parameters.Length; i += 2) shell.AddParameter((string)parameters[i], parameters[i + 1]);
                shell.AddParameter("ErrorAction", "Stop");
                try
                {
                    var result = shell.Invoke();
                    if (shell.HadErrors) throw new InvalidOperationException(string.Join(Environment.NewLine, shell.Streams.Error.Select(e => e.ToString())));
                    return result;
                }
                catch (RuntimeException ex)
                {
                    throw new InvalidOperationException(ex.Message + Environment.NewLine +
                        string.Join(Environment.NewLine, shell.Streams.Error.Select(e => e.ToString())), ex);
                }
            }
        }

        private void Import(string module)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "Modules", module, module + ".psd1");
            Run("Import-Module", "Name", path);
        }

        public InstalledPackage FindInstalled()
        {
            Import("Appx");
            return Run("Get-AppxPackage", "Name", AppInfo.PackageName)
                .Where(p => string.Equals(Value(p, "PackageFamilyName"), AppInfo.FamilyName, StringComparison.OrdinalIgnoreCase))
                .Select(p => new InstalledPackage
                {
                    Version = Version.Parse(Value(p, "Version")), FullName = Value(p, "PackageFullName"), Location = Value(p, "InstallLocation")
                }).OrderByDescending(p => p.Version).FirstOrDefault();
        }

        private static string Value(PSObject item, string name)
        {
            var property = item.Properties[name];
            return property == null || property.Value == null ? "" : property.Value.ToString();
        }

        public void Provision(string package, string license)
        {
            Import("Dism");
            Run("Add-AppxProvisionedPackage", "Online", true, "PackagePath", package, "LicensePath", new[] { license }, "Regions", "all");
        }

        public void Add(string package, bool repair)
        {
            Import("Appx");
            if (repair)
                Run("Add-AppxPackage", "Path", package, "ForceUpdateFromAnyVersion", true, "ForceTargetApplicationShutdown", true);
            else
                Run("Add-AppxPackage", "Path", package, "ForceTargetApplicationShutdown", true);
        }

        public void Register(string fullName)
        {
            Import("Appx");
            Run("Add-AppxPackage", "Register", true, "MainPackage", fullName, "ForceTargetApplicationShutdown", true);
        }

        public void Dispose() { runspace.Dispose(); }
    }
}
