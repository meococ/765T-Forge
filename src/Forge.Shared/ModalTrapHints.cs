namespace Forge.Shared;

public static class ModalTrapHints
{
    public static IReadOnlyList<QaFinding> EvaluateAutomationSysvars(
        int? fileDia,
        int? expert = null,
        bool commandActive = false,
        bool modalDialogLikely = false)
    {
        var findings = new List<QaFinding>();
        if (fileDia is not null and not 0)
        {
            findings.Add(new QaFinding(
                "modal_trap_filedia",
                "error",
                $"FILEDIA={fileDia}; batch/agent sessions require FILEDIA=0.",
                "Call forge_system_setvar name=FILEDIA value=0 before plot/publish.",
                "forge_qa_modal_trap"));
        }

        if (commandActive)
        {
            findings.Add(new QaFinding(
                "modal_trap_busy",
                "error",
                "AutoCAD command context appears busy; do not chain writes.",
                "Wait, cancel the active command, then forge_qa_readback_after_timeout if a write may have committed.",
                "forge_qa_modal_trap"));
        }

        if (modalDialogLikely)
        {
            findings.Add(new QaFinding(
                "modal_trap_dialog",
                "error",
                "A modal dialog is likely blocking automation.",
                "Dismiss the dialog in AutoCAD; fix missing fonts/paths rather than auto-clicking.",
                "forge_qa_modal_trap"));
        }

        if (expert is 0)
        {
            findings.Add(new QaFinding(
                "modal_trap_expert",
                "warning",
                "EXPERT=0 may surface extra prompts during batch.",
                "Consider EXPERT=1 for controlled agent sessions.",
                "forge_system_setvar"));
        }

        return findings;
    }
}
