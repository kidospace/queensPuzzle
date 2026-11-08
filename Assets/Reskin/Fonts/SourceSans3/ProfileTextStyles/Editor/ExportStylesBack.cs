using UnityEditor;

namespace qp {

    // Ships inside the designer's package. Sends back the presets, colors and styles alone, so
    // importing the result into the game cannot overwrite the font or the shaders.
    public static class ExportStylesBack {

        const string Root = "Assets/Reskin/Fonts/SourceSans3/ProfileTextStyles";

        [MenuItem("Profile Styles/Export Back")]
        public static void Run() {
            string path = EditorUtility.SaveFilePanel("Export Back", "", "ProfileTextStyles_Back", "unitypackage");
            if (string.IsNullOrEmpty(path)) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.ExportPackage(new[] { Root + "/Presets", Root + "/Colors", Root + "/Styles" }, path, ExportPackageOptions.Recurse);
            EditorUtility.RevealInFinder(path);
        }
    }
}
