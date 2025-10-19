using System.Runtime.CompilerServices;
using VerifyTests;

namespace EntityFrameworkCore.Projectables.Generator.Tests;

public static class VerifySettings
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.UniqueForTargetFrameworkAndVersion();
        
        // Uncomment the following line to enable automatic verification updates
        // VerifierSettings.AutoVerify();
    }
}