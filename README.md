# AutoAppInstaller

Automatically install apps from Play Store, RuStore, F-Droid and local folder on your Android smartphone using Appium

## How it works

The program uses Appium + UiAutomator2 driver and its dotnet-client to automate installation process.

**First**, it will install all local packages located in the 'local' folder within the working directory of this tool. File naming is important and should follow this format: `appLabel-version-packageName.apk`. Actually the main point is that the filename should be dash-separated string where the last part is the `packageName` and must end with `.apk`. Otherwise, the file will be ignored. Also, the program will not attempt to find local packages listed in the JSON array. If you want to install local packages, place them inside the `local` folder; you don't need to specify them in the JSON file. However, if you do specify them in the JSON file, this tool will only check if corresponding app is already installed on your system and increment a counter accordingly in the second part. Read next.  
**Second**, it will iterate over the given array of AppInfo objects and open the corresponding app page in the specified app store, find the 'Install' button using the `UiSelector` locator and click it.  
In case of the Google Play Store, that's all. The specified app will be downloaded and installed by the Play Store automatically. There's nothing to do anymore. However, in the case of the non-system app stores such as RuStore and Droid-ify (F-Droid), the Google Package Installer will prompt you each time to confirm the installation with a pop up window. This is the case for unrooted devices. Therefore, we need to wait for the download to finish first and then find the "Install" button again and click it. Listed stores anyway cannot download several apps simultaneously so you can leave your phone and have a cup of tea while installing apps. Required time depends on your app list, internet connection speed and smartphone performance obviously.

If your internet connection is very slow, you can increase timeout time when creating `AppInstaller` instance in the source code to make sure apps will be installed and not ignored because of timeout. Just pass the third parameter of the TimeSpan type to constructor. By default, the timeout is 1 minute. This means that if an download from, e.g., RuStore has not finished within 60 seconds, it will be ignored and not installed automatically. Also, it will break the process completely or partially.

> [!WARNING]
> Google is going to [deprecate](https://developer.android.com/training/testing/other-components/ui-automator#ui-automator)
> and remove `UiCollection`, `UiObject`, `UiScrollable`, and `UiSelector` support from the UiAutomator framework.
> This will render all `-android uiautomator`-based locators invalid, so please keep it in mind while
> using them or plan to use them in the future.

## How to use

1. Follow the [quickstart intro](https://appium.io/docs/en/latest/quickstart/) to install Appium server, driver and client
2. Create JSON file with an array of objects containing the following information about your apps: `label`, `packageName` and `source`, where `source` is an integer representing an enum value
3. Enable USB debugging on your phone and connect it to your PC. Check if it's available using `adb devices` command
4. Make sure that specified sources e.g. Play Store and RuStore are available and functional on your phone
5. Start the Appium server
6. Run this tool from the command line with your phone connected to PC

Display usage info

```powershell
AutoAppInstaller.exe
```

Install apps from JSON file

```powershell
AutoAppInstaller.exe listOfApps.json
```

Install apps from JSON file to the specific device defined by its id. It's useful when you have multiple devices connected to your PC because by default the program will use the first one from the `adb devices` list. 

```powershell
AutoAppInstaller.exe listOfApps.json 5a954c87
```

## Example of JSON file

```json
[
  {"label": "Anytype",   "packageName": "io.anytype.app",                          "source": 0},
  {"label": "Aqua Mail", "packageName": "org.kman.AquaMail",                       "source": 0},
  {"label": "Okay?",     "packageName": "de.stollenmayer.philipp.Pop_1_1_Android", "source": 0},
  {"label": "Mir Pay",   "packageName": "ru.nspk.mirpay",                          "source": 1},
  {"label": "Markor",    "packageName": "net.gsantner.markor",                     "source": 2},
  {"label": "Droid-ify", "packageName": "com.looker.droidify",                     "source": 3},
  {"label": "RuStore",   "packageName": "ru.vk.store",                             "source": 3}
]
```

`source` field represents an enum value where `0 = Google Play, 1 = RuStore, 2 = Droid-ify (F-Droid), 3 = Local`.
Any other values will be ignored.

## Useful links

- [Appium 2 Beginner Tutorials by Raghav Pal](https://www.youtube.com/playlist?list=PLhW3qG5bs-L8BQaqLpjt5792e8om6IR3k)
- [Guide on UiAutomator Locator Types](https://github.com/appium/appium-uiautomator2-driver/blob/master/docs/uiautomator-uiselector.md)
