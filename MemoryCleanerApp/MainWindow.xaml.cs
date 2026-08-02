using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Bnscl;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x524D4301;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const string PluginDirectoryName = "LoaderU";
    private const string LegacyPluginDirectoryName = "plugins";
    private const string LoaderDownloadUrl = "https://neoqol.ru/download/plugins/winmm.dll";
    private const int LoaderExpectedSize = 118784;
    private const string LoaderExpectedSha256 = "87DE14E689945AD8ECCB14EE383C3DDB9C6D8C73495189F1B62FB5CAC24FBCBD";

    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BNSCL", "settings.json");
    private HwndSource? _source;
    private bool _capturing;
    private HotkeySettings _settings = new(ModAlt, (uint)KeyInterop.VirtualKeyFromKey(Key.C), "Alt+C");

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        SourceInitialized += (_, _) =>
        {
            WindowTheming.ApplyDark(this);
            RegisterConfiguredHotkey();
        };
        Closed += (_, _) => ReleaseHotkey();
    }

    private void RegisterConfiguredHotkey()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (_source is null)
        {
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WindowHook);
        }
        UnregisterHotKey(handle, HotkeyId);
        HotkeyText.Text = _settings.Display;
        if (!RegisterHotKey(handle, HotkeyId, _settings.Modifiers | ModNoRepeat, _settings.VirtualKey))
            StatusText.Text = $"Комбинация {_settings.Display} занята другой программой";
        else
            StatusText.Text = $"Готово · {_settings.Display}";
    }

    private void ReleaseHotkey()
    {
        if (_source is null) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WindowHook);
        _source = null;
    }

    private nint WindowHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            _ = CleanAsync();
            handled = true;
        }
        return 0;
    }

    private void AssignHotkeyClick(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        AssignButton.Content = "Нажмите…";
        StatusText.Text = "Нажмите сочетание с Ctrl, Alt, Shift или Win";
        Focus();
    }

    private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        uint modifiers = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= ModAlt;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= ModControl;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= ModShift;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) modifiers |= ModWin;
        if (modifiers == 0)
        {
            StatusText.Text = "Добавьте Ctrl, Alt, Shift или Win";
            return;
        }

        string display = BuildDisplay(modifiers, key);
        _settings = new HotkeySettings(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key), display);
        _capturing = false;
        AssignButton.Content = "Изменить";
        SaveSettings();
        RegisterConfiguredHotkey();
        e.Handled = true;
    }

    private static string BuildDisplay(uint modifiers, Key key)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private async void CleanClick(object sender, RoutedEventArgs e) => await CleanAsync();

    private async Task CleanAsync()
    {
        try
        {
            StatusText.Text = "Очистка…";
            using var pipe = new NamedPipeClientStream(".", "BNSCLCleaner", PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(timeout.Token);
            byte[] command = Encoding.UTF8.GetBytes("clean\n");
            await pipe.WriteAsync(command, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            string response = await reader.ReadLineAsync(timeout.Token) ?? string.Empty;
            string[] values = response.Split('|');
            if (values.Length >= 4 && values[0] == "OK" &&
                ulong.TryParse(values[1], out ulong before) && ulong.TryParse(values[2], out ulong after))
            {
                StatusText.Text = $"Готово: {ToMb(before):N0} → {ToMb(after):N0} МБ";
                return;
            }
            StatusText.Text = values.Length >= 4 ? $"Ошибка Windows: {values[3]}" : "Некорректный ответ плагина";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Плагин не отвечает. Запустите игру после установки.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Плагин недоступен: {exception.Message}";
        }
    }

    private static double ToMb(ulong bytes) => bytes / 1024d / 1024d;

    private async void InstallClick(object sender, RoutedEventArgs e)
    {
        if (Process.GetProcessesByName("BNSR").Any() || Process.GetProcessesByName("BNSR_unpacked").Any())
        {
            StatusText.Text = "Сначала закройте игру";
            return;
        }

        string? directory = FindGameDirectory();
        if (directory is null)
        {
            var dialog = new OpenFolderDialog { Title = "Выберите папку BNSR\\Binaries\\Win64" };
            if (dialog.ShowDialog(this) != true) return;
            directory = ResolveGameDirectory(dialog.FolderName);
        }
        if (directory is null)
        {
            StatusText.Text = "Не найдена папка с BNSR.exe";
            return;
        }

        try
        {
            string plugins = Path.Combine(directory, PluginDirectoryName);
            Directory.CreateDirectory(plugins);
            bool loaderDownloaded = await EnsureLoaderAsync(directory);
            WriteResource("bnscleaner.dll", Path.Combine(plugins, "bnscleaner.dll"));
            StatusText.Text = RemoveLegacyCleaner(directory)
                ? loaderDownloaded
                    ? "LoaderU скачан, плагин установлен"
                    : "Плагин установлен, существующий winmm.dll не изменён"
                : "Плагин установлен в LoaderU, но старую копию удалить не удалось";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText.Text = "Нет доступа. Запустите приложение от администратора.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Ошибка установки: {exception.Message}";
        }
    }

    private static string? FindGameDirectory()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
            foreach (string root in new[]
            {
                Path.Combine(drive.RootDirectory.FullName, "Games", "Blade and Soul NEO"),
                Path.Combine(drive.RootDirectory.FullName, "Blade and Soul NEO")
            })
            {
                string candidate = Path.Combine(root, "BNSR", "Binaries", "Win64");
                if (File.Exists(Path.Combine(candidate, "BNSR.exe"))) return candidate;
            }
        }
        return null;
    }

    private static string? ResolveGameDirectory(string path)
    {
        if (File.Exists(Path.Combine(path, "BNSR.exe"))) return path;
        foreach (string suffix in new[] { Path.Combine("BNSR", "Binaries", "Win64"), Path.Combine("Binaries", "Win64") })
        {
            string candidate = Path.Combine(path, suffix);
            if (File.Exists(Path.Combine(candidate, "BNSR.exe"))) return candidate;
        }
        return null;
    }

    private static void WriteResource(string name, string target)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Встроенный файл {name} не найден");
        if (File.Exists(target))
        {
            string backup = target + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(target, backup, false);
        }
        string temporary = target + ".tmp";
        using (FileStream output = File.Create(temporary)) stream.CopyTo(output);
        File.Move(temporary, target, true);
    }

    private async Task<bool> EnsureLoaderAsync(string gameDirectory)
    {
        string target = Path.Combine(gameDirectory, "winmm.dll");
        if (File.Exists(target)) return false;

        StatusText.Text = "LoaderU не найден — скачивание…";
        return await EnsureLoaderFileAsync(target);
    }

    private static async Task<bool> EnsureLoaderFileAsync(string target)
    {
        if (File.Exists(target)) return false;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        byte[] bytes = await client.GetByteArrayAsync(LoaderDownloadUrl);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (bytes.Length != LoaderExpectedSize || !hash.Equals(LoaderExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Скачанный LoaderU не прошёл проверку целостности");

        string temporary = target + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, target, false);
            return true;
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    private static bool RemoveLegacyCleaner(string gameDirectory)
    {
        string legacyDirectory = Path.Combine(gameDirectory, LegacyPluginDirectoryName);
        string legacyPlugin = Path.Combine(legacyDirectory, "bnscleaner.dll");
        if (!File.Exists(legacyPlugin)) return true;

        try
        {
            File.SetAttributes(legacyPlugin, FileAttributes.Normal);
            File.Delete(legacyPlugin);
            if (Directory.Exists(legacyDirectory) && !Directory.EnumerateFileSystemEntries(legacyDirectory).Any())
                Directory.Delete(legacyDirectory);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                _settings = JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(_settingsPath)) ?? _settings;
        }
        catch { }
        HotkeyText.Text = _settings.Display;
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings));
    }

    private sealed record HotkeySettings(uint Modifiers, uint VirtualKey, string Display);
}
