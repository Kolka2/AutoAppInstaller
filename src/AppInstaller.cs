using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;
using System.Text.RegularExpressions;

namespace AutoAppInstaller;
public record AppInfo(string Label, string PackageName, AppSourceOptions Source);
public record SourceInfo(string PackageName, string MainActivity, string Locator);
[Flags]
public enum AppSourceOptions 
{ 
    Local = 0,
    GooglePlay = 1,
    FDroid = 2,
    RuStore = 3,
    WorkProfile = 4
}
public static class AppSourceExtensions
{
    public static bool IsWorkProfile(this AppSourceOptions source) 
        => source.HasFlag(AppSourceOptions.WorkProfile);

    public static AppSourceOptions GetSourceType(this AppSourceOptions source) 
        => source & ~AppSourceOptions.WorkProfile;
}
public sealed class AppInstaller : IDisposable
{
    private Dictionary<string, string>? _localApkCache;
    private readonly AndroidDriver _driver;
    private readonly SourceInfo _googlePlay;
    private readonly SourceInfo _ruStore;
    private readonly SourceInfo _droidify;
    private readonly TimeSpan _waiterTimeout;

    public AppInstaller(Uri serverUri, AppiumOptions driverOptions, TimeSpan? waiterTimeout = null)
    {
        _driver = new(serverUri, driverOptions, TimeSpan.FromSeconds(180));
        _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
        _waiterTimeout = waiterTimeout ?? TimeSpan.FromSeconds(60);

        _googlePlay = new(
            PackageName: "com.android.vending",
            MainActivity: ".AssetBrowserActivity",
            Locator: "new UiSelector().className(\"android.widget.Button\").instance(1)"
        );
        _ruStore = new(
            PackageName: "ru.vk.store",
            MainActivity: ".app.MainActivity",
            // For old version of RuStore (v1.82)
            // Locator: "new UiSelector().className(\"android.widget.Button\").instance(0)"
            Locator: "new UiSelector().className(\"android.widget.Button\").instance(6)"
        );
        _droidify = new(
            // PackageName: "org.fdroid.fdroid",
            PackageName: "com.looker.droidify",
            // MainActivity: "org.fdroid.fdroid.views.main.MainActivity",
            MainActivity: ".MainActivity",
            // Locator: "new UiSelector().resourceId(\"org.fdroid.fdroid:id/primaryButtonView\")"
            Locator: "new UiSelector().resourceId(\"com.looker.droidify:id/action\")"
        );
    }

    public (int[] installed, int[] total) InstallApps(IEnumerable<AppInfo> apps)
    {
        int[] installed = new int[8]; 
        int[] total = new int[8];

        int? workProfileId = GetWorkProfileId();
        FillLocalApkCache();

        foreach (var app in apps)
        {
            bool isWorkProfile = app.Source.IsWorkProfile();
            if (workProfileId is null)
            {
                Logger.Log($"[WARNING] Skip {app.PackageName}: Work profile not found.");
                continue;
            }
            int userId = isWorkProfile ? workProfileId.Value : 0;

            if (IsAppInstalledForUser(app.PackageName, userId))
            {
                Logger.Log($"[INFO] Skip {app.PackageName}: The app already installed for user with id {userId}");
                continue;
            }
            
            AppSourceOptions sourceType = app.Source.GetSourceType();

            bool isSuccess = sourceType switch
            {
                AppSourceOptions.Local      => InstallLocal(app, userId),
                AppSourceOptions.GooglePlay => InstallFromStore(_googlePlay, app, userId, false),
                AppSourceOptions.FDroid     => InstallFromStore(_droidify, app, userId),
                AppSourceOptions.RuStore    => InstallFromStore(_ruStore, app, userId),
                _ => throw new NotSupportedException($"Unknown source: {sourceType}")
            };

            int index = (int)app.Source; 
            
            if (isSuccess)
                installed[index]++;
            
            total[index]++;
        }

        return (installed, total);
    }

