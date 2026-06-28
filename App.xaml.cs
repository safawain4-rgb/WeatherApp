using System;
using System.Net;
using System.Windows;

namespace weatherForecasting
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure TLS 1.2 is used for the weather API calls on older Windows/.NET
            // configurations where it might not be the default negotiated protocol.
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
                // SecurityProtocolType.Tls12 may not be defined on extremely old
                // runtimes; if so, just fall back to whatever the OS defaults to.
            }
        }
    }
}
