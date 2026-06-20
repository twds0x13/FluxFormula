using UnityEditor;

public class TestEditor : FluxAssetEditor<float, FloatOp, FloatMathDef>
{
    [MenuItem("Window/FluxFormula/Test Editor")]
    public static void Open() => GetWindow<TestEditor>("Test");
}