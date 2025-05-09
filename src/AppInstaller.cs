using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Support.UI;

namespace AutoAppInstaller;
public record AppInfo(string Label, string PackageName, AppSourceOptions Source);
public record SourceInfo(string PackageName, string MainActivity, string Locator);
public enum AppSourceOptions { GooglePlay, RuStore, Droidify, Local }
public sealed class AppInstaller : IDisposable
{
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
            Locator: "new UiSelector().className(\"android.widget.Button\").instance(0)"
        );
        _ruStore = new(
            PackageName: "ru.vk.store",
            MainActivity: ".app.MainActivity",
            Locator: "new UiSelector().className(\"android.widget.Button\").instance(0)"
        );
        _droidify = new(
            PackageName: "com.looker.droidify",
            MainActivity: ".MainActivity",
            Locator: "new UiSelector().resourceId(\"com.looker.droidify:id/action\")"
        );
    }

    public (int[] installed, int[] total) InstallApps(IEnumerable<AppInfo> apps)
    {
        int[] installed = new int[4], total = new int[4];

        InstallLocal();
        foreach (var app in apps)
        {
            bool isSuccess = app.Source switch
            {
                AppSourceOptions.GooglePlay => InstallFromStore(_googlePlay, app, false),
                AppSourceOptions.RuStore => InstallFromStore(_ruStore, app),
                AppSourceOptions.Droidify => InstallFromStore(_droidify, app),
                AppSourceOptions.Local => _driver.IsAppInstalled(app.PackageName),
                _ => throw new NotSupportedException($"Unknown source: {app.Source}")
            };

            int index = (int)app.Source;
            if (isSuccess) installed[index]++;
            total[index]++;
        }

        return (installed, total);
    }

    /* Tested on Sony Xperia 5 III with Android 13 */
    private bool InstallFromStore(SourceInfo source, AppInfo app, bool interactivePackageInstaller = true)
    {
        try
        {
            _driver.StartActivityWithIntent(
                appPackage: source.PackageName,
                appActivity: source.MainActivity,
                intentAction: "android.intent.action.VIEW",
                intentOptionalArgs: $"-d \"market://details?id={app.PackageName}\"",
                stopApp: false
                );

            var installButton = _driver.FindElement(MobileBy.AndroidUIAutomator(source.Locator));
            installButton.Click();
            /* When installing an app from a non-system app, such as RuStore, F-Droid etc.,
             * the google package installer will prompt you each time to confirm the installation
             * with a pop up window. Unless you install an app from the Play Store or have a rooted
             * device, you cannot escape this window if you want to install an app from your smartphone.
             * Therefore, we need to wait for the download to finish and then find the "Install"
             * button and click it. */
            if (interactivePackageInstaller)
            {
                var wait = new DefaultWait<AndroidDriver>(_driver)
                {
                    Timeout = _waiterTimeout,
                    PollingInterval = TimeSpan.FromSeconds(1),
                    Message = $"The '{app.Label}' app from source '{app.Source}' was not installed. Reason: timed out."
                };
                if (wait.Until(driver => driver.CurrentActivity.Contains("packageinstaller")))
                {
                    /* We got there! The APK downloaded, and PackageInstaller pops up its window.
                     * button0: Cancel; button1: Install */
                    installButton = _driver.FindElement(MobileBy.Id("android:id/button1"));
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
            Console.WriteLine($"Ooops... Something went wrong for app '{app.Label}'. Read the exception message below for details.");
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    /* Filenames must contain packagenames of the corresponding apps in the
     * following format: appLabel-version-packageName.apk
     * Example: RuStore-v1.68.1.0-ru.vk.store.apk
     * Main point is that packagename is located after the last dash symbol.
     * Also note that this method will not replace any already installed app. */
    private void InstallLocal()
    {
        string path = Path.Combine(Environment.CurrentDirectory, "local");
        if (!Directory.Exists(path))
        {
            Console.WriteLine("The 'local' folder is not present in the current working directory. No local packages will be installed.");
            return;
        }

        var directoryInfo = new DirectoryInfo(path);
        foreach (var apk in directoryInfo.EnumerateFiles("*.apk"))
        {
            int packageNameStartPosition = apk.Name.LastIndexOf('-');
            if (packageNameStartPosition < 0)
            {
                Console.WriteLine("Wrong file naming for the file {0}. Skipping...", apk.Name);
                continue;
            }
            string packageName = apk.Name.Substring(packageNameStartPosition + 1);
            if (_driver.IsAppInstalled(packageName))
                continue;
            _driver.InstallApp(apk.FullName);
        }
    }
    public void Dispose() => _driver?.Quit();
}
