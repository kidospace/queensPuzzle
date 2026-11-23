using System.Linq;
using UnityEditor;
using UnityEngine;

namespace qp {

    // Packs what the designer needs to make name styles: the styles folder, the name font,
    // and whatever those use (the row backgrounds, the TMP shader). The game's other presets stay
    // out because nothing in the package points at them.
    public static class ExportNameStyles {

        const string Folder = "Assets/Reskin/Profile/Skins/NameStyles";
        // The scripts the package needs to work on its own: the style asset, the example row and its tools.
        const string Scripts = "Assets/Scripts/Profile/Skins/NameStyles";
        const string Font = "Assets/Reskin/Fonts/SourceSans3/SourceSans3-Black SDF.asset";

        [MenuItem("QueensPuzzle/Export Name Styles")]
        public static void Run() {
            string path = EditorUtility.SaveFilePanel("Export Name Styles", "", "NameStyles", "unitypackage");
            if (string.IsNullOrEmpty(path)) return;

            // Dependencies are listed by hand: ExportPackageOptions.IncludeDependencies can drag in
            // every script in the project.
            var roots = AssetDatabase.FindAssets("", new[] { Folder }).Select(AssetDatabase.GUIDToAssetPath).Append(Font).ToArray();
            var paths = AssetDatabase.GetDependencies(roots, true)
                .Where(p => p.StartsWith("Assets/") && !p.EndsWith(".cs"))
                .Append(Folder).Append(Scripts).ToArray();

            AssetDatabase.ExportPackage(paths, path, ExportPackageOptions.Recurse);

            Debug.Log($"[ExportNameStyles] {path}\n{string.Join("\n", paths)}");
            EditorUtility.RevealInFinder(path);
        }
    }
}
