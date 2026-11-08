using System.Linq;
using UnityEditor;
using UnityEngine;

namespace qp {

    // Packs what the designer needs to make profile name styles: the styles folder, the name font,
    // and whatever those use (the row backgrounds, the TMP shader). The game's other presets stay
    // out because nothing in the package points at them.
    public static class ExportProfileTextStyles {

        const string Folder = "Assets/Reskin/Fonts/SourceSans3/ProfileTextStyles";
        const string Font = "Assets/Reskin/Fonts/SourceSans3/SourceSans3-Black SDF.asset";

        [MenuItem("QueensPuzzle/Export Profile Text Styles")]
        public static void Run() {
            string path = EditorUtility.SaveFilePanel("Export Profile Text Styles", "", "ProfileTextStyles", "unitypackage");
            if (string.IsNullOrEmpty(path)) return;

            // Dependencies are listed by hand: ExportPackageOptions.IncludeDependencies can drag in
            // every script in the project.
            var roots = AssetDatabase.FindAssets("", new[] { Folder }).Select(AssetDatabase.GUIDToAssetPath).Append(Font).ToArray();
            var paths = AssetDatabase.GetDependencies(roots, true)
                .Where(p => p.StartsWith("Assets/") && !p.EndsWith(".cs"))
                .Append(Folder).ToArray();

            AssetDatabase.ExportPackage(paths, path, ExportPackageOptions.Recurse);

            Debug.Log($"[ExportProfileTextStyles] {path}\n{string.Join("\n", paths)}");
            EditorUtility.RevealInFinder(path);
        }
    }
}
