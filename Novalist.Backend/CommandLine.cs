namespace Novalist.Backend;

/// <summary>What a command line asked for, once it has been read.</summary>
/// <param name="Serve">
/// True when nothing was asked for - the JSON-RPC server, which is what the app
/// starts and what every existing caller gets.
/// </param>
public sealed record CommandRequest(
    bool Serve,
    string? ProjectPath = null,
    string? Format = null,
    string? OutputPath = null,
    string? Book = null,
    bool Help = false,
    string? Error = null);

/// <summary>
/// The backend as a tool as well as a server.
///
/// Program.cs parsed no arguments at all, so an export could only be produced
/// by a person clicking through a save dialog. Nothing outside the app could
/// build an EPUB on a commit, or a spreadsheet of the outline on a schedule -
/// which is the whole of what pandoc and calibre give away from the command
/// line.
///
/// Argument parsing is separated from doing the work so it can be tested
/// without spawning anything: the entry point stays a shim.
/// </summary>
public static class CommandLine
{
    public const string Usage = """
        Novalist backend

          novalist-backend                      Run the JSON-RPC server (what the app starts).
          novalist-backend --export <format> --project <dir> --out <file> [--book <name>]
                                                Write an export without opening the app.
          novalist-backend --help

        Formats: Epub, Docx, Pdf, Markdown, FinalDraft, LaTeX, Codex, CodexPdf,
                 Csv, Json, CodexCsv, Opml, SynopsisReport, PovReport
        """;

    /// <summary>
    /// Reads a command line. Anything unrecognised is an error rather than a
    /// silent fall back to serving: a typo in a scheduled job should fail
    /// loudly, not quietly start a server that nothing is talking to.
    /// </summary>
    public static CommandRequest Parse(string[] args)
    {
        if (args.Length == 0) return new CommandRequest(Serve: true);

        string? project = null, format = null, output = null, book = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h") return new CommandRequest(false, Help: true);

            string? Value()
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) return args[++i];
                return null;
            }

            switch (arg)
            {
                case "--project": project = Value(); break;
                case "--export": format = Value(); break;
                case "--out": output = Value(); break;
                case "--book": book = Value(); break;
                default:
                    return new CommandRequest(false, Error: $"Unknown argument '{arg}'.");
            }

            // A flag given without its value, which otherwise silently becomes
            // "nothing was asked for" further down.
            if (arg is "--project" or "--export" or "--out" or "--book"
                && (arg switch
                {
                    "--project" => project,
                    "--export" => format,
                    "--out" => output,
                    _ => book
                }) is null)
            {
                return new CommandRequest(false, Error: $"'{arg}' needs a value.");
            }
        }

        if (format is null) return new CommandRequest(false, Error: "--export needs a format.");
        if (project is null) return new CommandRequest(false, Error: "--export needs --project.");
        if (output is null) return new CommandRequest(false, Error: "--export needs --out.");

        return new CommandRequest(false, project, format, output, book);
    }

    /// <summary>
    /// Runs an export the way the Export view would, and returns a process exit
    /// code: zero for a file written, non-zero for anything else.
    /// </summary>
    /// <param name="existing">
    /// A workspace to use rather than making one. The entry point passes none;
    /// a test passes one so it can contribute an export format first.
    /// </param>
    public static async Task<int> ExportAsync(
        CommandRequest request, TextWriter log, Workspace? existing = null)
    {
        var workspace = existing ?? new Workspace(
            Environment.GetEnvironmentVariable("NOVALIST_SETTINGS_DIR"));
        try
        {
            return await RunExportAsync(request, log, workspace);
        }
        finally
        {
            // Only what this method made.
            if (existing == null) workspace.Dispose();
        }
    }

    private static async Task<int> RunExportAsync(
        CommandRequest request, TextWriter log, Workspace workspace)
    {
        try
        {
            await workspace.OpenProjectAsync(request.ProjectPath!);
        }
        catch (Exception ex)
        {
            log.WriteLine($"Could not open the project: {ex.Message}");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(request.Book))
        {
            var book = workspace.Projects.CurrentProject?.Books
                .FirstOrDefault(b => string.Equals(b.Name, request.Book, StringComparison.OrdinalIgnoreCase));
            if (book == null)
            {
                log.WriteLine($"No book called '{request.Book}' in this project.");
                return 2;
            }
            await workspace.Projects.SwitchBookAsync(book.Id);
        }

        var chapters = workspace.Projects.GetChaptersOrdered().Select(c => c.Guid).ToArray();
        var settings = workspace.Projects.ProjectSettings;

        try
        {
            var result = await new Rpc.ExportRpc(workspace).RunAsync(
                request.Format!,
                request.OutputPath!,
                workspace.Projects.ActiveBook?.Name ?? string.Empty,
                settings.Author,
                includeTitlePage: true,
                selectedChapterGuids: chapters);

            if (!result.Success)
            {
                log.WriteLine("The export produced no file.");
                return 1;
            }
            log.WriteLine($"Wrote {result.OutputPath} ({result.SizeBytes} bytes).");
            return 0;
        }
        catch (Exception ex)
        {
            log.WriteLine($"Export failed: {ex.Message}");
            return 1;
        }
    }
}
