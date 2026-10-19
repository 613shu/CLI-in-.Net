using System.CommandLine;

string[] allowedLanguages = { "cs", "js", "ts", "py", "java", "html", "css", "sql" };
string[] excludedFolders = { "bin", "obj", "debug", "release", "publish", ".vs", ".git", ".idea", "node_modules", "packages" };

// ================= options =================

var languages = new Option<string[]>("--language", "-l")
{
    Description = $"programming languages to include ({string.Join(", ", allowedLanguages)}) or 'all'",
    Required = true,
    AllowMultipleArgumentsPerToken = true
};
languages.AcceptOnlyFromAmong(["all", .. allowedLanguages]);

var output = new Option<FileInfo>("--output", "-o")
{
    Description = "bundle file name, or a full path (wrap paths with spaces in quotes)",
    Required = true
};

var note = new Option<bool>("--note", "-n")
{
    Description = "write the source file name and relative path as a comment"
};

var sort = new Option<string>("--sort", "-s")
{
    Description = "order of the files: by file name (name) or by code type (type)",
    DefaultValueFactory = _ => "name"
};
sort.AcceptOnlyFromAmong("name", "type");

var removeLines = new Option<bool>("--remove-empty-lines", "-r")
{
    Description = "remove empty lines from the source code"
};

var author = new Option<string>("--author", "-a")
{
    Description = "name of the bundle's author"
};

// ================= bundle command =================

var bundle = new Command("bundle", "bundle code files into one file")
{
    languages, output, note, sort, removeLines, author
};

