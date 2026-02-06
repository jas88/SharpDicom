using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace SharpDicom.Cli.Helpers;

/// <summary>
/// TTY-aware progress reporter. Uses Spectre.Console progress bars when interactive,
/// line-per-item logging otherwise. All progress output goes to stderr.
/// </summary>
internal sealed class ProgressReporter
{
    private readonly TextWriter _stderr;

    /// <summary>
    /// Whether the output is an interactive terminal.
    /// </summary>
    public bool IsInteractive { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="ProgressReporter"/>.
    /// </summary>
    /// <param name="stderr">The stderr writer for progress output.</param>
    public ProgressReporter(TextWriter stderr)
    {
        _stderr = stderr ?? throw new ArgumentNullException(nameof(stderr));
        IsInteractive = AnsiConsole.Profile.Capabilities.Interactive;
    }

    /// <summary>
    /// Run work with progress reporting.
    /// </summary>
    /// <param name="description">Task description shown in progress UI.</param>
    /// <param name="totalItems">Total number of items to process.</param>
    /// <param name="work">
    /// Delegate that performs work. Receives an <c>Action&lt;int&gt;</c> to call when items complete
    /// (the argument is the number of items completed in this call, typically 1).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RunWithProgressAsync(
        string description,
        int totalItems,
        Func<Action<int>, CancellationToken, Task> work,
        CancellationToken ct)
    {
        if (IsInteractive)
        {
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(_stderr),
            });

            await console.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask(description, maxValue: totalItems);
                    await work(increment => task.Increment(increment), ct).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        else
        {
            var completed = 0;
            await work(
                increment =>
                {
                    completed += increment;
                    _stderr.WriteLine($"{description}: {completed}/{totalItems}");
                },
                ct).ConfigureAwait(false);
        }
    }
}
