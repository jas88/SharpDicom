using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Cli.Commands;
using SharpDicom.Cli.Helpers;
using SharpDicom.Codecs;

// ---------------------------------------------------------------------------
// sharpdcm - SharpDicom command-line toolkit
// ---------------------------------------------------------------------------

CodecInitializer.RegisterAll();

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

// ---- Subcommands ----------------------------------------------------------

rootCommand.Subcommands.Add(DumpCommand.Create());

rootCommand.Subcommands.Add(StoreCommand.Create());

rootCommand.Subcommands.Add(FindCommand.Create());

rootCommand.Subcommands.Add(LintCommand.Create());

rootCommand.Subcommands.Add(FixCommand.Create());

rootCommand.Subcommands.Add(ConvertCommand.Create());

// ---- Run ------------------------------------------------------------------

return await rootCommand.Parse(args).InvokeAsync();
