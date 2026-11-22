using System.IO;
using System.Xml;
using Common.BuildCenter;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;

namespace QueensPuzzle
{
    /// <summary>
    /// Pawdoku only: tells Google Play the app does not need Wi-Fi hardware.
    ///
    /// AppLovin's SDK adds the permission ACCESS_WIFI_STATE; from it Google Play infers that
    /// android.hardware.wifi is REQUIRED, and since Nov 2026 Play Console flags it ("Required hardware
    /// feature declarations are restricting your app's availability"). This adds
    ///   &lt;uses-feature android:name="android.hardware.wifi" android:required="false" /&gt;
    /// to the manifest Unity generates, in every build. Nothing changes at run time: the permission
    /// stays and AppLovin reads the Wi-Fi state as before - only the store stops filtering out devices
    /// without Wi-Fi.
    /// </summary>
    public sealed class WifiNotRequiredSection : BuildCenterSection
    {
        const string Feature = "android.hardware.wifi";
        const string AndroidNs = "http://schemas.android.com/apk/res/android";

        // Between Gradle cache (-90) and the after-build git check (-60)
        public override int callbackOrder => -80;

        public override string Title => "Manifest: Wi-Fi not required (Google Play)";
        public override bool AppliesTo(BuildTarget target) => target == BuildTarget.Android;

        public override Run InTest => Run.Required;
        public override Run InDeploy => Run.Required;
        public override bool InUnityBuild => true;   // the manifest a store build ships with

        protected override SectionStatus CurrentStatus =>
            SectionStatus.Of(SectionState.Ok, "Adds android.hardware.wifi required=\"false\" to the manifest");

        protected override CheckWork Check() => null;   // nothing to check: it is written in the build

        public override void DrawDetails()
        {
            EditorGUILayout.LabelField(
                "AppLovin's ACCESS_WIFI_STATE permission makes Google Play require Wi-Fi hardware. Every build adds " +
                "<uses-feature android:name=\"android.hardware.wifi\" android:required=\"false\" /> so devices without " +
                "Wi-Fi are not filtered out. The permission stays; AppLovin works as before.",
                EditorStyles.wordWrappedMiniLabel);
        }

        protected override string RunBeforeBuild(BuildReport report) => null;

        // Into unityLibrary's manifest once Unity exported the Gradle project (merged into the app's)
        class Writer : IPostGenerateGradleAndroidProject
        {
            public int callbackOrder => 0;

            public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
            {
                if (!new WifiNotRequiredSection().RunsIn(BuildCenterRun.Kind)) return;

                var file = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
                if (!File.Exists(file))
                {
                    UnityEngine.Debug.LogWarning($"[Build Center] Wi-Fi not required: no manifest at {file}");
                    return;
                }

                var doc = new XmlDocument();
                doc.Load(file);
                var manifest = doc.DocumentElement;
                if (manifest == null) return;

                XmlElement feature = null;
                foreach (XmlNode node in manifest.ChildNodes)
                {
                    if (node is XmlElement e && e.Name == "uses-feature" && e.GetAttribute("name", AndroidNs) == Feature) feature = e;
                }
                if (feature == null)
                {
                    feature = doc.CreateElement("uses-feature");
                    _set(doc, feature, "name", Feature);
                    manifest.PrependChild(feature);
                }
                _set(doc, feature, "required", "false");
                doc.Save(file);
                UnityEngine.Debug.Log("[Build Center] manifest: android.hardware.wifi required=\"false\"");
            }

            // android:<name>="<value>", with the "android" prefix written out
            static void _set(XmlDocument doc, XmlElement e, string name, string value)
            {
                var a = e.GetAttributeNode(name, AndroidNs) ?? (XmlAttribute)e.SetAttributeNode(doc.CreateAttribute("android", name, AndroidNs));
                a.Value = value;
            }
        }
    }
}