    /* Tested on Sony Xperia 5 III with Android 13 */
    private bool InstallFromStore(SourceInfo source, AppInfo app, int userId, bool interactivePackageInstaller = true)
    {
        try
        {
            var args = new Dictionary<string, object>
            {
                ["package"] = $"{source.PackageName}",
                ["action"] = "android.intent.action.VIEW",
                ["uri"] = $"market://details?id={app.PackageName}",
                ["stop"] = false,
                ["user"] = userId,
                ["wait"] = true
            };
            _driver.ExecuteScript("mobile:startActivity", args);

            // NOTE: Temporary solution?
            /* Google Play opens an app card (not a full screen). On cold start
             * the card may not finish drawing and the installation is skipped. */
            var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(20));

            var installButton = wait.Until(d => _driver.FindElement(MobileBy.AndroidUIAutomator(source.Locator)));

            installButton.Click();
            /* When installing an app from a non-system app, such as RuStore, F-Droid etc.,
             * the google package installer will prompt you each time to confirm the installation
             * with a pop up window. Unless you install an app from the Play Store or have a rooted
             * device, you cannot escape this window if you want to install an app from your smartphone.
             * Therefore, we need to wait for the download to finish and then find the "Install"
             * button and click it. */
            if (interactivePackageInstaller)
            {
                var lWait = new DefaultWait<AndroidDriver>(_driver)
                {
                    Timeout = _waiterTimeout,
                    PollingInterval = TimeSpan.FromSeconds(1),
                    Message = $"The '{app.Label}' app from source '{app.Source}' was not installed. Reason: timed out."
                };
                if (lWait.Until(driver => driver.CurrentActivity.Contains("packageinstaller")))
                {
                    /* We got there! The APK downloaded, and PackageInstaller pops up its window.
                     * button2: Cancel; button1: Install */
                    installButton = _driver.FindElement(MobileBy.Id("android:id/button1"));

                    /* Android 7.1.1 device:
                     * ["ok_button", "cancel_button"] */
                    // installButton = _driver.FindElement(MobileBy.Id("com.android.packageinstaller:id/ok_button"));
                    installButton.Click();
                }
                else
                {
                    /* Timed out */
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ERROR] Ooops... Something went wrong for app '{app.Label}'. Read the exception message below for details.");
            Logger.Log(ex.Message);
            return false;
        }
    }

    /* Filenames must contain packagenames of the corresponding apps in the
     * following format: appLabel-version-packageName.apk
     * Example: Droidify-v0.7.1-com.looker.droidify.apk
     * Main point is that packagename is located after the last dash symbol.
     * Also note that this method will not replace any already installed app. */
    private bool InstallLocal(AppInfo app, int userId)
    {
        if (_localApkCache == null || !_localApkCache.TryGetValue(app.PackageName, out string? apkPath))
        {
            Logger.Log($"[INFO] Skip: file for {app.PackageName} not found in 'local' folder.");
            return false;
        }

        if (IsAppInstalledForUser(app.PackageName, userId)) 
            return true;

        string fileName = Path.GetFileName(apkPath);
        string deviceTempPath = $"/data/local/tmp/{fileName}";

        try
        {
            Logger.Log($"[DEBUG] Pushing {fileName} to {deviceTempPath}...");
            _driver.PushFile(deviceTempPath, new FileInfo(apkPath));

            var args = new Dictionary<string, object>
            {
                ["command"] = "pm",
                ["args"] = new List<string> { "install", "--user", userId.ToString(), deviceTempPath }
            };
            
            _driver.ExecuteScript("mobile: shell", args);
            Logger.Log($"[INFO] Successfully installed local package {app.PackageName} for user {userId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ERROR] Couldn't install local package {app.PackageName} for user {userId}: {ex.Message}");
            return false;
        }
        finally
        {
            try 
            {
                _driver.ExecuteScript("mobile: shell", new Dictionary<string, object> 
                { 
                    ["command"] = "rm", 
                    ["args"] = new List<string> { "-f", deviceTempPath }
                });
            }
            catch { }
        }
    }
    private void FillLocalApkCache()
    {
        if (_localApkCache != null) 
            return;

        _localApkCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(Environment.CurrentDirectory, "local");

        if (!Directory.Exists(path))
        {
            Logger.Log("[WARNING] The 'local' folder is not present in the current working directory. No local packages will be installed.");
            return;
        }

        var directoryInfo = new DirectoryInfo(path);
        foreach (var apk in directoryInfo.EnumerateFiles("*.apk"))
        {
            int dashIndex = apk.Name.LastIndexOf('-');
            if (dashIndex < 0)
            {
                Logger.Log($"[WARNING] Wrong file naming for the file {apk.Name}. Skipping...");
                continue;
            }

            string packageName = apk.Name.Substring(dashIndex + 1).Replace(".apk", "");
            
            if (!_localApkCache.ContainsKey(packageName))
            {
                _localApkCache.Add(packageName, apk.FullName);
            }
        }
    }
    public int? GetWorkProfileId()
    {
        var args = new Dictionary<string, object>
        {
            ["command"] = "pm",
            ["args"] = new List<string> { "list", "users" }
        };

        var output = _driver.ExecuteScript("mobile: shell", args)?.ToString();
        
        var match = Regex.Match(output ?? "", @"UserInfo\{(\d+):Work\b");

        if (match.Success)
            return int.Parse(match.Groups[1].Value);

        return null;
    }

    public bool IsAppInstalledForUser(string packageName, int userId)
    {
        var args = new Dictionary<string, object>
        {
            ["command"] = "dumpsys",
            ["args"] = new List<string>() { "package", packageName }
        };

        var output = _driver.ExecuteScript("mobile: shell", args)?.ToString();

        if (string.IsNullOrEmpty(output)) 
            return false;

        string pattern = $@"^\s+User\s+{userId}:.*installed=true";
        var match = Regex.Match(output, pattern, RegexOptions.Multiline);

        return match.Success;
    }
    public void Dispose() => _driver?.Quit();
}