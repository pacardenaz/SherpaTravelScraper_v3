using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;

namespace SherpaTravelScraper.Utils;

/// <summary>
/// Configuración de stealth para evitar detección de bots
/// </summary>
public class StealthConfig
{
    private readonly IConfiguration _configuration;
    private readonly Random _random = new();
    private int _userAgentIndex = 0;

    public StealthConfig(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Obtiene un User-Agent aleatorio de la lista configurada
    /// </summary>
    public string GetRandomUserAgent()
    {
        var userAgents = _configuration.GetSection("UserAgents").Get<string[]>() ?? 
            new[] { "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" };
        
        return userAgents[_random.Next(userAgents.Length)];
    }

    /// <summary>
    /// Obtiene el siguiente User-Agent en rotación secuencial
    /// </summary>
    public string GetNextUserAgent()
    {
        var userAgents = _configuration.GetSection("UserAgents").Get<string[]>() ?? 
            new[] { "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" };
        
        var agent = userAgents[_userAgentIndex % userAgents.Length];
        _userAgentIndex++;
        return agent;
    }

    /// <summary>
    /// Configura el browser para modo stealth
    /// </summary>
    public BrowserNewContextOptions GetStealthContextOptions(string? userAgent = null)
    {
        var agent = userAgent ?? GetRandomUserAgent();
        
        return new BrowserNewContextOptions
        {
            UserAgent = agent,
            Locale = "en-US",
            TimezoneId = "America/New_York",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            ScreenSize = new ScreenSize { Width = 1920, Height = 1080 },
            DeviceScaleFactor = 1,
            IsMobile = false,
            HasTouch = false,
            BypassCSP = true,
            JavaScriptEnabled = true,
        };
    }

    /// <summary>
/// Obtiene un delay aleatorio entre los segundos configurados
    /// </summary>
    public int GetRandomDelayMs()
    {
        var minSeconds = _configuration.GetValue<int>("Scraping:DelayMinSegundos", 3);
        var maxSeconds = _configuration.GetValue<int>("Scraping:DelayMaxSegundos", 8);
        
        // Distribución no uniforme (más probabilidad en valores medios)
        var random = new Random();
        var baseDelay = minSeconds + (maxSeconds - minSeconds) * random.NextDouble();
        var jitter = (random.NextDouble() - 0.5) * 1.5; // ±0.75s de variación
        var totalSeconds = Math.Clamp(baseDelay + jitter, minSeconds, maxSeconds);
        
        return (int)(totalSeconds * 1000);
    }

    /// <summary>
    /// Scripts de stealth para ejecutar en cada página
    /// </summary>
    public static string[] StealthScripts = new[]
    {
        // Ocultar webdriver
        "Object.defineProperty(navigator, 'webdriver', { get: () => undefined })",
        
        // Ocultar plugins
        "Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] })",
        
        // Ocultar languages
        "Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] })",
        
        // Ocultar permission
        "const originalQuery = window.navigator.permissions.query; window.navigator.permissions.query = (parameters) => (parameters.name === 'notifications' ? Promise.resolve({ state: Notification.permission }) : originalQuery(parameters))",
        
        // Ocultar Chrome runtime
        "window.chrome = { runtime: {} }",
        
        // Ocultar Chrome loadTimes
        """
        Object.defineProperty(window, 'chrome', {
            value: {
                app: {
                    isInstalled: false,
                    InstallState: { DISABLED: 'disabled', INSTALLED: 'installed', NOT_INSTALLED: 'not_installed' },
                    RunningState: { CANNOT_RUN: 'cannot_run', READY_TO_RUN: 'ready_to_run', RUNNING: 'running' }
                },
                runtime: {
                    OnInstalledReason: { CHROME_UPDATE: 'chrome_update', INSTALL: 'install', SHARED_MODULE_UPDATE: 'shared_module_update', UPDATE: 'update' },
                    OnRestartRequiredReason: { APP_UPDATE: 'app_update', OS_UPDATE: 'os_update', PERIODIC: 'periodic' },
                    PlatformArch: { ARM: 'arm', ARM64: 'arm64', MIPS: 'mips', MIPS64: 'mips64', MIPS64EL: 'mips64el', X86_32: 'x86-32', X86_64: 'x86-64' },
                    PlatformNaclArch: { MIPS: 'mips', MIPS64: 'mips64', MIPS64EL: 'mips64el', X86_32: 'x86-32', X86_64: 'x86-64' },
                    PlatformOs: { ANDROID: 'android', CROS: 'cros', LINUX: 'linux', MAC: 'mac', OPENBSD: 'openbsd', WIN: 'win' },
                    RequestUpdateCheckStatus: { NO_UPDATE: 'no_update', THROTTLED: 'throttled', UPDATE_AVAILABLE: 'update_available' }
                }
            }
        })
""",
    };
}