bundle.SetAction(parseResult =>
{
    string[] selected = parseResult.GetValue(languages)!;
    FileInfo outputFile = parseResult.GetValue(output)!;
    bool addNote = parseResult.GetValue(note);
    string sortBy = parseResult.GetValue(sort)!;
    bool removeEmpty = parseResult.GetValue(removeLines);
    string? authorName = parseResult.GetValue(author);

    if (selected.Contains("all"))
        selected = allowedLanguages;

    if (outputFile.Directory == null || !outputFile.Directory.Exists)
    {
        Console.Error.WriteLine($"Error: the folder '{outputFile.DirectoryName}' does not exist.");
        return 1;
    }

    string currentDir = Directory.GetCurrentDirectory();

    List<string> files = Directory.GetFiles(currentDir, "*", SearchOption.AllDirectories)
        .Where(f => !IsInExcludedFolder(currentDir, f))
        .Where(f => selected.Contains(Path.GetExtension(f).TrimStart('.').ToLower()))
        .Where(f => !string.Equals(Path.GetFullPath(f), outputFile.FullName, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (files.Count == 0)
    {
        Console.Error.WriteLine("No code files of the selected languages were found in this folder.");
        return 1;
    }

    files = sortBy == "type"
        ? files.OrderBy(f => Path.GetExtension(f).ToLower()).ThenBy(Path.GetFileName).ToList()
        : files.OrderBy(Path.GetFileName).ToList();

    try
    {
        using var writer = new StreamWriter(outputFile.FullName);

        if (!string.IsNullOrWhiteSpace(authorName))
        {
            writer.WriteLine(Comment(files[0], $"Author: {authorName}"));
            writer.WriteLine();
        }

        foreach (string file in files)
        {
            if (addNote)
                writer.WriteLine(Comment(file, $"Source: {Path.GetFileName(file)} | {Path.GetRelativePath(currentDir, file)}"));

            IEnumerable<string> lines = File.ReadAllLines(file);

            if (removeEmpty)
                lines = lines.Where(line => !string.IsNullOrWhiteSpace(line));

            foreach (string line in lines)
                writer.WriteLine(line);

            writer.WriteLine();
        }

        Console.WriteLine($"Bundle created: {outputFile.FullName} ({files.Count} files)");
        return 0;
    }
    catch (UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Error: no permission to write to '{outputFile.FullName}'.");
    }
    catch (PathTooLongException)
    {
        Console.Error.WriteLine("Error: the output path is too long.");
    }
    catch (IOException e)
    {
        Console.Error.WriteLine($"Error: could not save the bundle file. {e.Message}");
    }
    return 1;
});

// ================= create-rsp command =================

var createRsp = new Command("create-rsp", "build a response file with a ready bundle command, step by step");

createRsp.SetAction(parseResult =>
{
    Console.WriteLine("Answer the questions to build your bundle command.\n");

    // language (required, from the allowed list)
    string[] langs;
    while (true)
    {
        string answer = Ask($"Languages, separated by spaces ({string.Join(", ", allowedLanguages)}) or 'all': ");
        langs = answer.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] invalid = langs.Where(l => l != "all" && !allowedLanguages.Contains(l)).ToArray();

        if (langs.Length == 0)
            Console.WriteLine("  Please enter at least one language.");
        else if (invalid.Length > 0)
            Console.WriteLine($"  Unknown language: {string.Join(", ", invalid)}. Try again.");
        else
            break;
    }

    // output (required, valid path in an existing folder)
    string outputPath;
    while (true)
    {
        outputPath = Ask("Output file name or full path: ").Trim('"');
        string? error = ValidateOutputPath(outputPath);
        if (error == null) break;
        Console.WriteLine($"  {error}");
    }

    bool addNote = AskYesNo("Write the source of each file as a comment? (y/n): ");

    string sortBy;
    while (true)
    {
        sortBy = Ask("Sort files by 'name' or 'type' (Enter = name): ").ToLower();
        if (sortBy == "") sortBy = "name";
        if (sortBy is "name" or "type") break;
        Console.WriteLine("  Please type 'name' or 'type'.");
    }

    bool removeEmpty = AskYesNo("Remove empty lines? (y/n): ");

    string authorName = Ask("Author name (Enter to skip): ");
    if (authorName.Contains('"'))
        authorName = authorName.Replace("\"", "");

    string rspName;
    while (true)
    {
        rspName = Ask("Response file name (Enter = bundle.rsp): ").Trim('"');
        if (rspName == "") rspName = "bundle.rsp";
        if (!rspName.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase)) rspName += ".rsp";
        if (rspName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) break;
        Console.WriteLine("  The file name contains invalid characters.");
    }

    // build the command
    var lines = new List<string>
    {
        "bundle",
        $"--language {string.Join(" ", langs)}",
        $"--output \"{outputPath}\"",
        $"--sort {sortBy}"
    };
    if (addNote) lines.Add("--note");
    if (removeEmpty) lines.Add("--remove-empty-lines");
    if (authorName != "") lines.Add($"--author \"{authorName}\"");

    try
    {
        File.WriteAllLines(rspName, lines);
        Console.WriteLine($"\nResponse file created: {Path.GetFullPath(rspName)}");
        Console.WriteLine($"Run it with:  fb @{rspName}");
        return 0;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Error: could not save the response file. {e.Message}");
        return 1;
    }
});

// ================= root =================

var fb = new RootCommand("fb - bundle code files into one file");
fb.Subcommands.Add(bundle);
fb.Subcommands.Add(createRsp);

return await fb.Parse(args).InvokeAsync();

// ================= helpers =================

bool IsInExcludedFolder(string root, string file)
{
    string relative = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
    return relative
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(part => excludedFolders.Contains(part.ToLower()));
}

string Comment(string file, string text) =>
    Path.GetExtension(file).ToLower() switch
    {
        ".py" => $"# {text}",
        ".html" => $"<!-- {text} -->",
        ".css" => $"/* {text} */",
        ".sql" => $"-- {text}",
        _ => $"// {text}"
    };

string Ask(string question)
{
    Console.Write(question);
    return Console.ReadLine()?.Trim() ?? "";
}

bool AskYesNo(string question)
{
    while (true)
    {
        string answer = Ask(question).ToLower();
        if (answer is "y" or "yes") return true;
        if (answer is "n" or "no" or "") return false;
        Console.WriteLine("  Please type y or n.");
    }
}

string? ValidateOutputPath(string path)
{
    if (path == "")
        return "Output is required.";
    if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
        Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        return "The path contains invalid characters.";
    if (Path.GetFileName(path) == "")
        return "Please include a file name, not only a folder.";

    try
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder == null || !Directory.Exists(folder))
            return $"The folder '{folder}' does not exist.";
    }
    catch (Exception)
    {
        return "The path is not valid.";
    }
    return null;
}
