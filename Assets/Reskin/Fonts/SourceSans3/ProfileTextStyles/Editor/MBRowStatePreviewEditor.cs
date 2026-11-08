using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace qp {

    // Shows the Presets and Colors folders as dropdowns, each with None, and saves a checked pair
    // as a style.
    [CustomEditor(typeof(MBRowStatePreview))]
    public sealed class MBRowStatePreviewEditor : Editor {

        const string Root = "Assets/Reskin/Fonts/SourceSans3/ProfileTextStyles";

        string _styleName = "";

        public override void OnInspectorGUI() {
            serializedObject.Update();

            _Popup<Material>("preset", "Preset", Root + "/Presets", "t:Material");
            _Popup<TMP_ColorGradient>("color", "Color", Root + "/Colors", "t:TMP_ColorGradient");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("place"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isMe"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            _styleName = EditorGUILayout.TextField("Style Name", _styleName);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_styleName)))
                if (GUILayout.Button("Save Style")) _SaveStyle();
        }

        void _Popup<T>(string field, string label, string folder, string filter) where T : Object {
            var property = serializedObject.FindProperty(field);
            var assets = AssetDatabase.FindAssets(filter, new[] { folder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
                .ToArray();

            // None is first, so an asset's place in the list is one past its index.
            var names = new[] { "None" }.Concat(assets.Select(a => a.name)).ToArray();
            int current = System.Array.IndexOf(assets, property.objectReferenceValue) + 1;
            int picked = EditorGUILayout.Popup(label, current, names);
            if (picked != current) property.objectReferenceValue = picked == 0 ? null : assets[picked - 1];
        }

        void _SaveStyle() {
            var preview = (MBRowStatePreview)target;
            var style = CreateInstance<ProfileNameStyle>();
            style.preset = preview.preset;
            style.color = preview.color;

            // Unique, so saving under a used name adds a style instead of replacing one.
            string path = AssetDatabase.GenerateUniqueAssetPath($"{Root}/Styles/{_styleName.Trim()}.asset");
            AssetDatabase.CreateAsset(style, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(style);
            _styleName = "";
        }
    }
}
