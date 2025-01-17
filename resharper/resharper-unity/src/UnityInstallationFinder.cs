using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using JetBrains.Annotations;
using JetBrains.Diagnostics;
using JetBrains.Util;
using JetBrains.Util.Interop;
using JetBrains.Util.Logging;

namespace JetBrains.ReSharper.Plugins.Unity
{
    public static class UnityInstallationFinder
    {
        private static readonly ILogger ourLogger = Logger.GetLogger(typeof(UnityInstallationFinder));

        [CanBeNull]
        public static UnityInstallationInfo GetApplicationInfo(Version version)
        {
            var possible = GetPossibleInstallationInfos().ToArray();
            var possibleWithVersion = possible.Where(a => a.Version != null).ToArray();

            var bestChoice = possibleWithVersion.Where(a =>
                a.Version.Major == version.Major && a.Version.Minor == version.Minor &&
                a.Version.Build == version.Build && a.Version.Revision == version.Revision
                ).OrderBy(b=>b.Version).LastOrDefault();
            if (bestChoice != null)
                return bestChoice;
            var choice1 = possibleWithVersion.Where(a =>
                a.Version.Major == version.Major && a.Version.Minor == version.Minor &&
                a.Version.Build == version.Build).OrderBy(b=>b.Version).LastOrDefault();
            if (choice1 != null)
                return choice1;
            var choice2 = possibleWithVersion.Where(a =>
                a.Version.Major == version.Major && a.Version.Minor == version.Minor).OrderBy(b=>b.Version).LastOrDefault();
            if (choice2 != null)
                return choice2;
            var choice3 =  possibleWithVersion.Where(a => a.Version.Major == version.Major)
                .OrderBy(b=>b.Version).LastOrDefault();
            if (choice3!=null)
                return choice3;
            var choice4 =  possibleWithVersion
                .OrderBy(b=>b.Version).LastOrDefault();
            if (choice4!=null)
                return choice4;

            var worstChoice = possible.LastOrDefault();
            return worstChoice;
        }

        [NotNull]
        public static FileSystemPath GetApplicationContentsPath(FileSystemPath applicationPath)
        {
            if (applicationPath.IsEmpty)
                return applicationPath;

            AssertApplicationPath(applicationPath);

            switch (PlatformUtil.RuntimePlatform)
            {
                    case PlatformUtil.Platform.MacOsX:
                        return applicationPath.Combine("Contents");
                    case PlatformUtil.Platform.Linux:
                    case PlatformUtil.Platform.Windows:
                        return applicationPath.Directory.Combine("Data");
            }
            ourLogger.Error("Unknown runtime platform");
            return FileSystemPath.Empty;
        }

        private static List<UnityInstallationInfo> GetPossibleInstallationInfos()
        {
            var installations = GetPossibleApplicationPaths();
            return installations.Select(path =>
            {
                var version = UnityVersion.GetVersionByAppPath(path);
                return new UnityInstallationInfo(version, path);
            }).ToList();
        }

