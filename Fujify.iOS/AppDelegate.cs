using Foundation;
using UIKit;
using Avalonia;
using Avalonia.iOS;

namespace Fujify.iOS;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
