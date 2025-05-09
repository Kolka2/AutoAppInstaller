using OpenQA.Selenium.Appium;
using System.Text.Json;

namespace AutoAppInstaller;

internal class Program
{
    private const string _usageInfo = """
        Usage: AutomateInstallWithAppium.exe FILE [DEVICE ID]
        Examples: 
                AutomateInstallWithAppium.exe D:/MyDevice/Backup/listOfApps.json
                AutomateInstallWithAppium.exe D:/MyDevice/Backup/listOfApps.json 5a954c87
        Note: Local APK filenames must include the package name of the corresponding app
        in the following format 'appLabel-version-packageName.apk' and be located
        in the 'local' folder within the working directory of this tool.
        """;
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(_usageInfo);
            return 0;
        }

        try
        {
            string json = File.ReadAllText(args[0]);
            AppInfo[] apps = JsonSerializer.Deserialize<AppInfo[]>(json, _serializerOptions)!;

            if (apps.Length == 0)
            {
                Console.WriteLine($"Provided JSON file '{args[0]}' contains an empty array. Nothing to do. Exiting...");
                return 0;
            }

            var serverUri = new Uri(Environment.GetEnvironmentVariable("APPIUM_HOST") ?? "http://127.0.0.1:4723/");
            var driverOptions = new AppiumOptions()
            {
                AutomationName = "UiAutomator2",
                PlatformName = "Android",
            };
            driverOptions.AddAdditionalAppiumOption("noReset", true);
            if (args.Length > 1)
                driverOptions.AddAdditionalAppiumOption("udid", args[1]);

            using var installer = new AppInstaller(serverUri, driverOptions);
            var (installed, total) = installer.InstallApps(apps);

            Console.WriteLine($"""
            Summary. 
            Installed
            Google Play apps: {installed[0]}/{total[0]}
            RuStore apps: {installed[1]}/{total[1]}
            F-Droid apps: {installed[2]}/{total[2]}
            Local apps: {installed[3]}/{total[3]}
            """);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }

        return 0;
    }
}