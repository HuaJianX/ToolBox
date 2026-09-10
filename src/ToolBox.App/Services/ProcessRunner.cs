using System.Diagnostics;
using System.Text;

namespace ToolBox.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// 跑外部组件的小封装。三个要点：
/// 1. 不弹黑窗口（CreateNoWindow），用户看不到一闪一闪的 cmd。
/// 2. 用 ArgumentList 传参，文件名里有空格、中文都不会出错。
/// 3. 取消的时候整棵进程树一起杀掉，不留后台残留。
/// </summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null) return;
            standardOutput.AppendLine(args.Data);
            onOutputLine?.Invoke(args.Data);
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null) return;
            standardError.AppendLine(args.Data);
            onErrorLine?.Invoke(args.Data);
        };

        try
        {
            if (!process.Start()) throw new InvalidOperationException($"组件没能启动：{Path.GetFileName(fileName)}");
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException($"组件没能启动：{Path.GetFileName(fileName)}", exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 已经退出了，正合我意。
            }
        });

        // 这里故意不把 token 传给 WaitForExitAsync：取消时我们要先杀进程，
        // 等它真的退出了，再带着退出码和错误输出抛 OperationCanceledException，
        // 这样临时文件才清理得干净。
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        // 等两个异步读取流都收尾，否则可能丢最后几行。
        process.WaitForExit();

        cancellationToken.ThrowIfCancellationRequested();

        return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }
}
