using System;
using System.Collections.Generic;
using System.Text;

internal class TaskCreationOptions
{
    // HACK(Cysharp): Unity WebGL doesn't support ThreadPool. RunContinuationsAsynchronously causes a stuck at runtime.
    public const System.Threading.Tasks.TaskCreationOptions RunContinuationsAsynchronously = System.Threading.Tasks.TaskCreationOptions.None;
    public const System.Threading.Tasks.TaskCreationOptions None = System.Threading.Tasks.TaskCreationOptions.None;
}
