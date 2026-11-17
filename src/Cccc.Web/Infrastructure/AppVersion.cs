using System.Reflection;

namespace Cccc.Web.Infrastructure;

/// <summary>The running build's version, read off the assembly so there is no second literal to forget.</summary>
public static class AppVersion
{
    public static string Current { get; } =
        (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? "0.0.0").Split('+')[0];
}
