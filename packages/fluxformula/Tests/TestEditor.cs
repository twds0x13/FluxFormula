using UnityEditor;

public class TestEditor : FluxAssetEditor<float, FloatMathDef>
{
    [MenuItem("Window/FluxFormula/Test Editor")]
    public static void Open() => GetWindow<TestEditor>("Test");
}