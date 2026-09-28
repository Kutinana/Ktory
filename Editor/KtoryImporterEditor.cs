using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Ktory.Core.Parser;
using Ktory.Core.Ast;
using Ktory.Core.Common;

namespace Ktory.Unity.Editor
{
    /// <summary>
    /// Custom Inspector for .ktr script files in Unity Editor.
    /// Provides real-time syntax checking, section/block summaries, speaker lists, and source preview.
    /// </summary>
    [CustomEditor(typeof(KtoryImporter))]
    public class KtoryImporterEditor : ScriptedImporterEditor
    {
        private Vector2 _scrollPos;
        private bool _showPreview = true;
        private bool _showBlocks = true;
        private bool _showSpeakers = true;

        public override void OnInspectorGUI()
        {
            var importer = target as KtoryImporter;
            if (importer == null) return;

            string assetPath = importer.assetPath;
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
            {
                base.OnInspectorGUI();
                return;
            }

            string scriptText;
            try
            {
                scriptText = File.ReadAllText(assetPath);
            }
            catch (Exception ex)
            {
                EditorGUILayout.HelpBox($"Failed to read file: {ex.Message}", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Ktory Narrative Script", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            KtoryFile? ktoryFile = null;
            KtoryException? syntaxError = null;

            try
            {
                ktoryFile = KtoryParser.Parse(scriptText);
            }
            catch (KtoryException ex)
            {
                syntaxError = ex;
            }
            catch (Exception ex)
            {
                syntaxError = new KtoryException(ex.Message);
            }

            if (syntaxError != null)
            {
                EditorGUILayout.HelpBox($"Syntax Error at Line {syntaxError.Line}, Column {syntaxError.Column}:\n{syntaxError.Message}", MessageType.Error);
            }
            else if (ktoryFile != null)
            {
                EditorGUILayout.HelpBox($"Script syntax is valid! (Default Language: {ktoryFile.DefaultLang})", MessageType.Info);

                // Blocks / Scenes summary
                _showBlocks = EditorGUILayout.Foldout(_showBlocks, $"Scenes / Blocks ({ktoryFile.Blocks.Count})", true);
                if (_showBlocks)
                {
                    EditorGUI.indentLevel++;
                    foreach (var kvp in ktoryFile.Blocks)
                    {
                        var block = kvp.Value;
                        string prefix = block.IsRoot ? "[Root] " : "=== ";
                        string suffix = block.IsRoot ? "" : " ===";
                        EditorGUILayout.LabelField($"{prefix}{block.Label}{suffix}", $"Steps: {block.Steps.Count}");
                    }
                    EditorGUI.indentLevel--;
                }

                // Speakers summary
                _showSpeakers = EditorGUILayout.Foldout(_showSpeakers, $"Defined Speakers ({ktoryFile.Speakers.Count})", true);
                if (_showSpeakers)
                {
                    EditorGUI.indentLevel++;
                    if (ktoryFile.Speakers.Count == 0)
                    {
                        EditorGUILayout.LabelField("No explicit @speaker declarations.");
                    }
                    else
                    {
                        foreach (var speaker in ktoryFile.Speakers)
                        {
                            string aliases = string.Join(", ", speaker.Aliases);
                            EditorGUILayout.LabelField(aliases, $"ID: {speaker.Id}");
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space(8);
            _showPreview = EditorGUILayout.Foldout(_showPreview, "Script Source Preview", true);
            if (_showPreview)
            {
                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(180));
                EditorGUILayout.TextArea(scriptText, EditorStyles.textArea);
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(8);
            ApplyRevertGUI();
        }
    }
}
