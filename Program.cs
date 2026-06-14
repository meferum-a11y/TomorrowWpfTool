using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace TomorrowWpfTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        bool force = args.Contains("--force") || args.Contains("-f");

        try
        {
            switch (command)
            {
                case "get-wpf":
                    GenerateWpfFiles(force);
                    return 0;

                case "get-db-script":
                    CopyDbScript(force);
                    return 0;

                case "get-1c":
                    CopyOneCFile(force);
                    return 0;

                default:
                    Console.WriteLine($"Неизвестная команда: {command}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Ошибка: " + ex.Message);
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
    tomorrow-wpf — генератор файлов для WPF-проекта TomorrowEkz

    Команды:
      tomorrow-wpf get-wpf                Скопировать WPF-окна, модели, контекст и изображения
      tomorrow-wpf get-wpf --force        Перезаписать существующие WPF-файлы

      tomorrow-wpf get-db-script          Скопировать файл "бд скрипты.txt"
      tomorrow-wpf get-db-script --force  Перезаписать существующий SQL-файл

      tomorrow-wpf get-1c                 Скопировать файл "1Cv8.cf"
      tomorrow-wpf get-1c --force         Перезаписать существующий файл "1Cv8.cf"

    Запускать команды нужно из папки WPF-проекта, где лежит .csproj.

    Очистка истории PowerShell:
      Set-PSReadLineOption -HistorySaveStyle SaveNothing
      Clear-History
      [Microsoft.PowerShell.PSConsoleReadLine]::ClearHistory()
      Remove-Item (Get-PSReadLineOption).HistorySavePath -Force -ErrorAction SilentlyContinue
      cls
    """);
    }

    private static void GenerateWpfFiles(bool force)
    {
        string projectDir = Directory.GetCurrentDirectory();
        string? csprojPath = Directory.GetFiles(projectDir, "*.csproj").FirstOrDefault();

        if (csprojPath == null)
        {
            throw new InvalidOperationException("В текущей папке не найден .csproj файл. Запусти команду из папки WPF-проекта.");
        }

        string rootNamespace = GetRootNamespace(csprojPath);

        using Stream zipStream = OpenEmbeddedResource("WpfTemplates.zip");
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        int created = 0;
        int skipped = 0;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            string outputPath = Path.GetFullPath(Path.Combine(projectDir, entry.FullName));
            string projectFullPath = Path.GetFullPath(projectDir);

            if (!outputPath.StartsWith(projectFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Недопустимый путь внутри архива шаблонов.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            if (File.Exists(outputPath) && !force)
            {
                skipped++;
                continue;
            }

            if (IsTextFile(outputPath))
            {
                using Stream input = entry.Open();
                using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string text = reader.ReadToEnd();

                text = ReplaceNamespace(text, rootNamespace);

                File.WriteAllText(outputPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            else
            {
                using Stream input = entry.Open();
                using FileStream output = File.Create(outputPath);
                input.CopyTo(output);
            }

            created++;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Готово. Создано/обновлено файлов: {created}. Пропущено: {skipped}.");
        Console.ResetColor();

        Console.WriteLine();
        Console.WriteLine("Проверь, что в WPF-проекте есть нужные NuGet-пакеты:");
        Console.WriteLine("Microsoft.EntityFrameworkCore.SqlServer");
        Console.WriteLine("Microsoft.EntityFrameworkCore.Tools");
        Console.WriteLine("Microsoft.EntityFrameworkCore.Design");
    }

    private static void CopyDbScript(bool force)
    {
        string projectDir = Directory.GetCurrentDirectory();
        string outputPath = Path.Combine(projectDir, "бд скрипты.txt");

        if (File.Exists(outputPath) && !force)
        {
            Console.WriteLine("Файл уже существует. Для перезаписи используй:");
            Console.WriteLine("tomorrow-wpf get-db-script --force");
            return;
        }

        using Stream input = OpenEmbeddedResource("db-scripts.txt");
        using FileStream output = File.Create(outputPath);
        input.CopyTo(output);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Файл \"бд скрипты.txt\" успешно скопирован в текущую папку.");
        Console.ResetColor();
    }

    private static string GetRootNamespace(string csprojPath)
    {
        XDocument document = XDocument.Load(csprojPath);

        string? rootNamespace = document
            .Descendants("RootNamespace")
            .FirstOrDefault()
            ?.Value;

        if (!string.IsNullOrWhiteSpace(rootNamespace))
            return rootNamespace;

        string projectName = Path.GetFileNameWithoutExtension(csprojPath);

        return projectName
            .Replace("-", "_")
            .Replace(" ", "_");
    }

    private static string ReplaceNamespace(string text, string rootNamespace)
    {
        return text
            .Replace("using demo.", $"using {rootNamespace}.")
            .Replace("namespace demo", $"namespace {rootNamespace}")
            .Replace("x:Class=\"demo.", $"x:Class=\"{rootNamespace}.")
            .Replace("x:Class=\"demo\"", $"x:Class=\"{rootNamespace}\"")
            .Replace("clr-namespace:demo", $"clr-namespace:{rootNamespace}");
    }

    private static bool IsTextFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        return extension is ".cs"
            or ".xaml"
            or ".xml"
            or ".config"
            or ".json"
            or ".txt";
    }
    private static void CopyOneCFile(bool force)
    {
        string projectDir = Directory.GetCurrentDirectory();
        string outputPath = Path.Combine(projectDir, "1Cv8.cf");

        if (File.Exists(outputPath) && !force)
        {
            Console.WriteLine("Файл уже существует. Для перезаписи используй:");
            Console.WriteLine("tomorrow-wpf get-1c --force");
            return;
        }

        using Stream input = OpenEmbeddedResource("1Cv8.cf");
        using FileStream output = File.Create(outputPath);
        input.CopyTo(output);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Файл \"1Cv8.cf\" успешно скопирован в текущую папку.");
        Console.ResetColor();
    }

    private static Stream OpenEmbeddedResource(string resourceEnding)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceEnding, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new FileNotFoundException($"Внутри пакета не найден ресурс: {resourceEnding}");
        }

        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Не удалось открыть ресурс: {resourceEnding}");
    }
}