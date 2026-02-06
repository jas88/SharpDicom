using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Cli.Helpers;

// ---------------------------------------------------------------------------
// sharpdcm - SharpDicom command-line toolkit
// ---------------------------------------------------------------------------

var rootCommand = new RootCommand("SharpDicom command-line toolkit");

// ---- Global options -------------------------------------------------------

var formatOption = new Option<string>("--format", "-f")
{
    Description = "Output format: text, json, xml",
    DefaultValueFactory = _ => "text",
};

var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Enable verbose output",
};

var quietOption = new Option<bool>("--quiet", "-q")
{
    Description = "Suppress all output except errors",
};

var debugOption = new Option<bool>("--debug")
{
    Description = "Enable debug-level output",
};

var noColorOption = new Option<bool>("--no-color")
{
    Description = "Disable coloured output",
};

var configOption = new Option<string?>("--config")
{
    Description = "Path to configuration file (default: ~/.sharpdcm/config.toml)",
};

var continueOnErrorOption = new Option<bool>("--continue-on-error")
{
    Description = "Continue processing after errors",
};

rootCommand.Options.Add(formatOption);
rootCommand.Options.Add(verboseOption);
rootCommand.Options.Add(quietOption);
rootCommand.Options.Add(debugOption);
rootCommand.Options.Add(noColorOption);
rootCommand.Options.Add(configOption);
rootCommand.Options.Add(continueOnErrorOption);

// ---- Stub subcommands -----------------------------------------------------

var dumpCommand = new Command("dump", "Display DICOM file contents");
dumpCommand.SetAction((ParseResult _, CancellationToken _) =>
{
    Console.Error.WriteLine("Not yet implemented");
    return Task.FromResult(ExitCodes.UsageError);
});
rootCommand.Subcommands.Add(dumpCommand);

var storeCommand = new Command("store", "Send DICOM files to a PACS server (C-STORE)");
storeCommand.SetAction((ParseResult _, CancellationToken _) =>
{
    Console.Error.WriteLine("Not yet implemented");
    return Task.FromResult(ExitCodes.UsageError);
});
rootCommand.Subcommands.Add(storeCommand);

var findCommand = new Command("find", "Query a PACS server for studies/series/instances (C-FIND)");
findCommand.SetAction((ParseResult _, CancellationToken _) =>
{
    Console.Error.WriteLine("Not yet implemented");
    return Task.FromResult(ExitCodes.UsageError);
});
rootCommand.Subcommands.Add(findCommand);

var lintCommand = new Command("lint", "Validate DICOM files against the standard");
lintCommand.SetAction((ParseResult _, CancellationToken _) =>
{
    Console.Error.WriteLine("Not yet implemented");
    return Task.FromResult(ExitCodes.UsageError);
});
rootCommand.Subcommands.Add(lintCommand);

var fixCommand = new Command("fix", "Repair common issues in DICOM files");
fixCommand.SetAction((ParseResult _, CancellationToken _) =>
{
    Console.Error.WriteLine("Not yet implemented");
    return Task.FromResult(ExitCodes.UsageError);
});
rootCommand.Subcommands.Add(fixCommand);

// ---- Run ------------------------------------------------------------------

return await rootCommand.Parse(args).InvokeAsync();
