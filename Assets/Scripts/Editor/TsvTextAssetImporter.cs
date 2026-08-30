using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Escape.EditorTools
{
    // Unity does not treat ".tsv" as a built-in TextAsset extension, so in a
    // player build Resources.Load<TextAsset>("Data/...") returns null and the
    // game's TSV data (dialogue/item/text/speaker) fails to load. This importer
    // turns every .tsv into a TextAsset whose main object is the file text, so
    // the data loads identically in the editor and in builds.
    [ScriptedImporter(1, "tsv")]
    public sealed class TsvTextAssetImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text = File.ReadAllText(ctx.assetPath);
            var asset = new TextAsset(text);
            ctx.AddObjectToAsset("text", asset);
            ctx.SetMainObject(asset);
        }
    }
}