        public static List<FileSystemPath> GetPossibleApplicationPaths()
        {
            switch (PlatformUtil.RuntimePlatform)
            {
                case PlatformUtil.Platform.MacOsX:
                {
                    var appsHome = FileSystemPath.Parse("/Applications");
                    var unityApps = appsHome.GetChildDirectories("Unity*").Select(a=>a.Combine("Unity.app")).ToList();

                    var defaultHubLocation = appsHome.Combine("Unity/Hub/Editor");
                    var hubLocations = new List<FileSystemPath> {defaultHubLocation};

                    // Hub custom location
                    var appData = FileSystemPath.Parse(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                    var hubCustomLocation = GetCustomHubInstallPath(appData);
                    if (!hubCustomLocation.IsEmpty)
                        hubLocations.Add(hubCustomLocation);

                    // /Applications/Unity/Hub/Editor/2018.1.0b4/Unity.app
                    unityApps.AddRange(hubLocations.SelectMany(l=>l.GetChildDirectories().Select(unityDir =>
                        unityDir.Combine(@"Unity.app"))));

                    return unityApps.Where(a=>a.ExistsDirectory).Distinct().OrderBy(b=>b.FullPath).ToList();
                }
                case PlatformUtil.Platform.Linux:
                {
                    var unityApps = new List<FileSystemPath>();
                    var homeEnv = Environment.GetEnvironmentVariable("HOME");
                    var homes = new List<FileSystemPath> {FileSystemPath.Parse("/opt")};

                    unityApps.AddRange(
                        homes.SelectMany(a => a.GetChildDirectories("Unity*"))
                            .Select(unityDir => unityDir.Combine(@"Editor/Unity")));

                    if (homeEnv == null)
                        return unityApps;
                    var home = FileSystemPath.Parse(homeEnv);
                    homes.Add(home);

                    var defaultHubLocation = home.Combine("Unity/Hub/Editor");
                    var hubLocations = new List<FileSystemPath> {defaultHubLocation};
                    // Hub custom location
                    var configPath = home.Combine(".config");
                    var customHubInstallPath = GetCustomHubInstallPath(configPath);
                    if (!customHubInstallPath.IsEmpty)
                        hubLocations.Add(customHubInstallPath);

                    unityApps.AddRange(hubLocations.SelectMany(l=>l.GetChildDirectories().Select(unityDir =>
                        unityDir.Combine(@"Editor/Unity"))));

                    return unityApps.Where(a=>a.ExistsFile).Distinct().OrderBy(b=>b.FullPath).ToList();
                }

                case PlatformUtil.Platform.Windows:
                {
                    var unityApps = new List<FileSystemPath>();

                    var programFiles = GetProgramFiles();
                    unityApps.AddRange(
                        programFiles.GetChildDirectories("Unity*")
                            .Select(unityDir => unityDir.Combine(@"Editor\Unity.exe"))
                        );

                    // default Hub location
                    //"C:\Program Files\Unity\Hub\Editor\2018.1.0b4\Editor\Data\MonoBleedingEdge"
                    unityApps.AddRange(
                        programFiles.Combine(@"Unity\Hub\Editor").GetChildDirectories()
                            .Select(unityDir => unityDir.Combine(@"Editor\Unity.exe"))
                    );

                    // custom Hub location
                    var appData = FileSystemPath.Parse(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                    var customHubInstallPath = GetCustomHubInstallPath(appData);
                    if (!customHubInstallPath.IsEmpty)
                    {
                        unityApps.AddRange(
                            customHubInstallPath.GetChildDirectories()
                                .Select(unityDir => unityDir.Combine(@"Editor\Unity.exe"))
                        );
                    }

                    var lnks = FileSystemPath.Parse(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs")
                        .GetChildDirectories("Unity*").SelectMany(a => a.GetChildFiles("Unity.lnk")).ToArray();
                    unityApps.AddRange(lnks
                        .Select(ShellLinkHelper.ResolveLinkTarget)
                        .OrderBy(c => new FileInfo(c.FullPath).CreationTime));

                    foreach (var fileSystemPath in unityApps)
                    {
                        ourLogger.Log(LoggingLevel.VERBOSE, "Possible unity path: " + fileSystemPath);
                    }
                    return unityApps.Where(a=>a.ExistsFile).Distinct().OrderBy(b=>b.FullPath).ToList();
                }
            }
            ourLogger.Error("Unknown runtime platform");
            return new List<FileSystemPath>();
        }

        private static FileSystemPath GetCustomHubInstallPath(FileSystemPath appData)
        {
            var filePath = appData.Combine("UnityHub/secondaryInstallPath.json");
            if (filePath.ExistsFile)
            {
                var text = filePath.ReadAllText2().Text.TrimStart('"').TrimEnd('"');
                var customHubLocation = FileSystemPath.Parse(text);
                if (customHubLocation.ExistsDirectory)
                    return customHubLocation;
            }
            return FileSystemPath.Empty;
        }

        private static FileSystemPath GetProgramFiles()
        {
            // PlatformUtils.GetProgramFiles() will return the relevant folder for
            // the current app, not the current system. So a 32 bit app on a 64 bit
            // system will return the 32 bit Program Files. Force to get the system
            // native Program Files folder
            var environmentVariable = Environment.GetEnvironmentVariable("ProgramW6432");
            return string.IsNullOrWhiteSpace(environmentVariable) ? FileSystemPath.Empty : FileSystemPath.TryParse(environmentVariable);
        }

        [CanBeNull]
        public static FileSystemPath GetAppPathByDll(XmlElement documentElement)
        {
            var referencePathElement = documentElement.ChildElements()
                .Where(a => a.Name == "ItemGroup").SelectMany(b => b.ChildElements())
                .Where(c => c.Name == "Reference" && c.GetAttribute("Include").StartsWith("UnityEngine") || c.GetAttribute("Include").Equals("UnityEditor"))
                .SelectMany(d => d.ChildElements())
                .FirstOrDefault(c => c.Name == "HintPath");

            if (referencePathElement == null || string.IsNullOrEmpty(referencePathElement.InnerText))
                return null;

            var filePath = FileSystemPath.Parse(referencePathElement.InnerText);
            if (!filePath.IsAbsolute) // RIDER-21237
                return null;

            if (filePath.ExistsFile)
            {
                if (PlatformUtil.RuntimePlatform == PlatformUtil.Platform.Windows)
                {
                    var exePath = filePath.Combine("../../../Unity.exe"); // Editor\Data\Managed\UnityEngine.dll
                    if (!exePath.ExistsFile)
                        exePath = filePath.Combine("../../../../Unity.exe"); // Editor\Data\Managed\UnityEngine\UnityEngine.dll
                    if (exePath.ExistsFile)
                        return exePath;
                }
                else if (PlatformUtil.RuntimePlatform == PlatformUtil.Platform.MacOsX)
                {
                    var appPath = filePath;
                    while (!appPath.Name.Equals("Contents"))
                    {
                        appPath = appPath.Directory;
                        if (!appPath.ExistsDirectory || appPath.IsEmpty)
                            return null;
                    }

                    appPath = appPath.Directory;
                    return appPath;
                }
            }

            return null;
        }

        // Asserts the given path is to the application itself, either Whatever.app/ for Mac or Whatever.exe for Windows
        [Conditional("JET_MODE_ASSERT")]
        private static void AssertApplicationPath(FileSystemPath path)
        {
            if (path.IsEmpty || path.Exists == FileSystemPath.Existence.Missing)
                return;

            if (PlatformUtil.RuntimePlatform == PlatformUtil.Platform.MacOsX)
            {
                Assertion.Assert(path.ExistsDirectory, "path.ExistsDirectory");
                Assertion.Assert(path.FullPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase),
                    "path.FullPath.EndsWith('.app', StringComparison.OrdinalIgnoreCase)");
            }
            else
            {
                Assertion.Assert(path.ExistsFile, "path.ExistsFile");
                Assertion.Assert(path.FullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
                    "path.FullPath.EndsWith('.exe', StringComparison.OrdinalIgnoreCase)");
            }
        }
    }

    public class UnityInstallationInfo
    {
        public Version Version { get; }
        public FileSystemPath Path { get; }

        public UnityInstallationInfo(Version version, FileSystemPath path)
        {
            Version = version;
            Path = path;
        }
    }
}