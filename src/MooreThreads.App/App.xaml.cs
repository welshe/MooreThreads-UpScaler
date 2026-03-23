using System;
using Microsoft.Extensions.DependencyInjection;
using MooreThreadsUpScaler.Core.Profiles;
using MooreThreadsUpScaler.Core.Windowing;
using MooreThreadsUpScaler.ViewModels;

namespace MooreThreadsUpScaler
{
    public partial class App : System.Windows.Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            services.AddSingleton<WindowManager>();
            services.AddSingleton<ProfileManager>();
            services.AddSingleton<OptiScalerManager>();
            Services = services.BuildServiceProvider();
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            if (Services is IDisposable d) d.Dispose();
            base.OnExit(e);
        }
    }
}
