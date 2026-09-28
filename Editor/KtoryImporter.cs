using System;
using System.IO;
using UnityEngine;
using UnityEditor.AssetImporters;
using Ktory.Core.Parser;
using Ktory.Core.Common;

namespace Ktory.Unity.Editor
{
    /// <summary>
    /// ScriptedImporter for Ktory narrative script files (*.ktr).
    /// Generates a TextAsset as the main asset and runs static validation during import.
    /// </summary>
    [ScriptedImporter(1, "ktr")]
    public class KtoryImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string scriptText;
            try
            {
                scriptText = File.ReadAllText(ctx.assetPath);
            }
            catch (Exception ex)
            {
                ctx.LogImportError($"Failed to read file {ctx.assetPath}: {ex.Message}");
                return;
            }

            // Create TextAsset as the main imported asset so it can be assigned to Unity serialized fields
            var textAsset = new TextAsset(scriptText);
            ctx.AddObjectToAsset("main", textAsset);
            ctx.SetMainObject(textAsset);

            // Validate syntax at asset import time
            try
            {
                var ktoryFile = KtoryParser.Parse(scriptText);
            }
            catch (KtoryException ex)
            {
                ctx.LogImportError($"[Ktory Syntax Error] {ctx.assetPath} (Line {ex.Line}, Col {ex.Column}): {ex.Message}");
            }
            catch (Exception ex)
            {
                ctx.LogImportError($"[Ktory Import Error] {ctx.assetPath}: {ex.Message}");
            }
        }
    }
}
