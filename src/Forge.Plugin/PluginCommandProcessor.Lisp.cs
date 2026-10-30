using System.Runtime.InteropServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// forge_exec_lisp — synchronous evaluation with the value returned.
//
// Root cause of the old behaviour: the "sync" path called Editor.Command(lisp), which hands the
// whole expression to AutoCAD as ONE command token. AutoCAD rejects that with eInvalidInput for
// every LISP expression, so every call silently fell back to SendStringToExecute: the expression
// ran later (after the pipe had answered), completed=false, and no value could ever come back.
//
// Now: the expression is normalized (LispEval.Prepare), wrapped in a vl-catch-all-apply envelope
// (LispEval.Wrap) and evaluated in the current command context by acedEvaluateLisp (accore.dll).
// The returned resbuf chain is decoded into JSON + AutoLISP printed text. The queued
// SendStringToExecute path is kept ONLY for the genuine case where the evaluator entry point is
// missing from this AutoCAD build — then nothing has been evaluated, so queueing cannot run the
// expression twice. Any other evaluator failure is reported, never re-sent.
public sealed partial class PluginCommandProcessor
{
    private const int MaxLispResultTextChars = 256 * 1024;

    // int acedEvaluateLisp(const wchar_t* expr, resbuf*& result) — exported (C++ mangled) by
    // accore.dll since AutoCAD 2013; widely used by the ObjectARX/.NET community.
    [DllImport("accore.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode,
        EntryPoint = "?acedEvaluateLisp@@YAHPEB_WAEAPEAUresbuf@@@Z")]
    private static extern int AcedEvaluateLisp(string expression, out IntPtr result);

    private static ForgeResult ExecLisp(ForgeCommand command)
    {
        var args = Args<ExecLispArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Lisp))
        {
            return ForgeResult.Failure(command.Id, "missing_lisp", "lisp is required.");
        }

        if (!command.UnsafeAcknowledged)
        {
            return ForgeResult.Failure(command.Id, "unsafe_not_acknowledged", "forge_exec_lisp requires unsafeAcknowledged=true.");
        }

        var prepared = LispEval.Prepare(args.Lisp);
        if (!prepared.Ok)
        {
            return ForgeResult.Failure(
                command.Id,
                prepared.ErrorCode ?? "lisp_invalid",
                prepared.ErrorMessage ?? "The LISP expression could not be prepared.",
                "Nothing was evaluated. Fix the expression (balanced parentheses, closed strings) and retry.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Lisp, normalized = prepared.Normalized, mode = "sync_eval" });
        }

        var wrapped = LispEval.Wrap(prepared.Normalized);
        int status;
        IntPtr resultPtr;
        try
        {
            status = AcedEvaluateLisp(wrapped, out resultPtr);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Genuine fallback: the evaluator is not available, so nothing ran yet. Queue the
            // (normalized) expression on the command line; its value cannot be returned.
            ActiveDoc.SendStringToExecute(prepared.Normalized + " ", true, false, false);
            return ForgeResult.Success(command.Id, new
            {
                queued = true,
                completed = false,
                mode = "queued_fallback",
                fallbackReason = $"acedEvaluateLisp unavailable in this AutoCAD build: {ex.Message}",
                note = "The expression was queued on the command line and runs after this call returns; no value is available. Verify its effect with a read tool."
            });
        }

        List<LispAtom> atoms;
        using (var rb = resultPtr == IntPtr.Zero ? null : ResultBuffer.Create(resultPtr, true))
        {
            atoms = rb is null ? [] : ToLispAtoms(rb);
        }

        if (status != LispCodes.RtNorm)
        {
            return ForgeResult.Failure(
                command.Id,
                "lisp_eval_failed",
                $"acedEvaluateLisp returned {LispCodes.StatusName(status)} ({status}).",
                "The evaluator refused or aborted the expression (reader error, cancelled input, or a nested evaluation). It was NOT queued or retried; check the drawing with a read tool before re-running a write.",
                data: new { queued = false, completed = false, mode = "sync_eval", status, statusName = LispCodes.StatusName(status) });
        }

        var outcome = LispEval.Unwrap(atoms);
        if (outcome.LispError)
        {
            return ForgeResult.Failure(
                command.Id,
                "lisp_error",
                $"AutoLISP error: {outcome.ErrorMessage}",
                "The expression ran and raised an error (completed=true, the error was caught). Side effects made before the error stay in the drawing inside this call's undo group; forge_undo can revert them.",
                data: new { queued = false, completed = true, mode = "sync_eval", lispError = outcome.ErrorMessage });
        }

        var text = outcome.Text;
        var textTruncated = text.Length > MaxLispResultTextChars;
        if (textTruncated)
        {
            text = text[..MaxLispResultTextChars];
        }

        return ForgeResult.Success(command.Id, new
        {
            queued = false,
            completed = true,
            mode = "sync_eval",
            result = outcome.Value,
            resultText = text,
            resultTextTruncated = textTruncated,
            resultType = outcome.Type,
            envelope = outcome.EnvelopeFound
        });
    }

    private static List<LispAtom> ToLispAtoms(ResultBuffer rb)
    {
        var atoms = new List<LispAtom>();
        foreach (TypedValue tv in rb)
        {
            object? value;
            try
            {
                value = tv.Value switch
                {
                    ObjectId id => id.IsNull ? "0" : id.Handle.ToString(),
                    SelectionSet ss => PickSetHandles(ss),
                    Point3d p3 => new[] { p3.X, p3.Y, p3.Z },
                    Point2d p2 => new[] { p2.X, p2.Y },
                    _ => tv.Value
                };
            }
            catch (Exception ex)
            {
                value = $"<unreadable: {ex.Message}>";
            }

            atoms.Add(new LispAtom(tv.TypeCode, value));
        }

        return atoms;
    }

    private static string[] PickSetHandles(SelectionSet ss)
    {
        try
        {
            return ss.GetObjectIds().Select(id => id.Handle.ToString()).ToArray();
        }
        catch
        {
            return [];
        }
    }
}
