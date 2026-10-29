using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace GymTracker.AndroidHead;

// Avalonia 12 wants the Application class declared here, not in MainActivity
[Application]
public class AndroidApp : AvaloniaAndroidApplication<GymTracker.Avalonia.App>
{
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
