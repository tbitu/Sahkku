using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The match loop's clock: waiting "for a while" is expressed in frames rather than as a wall-clock
/// delay.
///
/// A Unity WebGL player runs the whole engine on one WebAssembly thread, and that thread has no timer
/// pump: <c>System.Threading.Timer</c> callbacks never fire there. <see cref="Task.Delay"/> is built on
/// exactly such a timer, so on WebGL its task never completes, the awaiting method is never resumed, and
/// the match loop stops for good at its first delay. <c>await Task.Yield()</c> has no such dependency: it
/// posts the continuation to Unity's main-thread <c>SynchronizationContext</c>, which the player loop
/// drains once per frame, so polling <see cref="Time.time"/> is a wait that comes back on every platform
/// — desktop, editor and WebGL alike.
///
/// Both helpers honour the match's cancellation token by throwing
/// <see cref="System.OperationCanceledException"/>, which is what lets <see cref="GameLogic"/>'s match
/// loop unwind instead of resuming a cancelled match.
/// </summary>
public static class UnityTimeDelays
{
    /// <summary>
    /// Waits <paramref name="seconds"/> of frame time. Every wait gives the frame back at least once, so
    /// a delay of zero (or less) means "continue on the next frame".
    /// </summary>
    /// <exception cref="System.OperationCanceledException">The caller's token was cancelled while waiting.</exception>
    public static async Task DelayAsync(float seconds, CancellationToken cancellationToken)
    {
        // The deadline is read from the same frame clock the loop below watches, so the wait is measured
        // in the time the player actually spent running frames (a paused game pauses the pacing too).
        float deadline = Time.time + seconds;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
        while (Time.time < deadline);
    }

    /// <summary>
    /// True when <paramref name="pending"/> had not finished after <paramref name="timeoutSeconds"/> of
    /// frame time.
    ///
    /// The task is polled rather than raced against a timer: <c>Task.WhenAny(pending, Task.Delay(...))</c>
    /// would be resolved by the timer that never fires on WebGL, leaving a bot that never answers — and
    /// every decision after it — waiting forever. The timeout is floored at a tenth of a second, because
    /// anything shorter is a bug rather than a deadline.
    /// </summary>
    /// <param name="pending">The task being waited on; must not be null.</param>
    /// <exception cref="System.OperationCanceledException">The caller's token was cancelled while waiting.</exception>
    public static async Task<bool> TimedOutAsync(Task pending, float timeoutSeconds, CancellationToken cancellationToken)
    {
        float deadline = Time.time + Mathf.Max(0.1f, timeoutSeconds);
        while (!pending.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A task that finished during this frame wins over the deadline: the agent did answer, so
            // throwing away its answer because the clock rounded past the deadline would be wrong.
            if (Time.time >= deadline) return true;

            await Task.Yield();
        }
        return false;
    }
}
