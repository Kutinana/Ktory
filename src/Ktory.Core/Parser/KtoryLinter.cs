using System;
using System.Collections.Generic;
using System.Linq;
using Ktory.Core.Ast;
using Ktory.Core.Common;

namespace Ktory.Core.Parser
{
    /// <summary>
    /// Static code analysis engine for Ktory scripts.
    /// Analyzes syntactic and semantic errors (blocking execution) and warnings (potential bugs or dead code).
    /// </summary>
    public static class KtoryLinter
    {
        public static List<KtoryDiagnostic> Analyze(string? source)
        {
            var diagnostics = new List<KtoryDiagnostic>();
            if (string.IsNullOrWhiteSpace(source))
            {
                return diagnostics;
            }

            KtoryFile file;
            try
            {
                file = KtoryParser.Parse(source);
            }
            catch (KtoryException ex)
            {
                string msg = ex.Message;
                int paren = msg.LastIndexOf(" (Line: ");
                if (paren > 0)
                {
                    msg = msg.Substring(0, paren);
                }

                string code = ClassifyErrorCode(msg);
                int line = Math.Max(1, ex.Line);
                int col = Math.Max(1, ex.Column);

                diagnostics.Add(new KtoryDiagnostic(
                    KtoryDiagnosticSeverity.Error,
                    code,
                    msg,
                    line,
                    col,
                    line,
                    col + 1
                ));

                return diagnostics;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new KtoryDiagnostic(
                    KtoryDiagnosticSeverity.Error,
                    "KTR_E001",
                    ex.Message,
                    1,
                    1
                ));
                return diagnostics;
            }

            // Semantic and Best Practice Warnings
            CheckUnreachableCode(file, diagnostics);
            CheckUnreferencedSections(file, diagnostics);
            CheckMissingDefaultLanguageTranslations(file, diagnostics);
            CheckSpeakerDefinitions(file, diagnostics);
            CheckEmptySections(file, diagnostics);

            return diagnostics;
        }

