using Common.BuildCenter;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Common {

    /// <summary>
    /// iOS: after Unity wrote the Xcode project, check that only one SDK registers the
    /// UnityAppController (two = only one wins at runtime). On a conflict SingularSetup offers
    /// Singular's swizzle fix, as it did before (a dialog; a warning in batch mode).
    /// </summary>
    public sealed class SingularAppControllerSection : BuildCenterSection {

        // Pawdoku's own number (Common doesn't know Singular): late, after the EDM iOS Resolver wrote the Podfile
        public override int callbackOrder => 10000;
        public override string Title => "Singular: UnityAppController conflict (iOS)";
        public override bool AppliesTo(BuildTarget target) => target == BuildTarget.iOS;

        public override Run InTest => Run.Required;
        public override Run InDeploy => Run.Required;
        public override bool InUnityBuild => true;   // a conflict breaks Singular at runtime

        protected override SectionStatus CurrentStatus =>
            SectionStatus.Of(SectionState.Ok, "Checked after Unity writes the Xcode project");

        protected override CheckWork Check() => null;   // nothing to check

        public override void DrawDetails() {
            EditorGUILayout.LabelField("More than one IMPL_APP_CONTROLLER_SUBCLASS in the export → offers Singular's swizzle fix.",
                EditorStyles.wordWrappedMiniLabel);
        }

        protected override string RunBeforeBuild(BuildReport report) => null;

        protected override string RunAfterUnityBuild(BuildReport report) {
            SingularSetup.OnPostProcessBuildIOS(report.summary.platform, report.summary.outputPath);
            return null;
        }
    }
}
