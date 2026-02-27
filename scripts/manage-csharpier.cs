#!/usr/bin/env dotnet

// manage-csharpier installs CSharpier to all .NET solutions,
// updates them to the latest version of CSharpier, formats
// all their C# and XML code and sends a PR to their repositories.

// This is a file-based app. So you can run it with the following command:
//
// ```bash
// $ dotnet run manage-csharpier.cs
// ```
// File-based apps references:
//
// - https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps
// - https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/tutorials/file-based-programs
// - https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/overview#file-based-apps

// It uses Colorful.Console for applying color to console output:
//
// - `Color.Yellow` for separators.
// - `Color.DeepPink` for `.sln` files.
// - `Color.Green` for StandardOutput stream and updated repositories.
// - `Color.Red` for StandardError stream and restored repositores.
//
// Colorful.Console reference:
//
// - https://github.com/tomakita/Colorful.Console
#:package Colorful.Console@1.2.15

using System.Diagnostics;
using System.Drawing;
using System.Text;
using Console = Colorful.Console;

// Parent directory that holds all repositories (including .NET ones).
const string ParentDirectory = "/Users/alejandroantonioestornellsalamanca/Developer";
const string MainBranch = "main";
const string BuildBranch = "build/update-csharpier-format";

var watch = new Stopwatch();
watch.Start();

Directory.SetCurrentDirectory(ParentDirectory);

// Preallocate lists.
var updatedRepositories = new List<string>(32);
var restoredRepositories = new List<string>(32);

foreach (var directory in Directory.EnumerateDirectories("."))
{
    var trimmedDirectory = directory.Replace("./", "");

    var slnFiles = Directory.GetFiles(directory, "*.sln");
    var isSlnFile = slnFiles.Length > 0;
    if (!isSlnFile)
    {
        continue;
    }

    Directory.SetCurrentDirectory(directory);

    DrawStart(slnFiles[0]);

    // Fetch the latest changes on main.
    // git usually reports its progress to the standard output and error streams.
    // That's why, for now, the standard output and error streams are checked.
    //
    // - https://git-scm.com/docs/git-switch#Documentation/git-switch.txt---progress
    // - https://git-scm.com/docs/git-push#Documentation/git-push.txt---progress
    var (output, error) = ExecuteProcess("git", "switch main");
    DrawCommand("git switch main", output, error);
    (output, error) = ExecuteProcess("git", "pull");
    DrawCommand("git pull", output, error);

    // Create `dotnet-tool.json` if it doesn't exist and install CSharpier.
    // Update CSharpier if it is already installed.
    (output, error) = ExecuteProcess("dotnet", "tool install csharpier");
    DrawCommand("dotnet tool install csharpier", output, error);
    if (!string.IsNullOrWhiteSpace(error))
    {
        RestoreRepository(restoredRepositories, trimmedDirectory);
        continue;
    }

    // Format C# and XML code.
    (output, error) = ExecuteProcess("dotnet", "csharpier format .");
    DrawCommand("dotnet csharpier format .", output, error);

    // Check if there are new changes.
    (output, error) = ExecuteProcess("git", "status -s");
    DrawCommand("git status -s", output, error);
    if (string.IsNullOrWhiteSpace(output) || !string.IsNullOrWhiteSpace(error))
    {
        RestoreRepository(restoredRepositories, trimmedDirectory);
        continue;
    }

    // Check if there are tests and if they all pass.
    // Bear in mind that `dotnet test` also builds the solution.
    (output, error) = ExecuteProcess("dotnet", "test");
    DrawCommand("dotnet test", output, error);
    if (!string.IsNullOrWhiteSpace(error))
    {
        RestoreRepository(restoredRepositories, trimmedDirectory);
        continue;
    }

    // Create branch `build/update-csharpier-format`.
    (output, error) = ExecuteProcess("git", $"switch -c {BuildBranch}");
    DrawCommand($"git switch -c {BuildBranch}", output, error);

    // Add changes to the index/staging area and commit them.
    (output, error) = ExecuteProcess("git", "add -A");
    DrawCommand("git add -A", output, error);
    (output, error) = ExecuteProcess("git", "commit -m \"Update CSharpier and format code\"");
    DrawCommand("git commit -m \"Update CSharpier and format code\"", output, error);

    // Push new branch and committed changes.
    (output, error) = ExecuteProcess("git", $"push -u origin {BuildBranch}");
    DrawCommand($"git push -u origin {BuildBranch}", output, error);

    updatedRepositories.Add(trimmedDirectory);

    DrawSeparator();

    Directory.SetCurrentDirectory(ParentDirectory);
}

watch.Stop();
var elapsed = watch.ElapsedMilliseconds;

DrawSummary(updatedRepositories, restoredRepositories, elapsed);

static void DrawSeparator() => Console.WriteLine("-------", Color.Yellow);

static void DrawStart(string solution)
{
    DrawSeparator();
    Console.WriteLine($"Solution: {solution}", Color.DeepPink);
}

// Create the process that's going to be executed and execute it.
// For now, it doesn't take into account the exit codes returned
// by the process.
static (string output, string error) ExecuteProcess(string fileName, string arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    using var process = new Process { StartInfo = startInfo };
    var error = new StringBuilder();
    process.ErrorDataReceived += (sender, e) => error.Append(e.Data);

    _ = process.Start();

    // The StandardError stream is read asynchronously whereas the
    // StandardOutput stream is read synchronously. This is a must
    // to avoid deadlocks between the parent and child processes. They
    // can be swapped, but one of them must be read asynchronously and the
    // other one synchronously.
    //
    // https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.redirectstandarderror?view=net-10.0#remarks
    process.BeginErrorReadLine();
    var output = process.StandardOutput.ReadToEnd();

    process.WaitForExit();

    return (output, error.ToString());
}

// Print to the console the executed command and its related info:
//
// - StandardOutput stream data.
// - StandardError stream data.
static void DrawCommand(string command, string output, string error)
{
    // Fix the case when `output` is null or whitespace because it appears
    // with the wrong format: `output: error`.
    if (string.IsNullOrWhiteSpace(output))
    {
        output = "\n";
    }

    Console.WriteLine($"Executing: {command}");
    Console.Write($"Output: {output}", Color.Green);
    Console.WriteLine($"Error: {error}", Color.Red);
}

// Restore the repository status.
static void RestoreRepository(List<string> restoredRepositories, string trimmedDirectory)
{
    _ = ExecuteProcess("git", $"switch {MainBranch}");
    _ = ExecuteProcess("git", $"branch -D {BuildBranch}");
    _ = ExecuteProcess("git", "restore ./");

    restoredRepositories.Add(trimmedDirectory);

    Console.WriteLine("Restoring repository...");
    DrawSeparator();

    Directory.SetCurrentDirectory(ParentDirectory);
}

// Print to the console a summary of the executed changes.
static void DrawSummary(
    List<string> updatedRepositories,
    List<string> restoredRepositories,
    long elapsed
)
{
    DrawSeparator();
    Console.WriteLine("Summary");

    Console.WriteLine($"\tUpdated repositories (total: {updatedRepositories.Count}):", Color.Green);
    foreach (var repository in updatedRepositories)
    {
        Console.WriteLine($"\t\t{repository}");
    }

    Console.WriteLine(
        $"\tRestored repositories (total: {restoredRepositories.Count}):",
        Color.Red
    );
    foreach (var repository in restoredRepositories)
    {
        Console.WriteLine($"\t\t{repository}");
    }

    Console.WriteLine($"Total Time: {elapsed} ms");

    DrawSeparator();
}