        private static string ClassifyErrorCode(string message)
        {
            if (message.IndexOf("Unrecognized control flow", StringComparison.OrdinalIgnoreCase) >= 0) return "KTR_E002";
            if (message.IndexOf("has no target specified", StringComparison.OrdinalIgnoreCase) >= 0) return "KTR_E003";
            if (message.IndexOf("Target section", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0) return "KTR_E004";
            if (message.IndexOf("cannot be combined with an indented branch body", StringComparison.OrdinalIgnoreCase) >= 0) return "KTR_E006";
            if (message.IndexOf("Decorator has no preceding anchor", StringComparison.OrdinalIgnoreCase) >= 0) return "KTR_E007";
            if (message.IndexOf("@speaker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Speaker", StringComparison.OrdinalIgnoreCase) >= 0) return "KTR_E008";
            if (message.IndexOf("Forbidden block Markdown", StringComparison.OrdinalIgnoreCase) >= 0) return "KTR_E009";
            if (message.IndexOf("Duplicate section", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("already defined", StringComparison.OrdinalIgnoreCase) >= 0) return "KTR_E010";

            return "KTR_E001";
        }

        private static void CheckUnreachableCode(KtoryFile file, List<KtoryDiagnostic> diagnostics)
        {
            foreach (var kvp in file.Blocks)
            {
                var block = kvp.Value;
                if (block.IsRoot) continue;

                bool terminalEncountered = false;
                int terminalLine = 0;

                foreach (var step in block.Steps)
                {
                    if (terminalEncountered)
                    {
                        diagnostics.Add(new KtoryDiagnostic(
                            KtoryDiagnosticSeverity.Warning,
                            "KTR_W001",
                            $"Unreachable code in section '=== {block.Label} ==='. Step at line {step.LineNumber} appears after terminal control flow at line {terminalLine}.",
                            step.LineNumber,
                            1
                        ));
                    }
                    else if (step is ControlFlowStep cf && (cf.FlowType == ControlFlowType.Return || cf.FlowType == ControlFlowType.End))
                    {
                        terminalEncountered = true;
                        terminalLine = step.LineNumber;
                    }
                }
            }
        }

        private static void CheckUnreferencedSections(KtoryFile file, List<KtoryDiagnostic> diagnostics)
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void CollectReferences(StepNode step)
            {
                if (step is ControlFlowStep cf && !string.IsNullOrEmpty(cf.TargetLabel))
                {
                    referenced.Add(cf.TargetLabel);
                }
                else if (step is ContainerStep cs)
                {
                    foreach (var item in cs.Items)
                    {
                        if (item.TargetJump != null && !string.IsNullOrEmpty(item.TargetJump.TargetLabel))
                        {
                            referenced.Add(item.TargetJump.TargetLabel);
                        }
                        foreach (var inline in item.InlineSteps)
                        {
                            CollectReferences(inline);
                        }
                    }
                }
            }

            foreach (var block in file.Blocks.Values)
            {
                foreach (var step in block.Steps)
                {
                    CollectReferences(step);
                }
            }

            foreach (var block in file.Blocks.Values)
            {
                if (block.IsRoot) continue;
                if (!referenced.Contains(block.Label))
                {
                    diagnostics.Add(new KtoryDiagnostic(
                        KtoryDiagnosticSeverity.Warning,
                        "KTR_W002",
                        $"Section '=== {block.Label} ===' is defined but never referenced.",
                        block.StartLineNumber > 0 ? block.StartLineNumber : 1,
                        1
                    ));
                }
            }
        }

        private static void CheckMissingDefaultLanguageTranslations(KtoryFile file, List<KtoryDiagnostic> diagnostics)
        {
            if (!file.HasLocalization || string.IsNullOrEmpty(file.DefaultLang)) return;
            string defaultLang = file.DefaultLang!;

            void CheckStep(StepNode step)
            {
                if (step is TextStep ts)
                {
                    bool hasSpecificLocales = ts.TextVariants.Keys.Any(k => !string.IsNullOrEmpty(k));
                    if (hasSpecificLocales && !ts.TextVariants.ContainsKey(defaultLang) && !ts.TextVariants.ContainsKey(""))
                    {
                        diagnostics.Add(new KtoryDiagnostic(
                            KtoryDiagnosticSeverity.Warning,
                            "KTR_W003",
                            $"Dialogue line has localization variants but is missing translation for default language '{defaultLang}'.",
                            ts.LineNumber,
                            1
                        ));
                    }
                }
                else if (step is ContainerStep cs)
                {
                    foreach (var item in cs.Items)
                    {
                        bool hasSpecificLocales = item.LabelVariants.Keys.Any(k => !string.IsNullOrEmpty(k));
                        if (hasSpecificLocales && !item.LabelVariants.ContainsKey(defaultLang) && !item.LabelVariants.ContainsKey(""))
                        {
                            diagnostics.Add(new KtoryDiagnostic(
                                KtoryDiagnosticSeverity.Warning,
                                "KTR_W003",
                                $"Option item has localization variants but is missing translation for default language '{defaultLang}'.",
                                item.LineNumber,
                                1
                            ));
                        }
                        foreach (var inline in item.InlineSteps)
                        {
                            CheckStep(inline);
                        }
                    }
                }
            }

            foreach (var block in file.Blocks.Values)
            {
                foreach (var step in block.Steps)
                {
                    CheckStep(step);
                }
            }
        }

        private static void CheckSpeakerDefinitions(KtoryFile file, List<KtoryDiagnostic> diagnostics)
        {
            if (file.Speakers.Count == 0) return;

            var usedSpeakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void ScanSpeakers(StepNode step)
            {
                if (step is TextStep ts && !string.IsNullOrEmpty(ts.Speaker))
                {
                    usedSpeakers.Add(ts.Speaker);
                }
                else if (step is ContainerStep cs)
                {
                    foreach (var item in cs.Items)
                    {
                        foreach (var inline in item.InlineSteps)
                        {
                            ScanSpeakers(inline);
                        }
                    }
                }
            }

            foreach (var block in file.Blocks.Values)
            {
                foreach (var step in block.Steps)
                {
                    ScanSpeakers(step);
                }
            }

            // W004: Unused speaker definition
            foreach (var spk in file.Speakers)
            {
                bool isUsed = false;
                if (!string.IsNullOrEmpty(spk.Id) && usedSpeakers.Contains(spk.Id))
                {
                    isUsed = true;
                }
                else
                {
                    foreach (var alias in spk.Aliases)
                    {
                        if (usedSpeakers.Contains(alias))
                        {
                            isUsed = true;
                            break;
                        }
                    }
                }

                if (!isUsed)
                {
                    string name = spk.Id ?? spk.DisplayNames.Values.FirstOrDefault() ?? "unknown";
                    diagnostics.Add(new KtoryDiagnostic(
                        KtoryDiagnosticSeverity.Warning,
                        "KTR_W004",
                        $"Speaker '{name}' declared at line {spk.LineNumber} is never used.",
                        spk.LineNumber,
                        1
                    ));
                }
            }

            // W005: Undeclared speaker name used in dialogue
            var reportedUndeclared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void CheckUndeclared(StepNode step)
            {
                if (step is TextStep ts && !string.IsNullOrEmpty(ts.Speaker))
                {
                    if (!file.SpeakersByAlias.ContainsKey(ts.Speaker))
                    {
                        if (reportedUndeclared.Add(ts.Speaker + ":" + ts.LineNumber))
                        {
                            diagnostics.Add(new KtoryDiagnostic(
                                KtoryDiagnosticSeverity.Warning,
                                "KTR_W005",
                                $"Speaker '{ts.Speaker}' is not declared in any @speaker definition.",
                                ts.LineNumber,
                                1
                            ));
                        }
                    }
                }
                else if (step is ContainerStep cs)
                {
                    foreach (var item in cs.Items)
                    {
                        foreach (var inline in item.InlineSteps)
                        {
                            CheckUndeclared(inline);
                        }
                    }
                }
            }

            foreach (var block in file.Blocks.Values)
            {
                foreach (var step in block.Steps)
                {
                    CheckUndeclared(step);
                }
            }
        }

        private static void CheckEmptySections(KtoryFile file, List<KtoryDiagnostic> diagnostics)
        {
            foreach (var block in file.Blocks.Values)
            {
                if (block.IsRoot) continue;
                if (block.Steps.Count == 0)
                {
                    diagnostics.Add(new KtoryDiagnostic(
                        KtoryDiagnosticSeverity.Warning,
                        "KTR_W006",
                        $"Section '=== {block.Label} ===' is empty.",
                        block.StartLineNumber > 0 ? block.StartLineNumber : 1,
                        1
                    ));
                }
            }
        }
    }
}
